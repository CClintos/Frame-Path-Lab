using System.Globalization;
using System.Text;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Models;
using FramePathLab.Core.Persistence;
using FramePathLab.Core.Reporting;
using FramePathLab.Core.Services;
using FramePathLab.Core.Statistics;
using FramePathLab.Windows.Power;
using FramePathLab.Windows.Scanning;

namespace FramePathLab.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests =
    [
        ("R7 quantiles are deterministic", TestQuantilesAsync),
        ("CSV analyzer selects CS2 and computes metrics", TestCsvAnalyzerAsync),
        ("CSV analyzer fails closed on ambiguous target", TestAmbiguousCaptureAsync),
        ("CSV analyzer enforces file limits", TestFileLimitAsync),
        ("History store writes, reads and deletes atomically", TestHistoryStoreAsync),
        ("Evidence catalog preserves safety exclusions", TestEvidenceCatalogAsync),
        ("Markdown report labels baseline and safety boundary", TestMarkdownReportAsync),
        ("Windows scanner smoke test", TestWindowsScannerAsync),
        ("Power session applies and restores exact schemes", TestPowerSessionApplyRestoreAsync),
        ("Power session rollback preserves an external third scheme", TestPowerSessionExternalChangeAsync),
        ("Power session rejects DC operation without mutation", TestPowerSessionRejectsDcAsync),
        ("Power session rejects an unavailable target without mutation", TestPowerSessionRejectsUnavailableTargetAsync),
        ("Power session rejects drift after approval without mutation", TestPowerSessionRejectsApprovalDriftAsync),
        ("Power session preflights Group Policy without mutation", TestPowerSessionPolicyPreflightAsync),
        ("Power session guardian failure prevents system mutation", TestPowerSessionGuardianArmFailureAsync),
        ("Power session recovers when target setter changes then throws", TestPowerSessionSetterThrowsAfterChangeAsync),
        ("Power journal round-trips and rejects tampering", TestPowerJournalIntegrityAsync)
    ];

    public static async Task<int> Main()
    {
        var failures = new List<string>();
        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.Message}");
                Console.WriteLine($"FAIL  {name}\n      {exception}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{Tests.Count - failures.Count}/{Tests.Count} tests passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Task TestQuantilesAsync()
    {
        double[] values = [1, 2, 3, 4, 5];
        AssertNear(3, DescriptiveStatistics.QuantileR7(values, 0.5), 1e-12, "median");
        AssertNear(4.6, DescriptiveStatistics.QuantileR7(values, 0.9), 1e-12, "p90");
        AssertNear(3, DescriptiveStatistics.Mean(values), 1e-12, "mean");
        return Task.CompletedTask;
    }

    private static async Task TestCsvAnalyzerAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var path = Path.Combine(directory, "capture.csv");
            var builder = new StringBuilder("Application,ProcessID,FrameTime,CPUBusy,GPUBusy,PresentMode,AllowsTearing\n");
            for (var index = 0; index < 200; index++)
            {
                var application = index < 150 ? "cs2.exe" : "other.exe";
                var frameTime = 3.5 + ((index % 11) * 0.2);
                builder.AppendLine(FormattableString.Invariant($"{application},730,{frameTime},2.1,1.8,Hardware: Independent Flip,1"));
            }

            await File.WriteAllTextAsync(path, builder.ToString());
            var result = await new PresentMonCsvAnalyzer().AnalyzeAsync(path, new CaptureAnalysisOptions(4.1667));
            AssertEqual("cs2.exe", result.SelectedApplication, "selected application");
            AssertEqual(150L, result.AcceptedRows, "accepted rows");
            AssertEqual(ResultOutcome.BaselineOnly, result.Outcome, "outcome");
            Assert(result.Metrics.Any(metric => metric.Id == "p99_frame_ms" && metric.Value.HasValue), "p99 metric is missing");
            Assert(result.Warnings.Any(warning => warning.Contains("other applications", StringComparison.OrdinalIgnoreCase)), "other-app warning is missing");
        });
    }

    private static async Task TestAmbiguousCaptureAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var path = Path.Combine(directory, "ambiguous.csv");
            await File.WriteAllTextAsync(path, "Application,FrameTime\nfoo.exe,4.0\nbar.exe,4.1\n");
            var result = await new PresentMonCsvAnalyzer().AnalyzeAsync(path, new CaptureAnalysisOptions());
            AssertEqual(ResultOutcome.Invalid, result.Outcome, "ambiguous outcome");
            Assert(result.Warnings.Any(warning => warning.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)), "ambiguous warning is missing");
        });
    }

    private static async Task TestFileLimitAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var path = Path.Combine(directory, "oversized.csv");
            await File.WriteAllTextAsync(path, "Application,FrameTime\ncs2.exe,4.0\n");
            await AssertThrowsAsync<InvalidDataException>(() =>
                new PresentMonCsvAnalyzer().AnalyzeAsync(path, new CaptureAnalysisOptions(MaximumFileBytes: 4)));
        });
    }

    private static async Task TestHistoryStoreAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var store = new JsonHistoryStore(directory);
            var entry = new HistoryEntry(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                "test",
                "fixture",
                "BaselineOnly",
                "fixture.csv",
                new string('a', 64),
                new Dictionary<string, double> { ["p99"] = 4.2 });
            await store.AppendAsync(entry);
            var read = await store.ReadAsync();
            AssertEqual(1, read.Count, "history count");
            AssertEqual(entry.Id, read[0].Id, "history id");
            await store.DeleteAllAsync();
            AssertEqual(0, (await store.ReadAsync()).Count, "history count after delete");
        });
    }

    private static Task TestEvidenceCatalogAsync()
    {
        var snapshot = CreateSnapshot();
        var findings = new DefaultEvidenceCatalog().Evaluate(snapshot);
        Assert(findings.Any(finding => finding.Id == "EXCLUDE-001" && finding.Disposition == FindingDisposition.Excluded), "excluded-tweaks card missing");
        Assert(findings.Any(finding => finding.Id == "SAFE-001"), "safety card missing");
        Assert(findings.All(finding => !finding.CanProduceCausalDecision), "prototype unexpectedly enabled a causal decision");
        return Task.CompletedTask;
    }

    private static Task TestMarkdownReportAsync()
    {
        var snapshot = CreateSnapshot();
        var findings = new DefaultEvidenceCatalog().Evaluate(snapshot);
        var scan = new ScanReport(snapshot, findings, "test", "scan-v1");
        var content = new MarkdownReportWriter().Build(scan, null);
        Assert(content.Contains("Observational diagnostic report", StringComparison.Ordinal), "report disclaimer missing");
        Assert(content.Contains("Creating this report does not change Windows", StringComparison.Ordinal), "report safety boundary missing");
        return Task.CompletedTask;
    }

    private static async Task TestWindowsScannerAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        Assert(!string.IsNullOrWhiteSpace(snapshot.OsVersion), "OS version missing");
        Assert(snapshot.LogicalProcessorCount > 0, "processor count invalid");
        Assert(snapshot.CapturedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(2), "capture timestamp invalid");
    }

    private static Task TestPowerSessionApplyRestoreAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var highPerformance = PowerSessionCoordinator.HighPerformanceSchemeId;
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(highPerformance, "High performance"));
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        var applied = coordinator.ApplyHighPerformance(
            ownerProcessId: 730,
            ownerProcessStartTimeUtcTicks: 123456789,
            expectedOriginalSchemeId: balanced,
            isOnAcPower: true,
            leaseDuration: TimeSpan.FromMinutes(15));

        AssertEqual(PowerSessionState.AppliedVerified, applied.Record.State, "applied state");
        AssertEqual(balanced, applied.Record.OriginalSchemeId, "recorded original scheme");
        AssertEqual(highPerformance, applied.Record.TargetSchemeId, "recorded target scheme");
        AssertEqual(highPerformance, applied.ObservedSchemeId, "observed applied scheme");
        AssertEqual(highPerformance, controller.ActiveSchemeId, "active scheme after apply");
        AssertEqual(1, controller.SetCalls.Count, "set count after apply");
        AssertEqual(highPerformance, controller.SetCalls[0], "exact applied scheme");
        AssertEqual(1, guardian.ArmCalls.Count, "guardian arm count");
        AssertEqual(applied.Record.SessionId, guardian.ArmCalls[0].SessionId, "guardian session id");
        AssertEqual(applied.Record.GuardianNonce, guardian.ArmCalls[0].Nonce, "guardian nonce");
        AssertEqual(730, guardian.ArmCalls[0].OwnerProcessId, "guardian owner process id");

        var reverted = coordinator.Revert(applied.Record.SessionId, "unit-test completion");

        AssertEqual(PowerSessionState.RevertedVerified, reverted.Record.State, "reverted state");
        AssertEqual(balanced, reverted.ObservedSchemeId, "observed restored scheme");
        AssertEqual(balanced, controller.ActiveSchemeId, "active scheme after restore");
        AssertEqual(2, controller.SetCalls.Count, "total set count");
        AssertEqual(balanced, controller.SetCalls[1], "exact restored scheme");
        AssertEqual(3, journal.Writes.Count, "durable transition count");
        AssertEqual(PowerSessionState.Prepared, journal.Writes[0].State, "first journal state");
        AssertEqual(PowerSessionState.AppliedVerified, journal.Writes[1].State, "second journal state");
        AssertEqual(PowerSessionState.RevertedVerified, journal.Writes[2].State, "final journal state");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionExternalChangeAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var highPerformance = PowerSessionCoordinator.HighPerformanceSchemeId;
        var external = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(highPerformance, "High performance"),
            new PowerSchemeDescriptor(external, "External plan"));
        var journal = new InMemoryPowerSessionJournal();
        var coordinator = new PowerSessionCoordinator(controller, journal, new InMemoryPowerSessionGuardian());
        var applied = coordinator.ApplyHighPerformance(730, 123456789, balanced, true, TimeSpan.FromMinutes(15));

        controller.SimulateExternalChange(external);
        var reverted = coordinator.Revert(applied.Record.SessionId, "unit-test completion");

        AssertEqual(PowerSessionState.ExternalChange, reverted.Record.State, "external-change state");
        AssertEqual(external, reverted.ObservedSchemeId, "preserved external scheme");
        AssertEqual(external, controller.ActiveSchemeId, "active external scheme");
        AssertEqual(1, controller.SetCalls.Count, "coordinator must not overwrite third scheme");
        AssertEqual(highPerformance, controller.SetCalls[0], "only coordinator write");
        AssertEqual(PowerSessionState.ExternalChange, journal.Current!.State, "journal conflict state");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionRejectsDcAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(PowerSessionCoordinator.HighPerformanceSchemeId, "High performance"));
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        AssertThrows<InvalidOperationException>(() =>
            coordinator.ApplyHighPerformance(730, 123456789, balanced, false, TimeSpan.FromMinutes(15)));

        AssertEqual(balanced, controller.ActiveSchemeId, "active scheme after DC rejection");
        AssertEqual(0, controller.SetCalls.Count, "set count after DC rejection");
        AssertEqual(0, journal.Writes.Count, "journal writes after DC rejection");
        AssertEqual(0, guardian.ArmCalls.Count, "guardian calls after DC rejection");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionRejectsUnavailableTargetAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"));
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        AssertThrows<InvalidOperationException>(() =>
            coordinator.ApplyHighPerformance(730, 123456789, balanced, true, TimeSpan.FromMinutes(15)));

        AssertEqual(balanced, controller.ActiveSchemeId, "active scheme after unavailable-target rejection");
        AssertEqual(0, controller.SetCalls.Count, "set count after unavailable-target rejection");
        AssertEqual(0, journal.Writes.Count, "journal writes after unavailable-target rejection");
        AssertEqual(0, guardian.ArmCalls.Count, "guardian calls after unavailable-target rejection");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionRejectsApprovalDriftAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var external = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
        var controller = new InMemoryPowerSchemeController(
            external,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(external, "External plan"),
            new PowerSchemeDescriptor(PowerSessionCoordinator.HighPerformanceSchemeId, "High performance"));
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        AssertThrows<InvalidOperationException>(() => coordinator.ApplyHighPerformance(
            730,
            123456789,
            balanced,
            true,
            TimeSpan.FromMinutes(15)));

        AssertEqual(external, controller.ActiveSchemeId, "externally selected plan after approval drift");
        AssertEqual(0, controller.SetCalls.Count, "set count after approval drift");
        AssertEqual(0, journal.Writes.Count, "journal writes after approval drift");
        AssertEqual(0, guardian.ArmCalls.Count, "guardian calls after approval drift");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionGuardianArmFailureAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var highPerformance = PowerSessionCoordinator.HighPerformanceSchemeId;
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(highPerformance, "High performance"));
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian
        {
            ArmFailure = new InvalidOperationException("Injected guardian arm failure.")
        };
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        AssertThrows<InvalidOperationException>(() => coordinator.ApplyHighPerformance(
            730,
            123456789,
            balanced,
            true,
            TimeSpan.FromMinutes(15)));

        AssertEqual(balanced, controller.ActiveSchemeId, "active scheme after guardian failure");
        AssertEqual(0, controller.SetCalls.Count, "setter calls after guardian failure");
        AssertEqual(1, guardian.ArmCalls.Count, "guardian arm attempt count");
        AssertEqual(2, journal.Writes.Count, "journal transition count after guardian failure");
        AssertEqual(PowerSessionState.Prepared, journal.Writes[0].State, "guardian-failure initial journal state");
        AssertEqual(PowerSessionState.RevertedVerified, journal.Writes[1].State, "guardian-failure terminal journal state");
        AssertEqual(balanced, journal.Current!.OriginalSchemeId, "guardian-failure recorded original plan");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionPolicyPreflightAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(PowerSessionCoordinator.HighPerformanceSchemeId, "High performance"))
        {
            PolicyFailure = new UnauthorizedAccessException("Blocked by test policy.")
        };
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        var overview = coordinator.Inspect();
        Assert(!overview.HighPerformancePolicyAllowed, "policy-blocked plan was reported eligible");
        Assert(overview.PolicyStatus.Contains("restricted", StringComparison.OrdinalIgnoreCase), "policy status did not explain restriction");
        AssertThrows<UnauthorizedAccessException>(() => coordinator.ApplyHighPerformance(
            730,
            123456789,
            balanced,
            true,
            TimeSpan.FromMinutes(15)));
        AssertEqual(0, controller.SetCalls.Count, "set calls after policy rejection");
        AssertEqual(0, journal.Writes.Count, "journal writes after policy rejection");
        AssertEqual(0, guardian.ArmCalls.Count, "guardian calls after policy rejection");
        return Task.CompletedTask;
    }

    private static Task TestPowerSessionSetterThrowsAfterChangeAsync()
    {
        var balanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var highPerformance = PowerSessionCoordinator.HighPerformanceSchemeId;
        var controller = new InMemoryPowerSchemeController(
            balanced,
            new PowerSchemeDescriptor(balanced, "Balanced"),
            new PowerSchemeDescriptor(highPerformance, "High performance"))
        {
            ThrowAfterChangingToSchemeId = highPerformance
        };
        var journal = new InMemoryPowerSessionJournal();
        var guardian = new InMemoryPowerSessionGuardian();
        var coordinator = new PowerSessionCoordinator(controller, journal, guardian);

        AssertThrows<InvalidOperationException>(() => coordinator.ApplyHighPerformance(
            730,
            123456789,
            balanced,
            true,
            TimeSpan.FromMinutes(15)));

        AssertEqual(balanced, controller.ActiveSchemeId, "active scheme after setter failure recovery");
        AssertEqual(2, controller.SetCalls.Count, "setter call count after recovery");
        AssertEqual(highPerformance, controller.SetCalls[0], "failed target setter call");
        AssertEqual(balanced, controller.SetCalls[1], "exact recovery setter call");
        AssertEqual(1, guardian.ArmCalls.Count, "guardian arm count before setter failure");
        AssertEqual(2, journal.Writes.Count, "journal transition count after setter failure");
        AssertEqual(PowerSessionState.Prepared, journal.Writes[0].State, "setter-failure initial journal state");
        AssertEqual(PowerSessionState.RevertedVerified, journal.Writes[1].State, "setter-failure terminal journal state");
        AssertEqual(balanced, journal.Current!.OriginalSchemeId, "setter-failure recorded original plan");
        return Task.CompletedTask;
    }

    private static async Task TestPowerJournalIntegrityAsync()
    {
        await WithTemporaryDirectoryAsync(directory =>
        {
            var store = new PowerSessionJournalStore(directory);
            var created = DateTimeOffset.UtcNow;
            var record = new PowerSessionRecord(
                Guid.NewGuid(),
                PowerSessionCoordinator.Operation,
                PowerSessionCoordinator.SchemaVersion,
                created,
                created,
                created.AddMinutes(15),
                730,
                123456789,
                Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
                "Balanced",
                PowerSessionCoordinator.HighPerformanceSchemeId,
                "High performance",
                PowerSessionState.Prepared,
                Guid.NewGuid(),
                "Prepared in fake-only journal test.",
                null);
            store.Write(record);
            var roundTrip = store.Read();
            Assert(roundTrip is not null, "journal round-trip returned null");
            AssertEqual(record.SessionId, roundTrip!.SessionId, "journal session id");
            AssertEqual(record.OriginalSchemeId, roundTrip.OriginalSchemeId, "journal original plan");

            var path = Path.Combine(directory, "power-session.v1.json");
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text.Replace("Balanced", "Tampered", StringComparison.Ordinal));
            AssertThrows<InvalidDataException>(() => store.Read());

            var fallbackDirectory = Path.Combine(directory, "fallback");
            var fallbackStore = new PowerSessionJournalStore(fallbackDirectory);
            fallbackStore.Write(record);
            fallbackStore.Write(record with
            {
                State = PowerSessionState.AppliedVerified,
                UpdatedAtUtc = created.AddSeconds(1),
                LastObservation = "Applied in fake-only journal test."
            });
            var currentPath = Path.Combine(fallbackDirectory, "power-session.v1.json");
            var currentText = File.ReadAllText(currentPath);
            File.WriteAllText(currentPath, currentText.Replace("High performance", "Tampered target", StringComparison.Ordinal));
            var recovered = fallbackStore.Read();
            Assert(recovered is not null, "validated previous journal was not recovered");
            AssertEqual(PowerSessionState.Prepared, recovered!.State, "fallback journal state");
            return Task.CompletedTask;
        });
    }

    private static EnvironmentSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            "Windows",
            "10.0",
            true,
            false,
            false,
            16,
            32UL * 1024 * 1024 * 1024,
            [new DisplaySnapshot("DISPLAY1", "NVIDIA test adapter", "Test monitor", true, true, 1920, 1080, 32, 240, 240, [60, 120, 240])],
            new SteamGameSnapshot(true, true, false, "123", "Installed"),
            new PowerSnapshot(true, -1, "scheme", "AC"),
            [],
            CapabilityState.Supported,
            []);

    private static async Task WithTemporaryDirectoryAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FramePathLabTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await action(directory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void AssertNear(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{message}: expected {expected.ToString(CultureInfo.InvariantCulture)}, actual {actual.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private sealed class InMemoryPowerSchemeController(
        Guid activeSchemeId,
        params PowerSchemeDescriptor[] schemes) : IPowerSchemeController
    {
        private readonly IReadOnlyList<PowerSchemeDescriptor> _schemes = schemes;

        public Guid ActiveSchemeId { get; private set; } = activeSchemeId;

        public List<Guid> SetCalls { get; } = [];

        public Guid? ThrowAfterChangingToSchemeId { get; init; }

        public Exception? PolicyFailure { get; init; }

        private bool HasInjectedSetFailure { get; set; }

        public IReadOnlyList<PowerSchemeDescriptor> EnumerateSchemes() => _schemes;

        public Guid GetActiveScheme() => ActiveSchemeId;

        public void EnsureCanSetActiveScheme(Guid schemeId)
        {
            if (PolicyFailure is not null)
            {
                throw PolicyFailure;
            }
        }

        public void SetActiveScheme(Guid schemeId)
        {
            if (!_schemes.Any(scheme => scheme.Id == schemeId))
            {
                throw new InvalidOperationException($"Scheme {schemeId:D} is unavailable in the fake controller.");
            }

            SetCalls.Add(schemeId);
            ActiveSchemeId = schemeId;
            if (!HasInjectedSetFailure && schemeId == ThrowAfterChangingToSchemeId)
            {
                HasInjectedSetFailure = true;
                throw new InvalidOperationException("Injected setter failure after changing the active scheme.");
            }
        }

        public void SimulateExternalChange(Guid schemeId)
        {
            if (!_schemes.Any(scheme => scheme.Id == schemeId))
            {
                throw new InvalidOperationException($"External scheme {schemeId:D} is unavailable in the fake controller.");
            }

            ActiveSchemeId = schemeId;
        }
    }

    private sealed class InMemoryPowerSessionJournal : IPowerSessionJournal
    {
        public PowerSessionRecord? Current { get; private set; }

        public List<PowerSessionRecord> Writes { get; } = [];

        public PowerSessionRecord? Read() => Current;

        public void Write(PowerSessionRecord record)
        {
            Current = record;
            Writes.Add(record);
        }
    }

    private sealed class InMemoryPowerSessionGuardian : IPowerSessionGuardian
    {
        public List<(Guid SessionId, Guid Nonce, int OwnerProcessId)> ArmCalls { get; } = [];

        public Exception? ArmFailure { get; init; }

        public void Arm(Guid sessionId, Guid nonce, int ownerProcessId)
        {
            ArmCalls.Add((sessionId, nonce, ownerProcessId));
            if (ArmFailure is not null)
            {
                throw ArmFailure;
            }
        }

        public bool IsArmed(Guid sessionId)
            => ArmCalls.Any(call => call.SessionId == sessionId);
    }
}

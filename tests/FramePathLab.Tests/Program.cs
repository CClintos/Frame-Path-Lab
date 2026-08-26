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
using FramePathLab.Windows.Mutation;
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
        ("Power journal round-trips and rejects tampering", TestPowerJournalIntegrityAsync),
        ("Mutation executor applies and restores a registry value", TestMutationRoundTripAsync),
        ("Mutation executor removes a value it created", TestMutationRemovesCreatedValueAsync),
        ("Mutation executor preserves an external change on revert", TestMutationPreservesExternalChangeAsync),
        ("Tweak journal round-trips and rejects tampering", TestTweakJournalIntegrityAsync),
        ("Expert engine applies, journals and reverts", TestExpertEngineApplyRevertAsync),
        ("Expert engine blocks elevated writes when not elevated", TestExpertEngineElevationGateAsync),
        ("Expert engine rolls back a partial apply", TestExpertEnginePartialApplyAsync),
        ("Expert engine fails closed when a before-state is unreadable", TestExpertEngineFailsClosedAsync),
        ("Delivery analyzer flags a composed present path", TestDeliveryComposedPathAsync),
        ("Delivery analyzer classifies the limiting stage", TestDeliveryBoundClassAsync),
        ("Delivery analyzer reads vertical sync from the capture", TestDeliverySyncIntervalAsync),
        ("CPU topology resolves core groups", TestCpuTopologyAsync),
        ("Display timing returns an exact rational refresh", TestDisplayTimingAsync),
        ("Expert catalogue evaluates without mutating", TestExpertCatalogueReadOnlyAsync),
        ("SMBIOS reports a self-consistent memory configuration", TestSmbiosMemoryAsync),
        ("Stacked cache is detected from cache per core", TestStackedCacheDetectionAsync),
        ("Platform timer frequency is read and classified", TestPlatformTimerAsync),
        ("Audio endpoints report a plausible shared format", TestAudioEndpointsAsync),
        ("Panel identity is self-consistent", TestPanelIdentityAsync),
        ("Driver profile degrades cleanly when absent", TestNvidiaProfileAsync),
        ("Excluded tweaks never offer a mutation", TestDebunkRegisterAsync)
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

    // ---- Expert tier ----------------------------------------------------------------------

    private const string TestRegistryPath = @"HKCU\Software\FramePathLabTests";

    private static MutationPlan TestRegistryPlan(string valueName, string desired)
        => new(
            $"test.{valueName}",
            MutationKind.RegistryValue,
            TestRegistryPath,
            valueName,
            desired,
            "DWord",
            $"Test mutation for {valueName}");

    private static void ClearTestRegistry()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software", writable: true);
        key?.DeleteSubKeyTree("FramePathLabTests", throwOnMissingSubKey: false);
    }

    private static Task TestMutationRoundTripAsync()
    {
        ClearTestRegistry();
        try
        {
            var executor = new WindowsMutationExecutor();
            var seed = TestRegistryPlan("Existing", "7");
            executor.Apply(seed);

            var plan = TestRegistryPlan("Existing", "42");
            var applied = executor.Apply(plan);
            Assert(applied.ExistedBefore, "value should have existed before the second apply");
            AssertEqual("7", applied.BeforeValue ?? "<null>", "captured before-value");
            AssertEqual("42", applied.ObservedAfterValue ?? "<null>", "read-back after apply");
            Assert(applied.VerifiedAfterWrite, "apply should verify by read-back");

            var reverted = executor.Revert(applied);
            AssertEqual("7", reverted.ObservedAfterValue ?? "<null>", "restored value");
            Assert(reverted.VerifiedAfterWrite, "revert should verify by read-back");
            return Task.CompletedTask;
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static Task TestMutationRemovesCreatedValueAsync()
    {
        ClearTestRegistry();
        try
        {
            var executor = new WindowsMutationExecutor();
            var plan = TestRegistryPlan("Created", "1");
            var applied = executor.Apply(plan);
            Assert(!applied.ExistedBefore, "value must not have existed before");

            var reverted = executor.Revert(applied);
            executor.Read(plan, out var stillExists);
            Assert(!stillExists, "a value FramePath Lab created must be removed on revert");
            Assert(reverted.VerifiedAfterWrite, "removal should verify");
            return Task.CompletedTask;
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static Task TestMutationPreservesExternalChangeAsync()
    {
        ClearTestRegistry();
        try
        {
            var executor = new WindowsMutationExecutor();
            executor.Apply(TestRegistryPlan("External", "1"));
            var applied = executor.Apply(TestRegistryPlan("External", "2"));

            // Something else takes ownership of the value after we wrote it.
            executor.Apply(TestRegistryPlan("External", "99"));

            var reverted = executor.Revert(applied);
            executor.Read(TestRegistryPlan("External", "0"), out _);
            AssertEqual("99", reverted.ObservedAfterValue ?? "<null>", "external value must survive the revert");
            Assert(
                reverted.Observation.StartsWith("Left unchanged", StringComparison.Ordinal),
                "revert should report that it preserved a newer external change");
            return Task.CompletedTask;
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static async Task TestTweakJournalIntegrityAsync()
    {
        await WithTemporaryDirectoryAsync(directory =>
        {
            var store = new TweakJournalStore(directory);
            var transaction = new TweakTransaction(
                Guid.NewGuid(),
                "TEST-001",
                "Test tweak",
                DateTimeOffset.UtcNow,
                null,
                false,
                [],
                TweakTransaction.StateApplied,
                "applied");
            store.Upsert(transaction);

            var read = store.Read();
            AssertEqual(1, read.Count, "journal entry count");
            AssertEqual("TEST-001", read[0].TweakId, "round-tripped tweak id");
            Assert(read[0].IsOutstanding, "applied transaction should be outstanding");

            var path = Path.Combine(directory, "expert-tweaks.v1.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("TEST-001", "TEST-XXX", StringComparison.Ordinal));
            AssertThrows<InvalidDataException>(() => store.Read());
            return Task.CompletedTask;
        });
    }

    private static ExpertTweakCard BuildTestCard(
        string id,
        IReadOnlyList<MutationPlan> plan,
        TweakState state = TweakState.Suboptimal)
        => new(
            new ExpertTweakDefinition(
                id, "Test", $"{id} title", "mechanism", "rationale", "tradeoff",
                TweakRisk.Low, TweakScope.CurrentUser, EvidenceQuality.Moderate,
                false, false, false, []),
            new TweakReading(state, "current", "recommended", "detail"),
            plan,
            null);

    private static async Task TestExpertEngineApplyRevertAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var engine = new ExpertTweakEngine(
                    new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: true);

                var card = BuildTestCard("ENGINE-001", [
                    TestRegistryPlan("EngineA", "1"),
                    TestRegistryPlan("EngineB", "2")
                ]);

                var applied = engine.Apply(card);
                AssertEqual(TweakTransaction.StateApplied, applied.State, "applied state");
                AssertEqual(1, engine.OutstandingTransactions().Count, "outstanding count after apply");

                var reverted = engine.Revert(applied.TransactionId, "test");
                AssertEqual(TweakTransaction.StateReverted, reverted.State, "reverted state");
                AssertEqual(0, engine.OutstandingTransactions().Count, "outstanding count after revert");

                var executor = new WindowsMutationExecutor();
                executor.Read(TestRegistryPlan("EngineA", "0"), out var aExists);
                executor.Read(TestRegistryPlan("EngineB", "0"), out var bExists);
                Assert(!aExists && !bExists, "both created values must be removed on revert");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static async Task TestExpertEngineElevationGateAsync()
    {
        await WithTemporaryDirectoryAsync(directory =>
        {
            var engine = new ExpertTweakEngine(
                new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: false);

            var machinePlan = new MutationPlan(
                "test.machine",
                MutationKind.RegistryValue,
                @"HKLM\SOFTWARE\FramePathLabTests",
                "Value",
                "1",
                "DWord",
                "machine-scope test mutation");

            var gated = typeof(ExpertTweakEngine)
                .GetMethod("GateOnElevation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(engine, [BuildTestCard("ELEV-001", [machinePlan])]) as ExpertTweakCard;

            Assert(gated is not null, "gate returned nothing");
            Assert(gated!.BlockedReason is not null, "machine-scope write must be blocked without elevation");
            Assert(!gated.CanApply, "blocked card must not be applicable");
            return Task.CompletedTask;
        });
    }

    private static async Task TestExpertEnginePartialApplyAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var engine = new ExpertTweakEngine(
                    new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: true);

                // The second mutation reads cleanly (the process simply is not running) but cannot
                // be written, so the first one lands and then the tweak fails mid-apply.
                var card = BuildTestCard("PARTIAL-001", [
                    TestRegistryPlan("PartialA", "5"),
                    new MutationPlan(
                        "test.absent-process",
                        MutationKind.ProcessAffinity,
                        "framepathlab-no-such-process",
                        "ProcessorAffinity",
                        "1",
                        "Mask",
                        "affinity on a process that is not running")
                ]);

                var result = engine.Apply(card);
                AssertEqual(TweakTransaction.StateReverted, result.State,
                    "a failed apply must automatically roll back and report a clean revert");

                var executor = new WindowsMutationExecutor();
                executor.Read(TestRegistryPlan("PartialA", "0"), out var exists);
                Assert(!exists, "the mutation that did land must be undone");
                Assert(
                    result.Mutations.Any(record => !record.AttemptedWrite),
                    "the unwritten mutation must be recorded as never attempted");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static async Task TestExpertEngineFailsClosedAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var journal = new TweakJournalStore(directory);
                var engine = new ExpertTweakEngine(new WindowsMutationExecutor(), journal, isElevated: true);

                // An unreadable target means no before-state can be captured, so nothing may be
                // written at all: applying it would create a change that could never be undone.
                var card = BuildTestCard("FAILCLOSED-001", [
                    TestRegistryPlan("NeverWritten", "5"),
                    new MutationPlan("test.bad-hive", MutationKind.RegistryValue, @"HKXX\Nope", "V", "1", "DWord", "bad")
                ]);

                AssertThrows<ArgumentException>(() => engine.Apply(card));

                new WindowsMutationExecutor().Read(TestRegistryPlan("NeverWritten", "0"), out var exists);
                Assert(!exists, "no value may be written when a before-state cannot be captured");
                AssertEqual(0, journal.Read().Count, "no transaction may be journalled for a refused apply");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static Task TestDeliveryComposedPathAsync()
    {
        var findings = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["Composed: Flip"] = 1000 },
            [4.0, 4.1, 4.0],
            [], [], [], [], [], 0);

        var path = findings.Single(finding => finding.Id == "PRESENT-PATH");
        AssertEqual(DeliverySeverity.Costly, path.Severity, "composed path severity");

        var good = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["Hardware: Independent Flip"] = 1000 },
            [4.0, 4.1, 4.0],
            [], [], [], [], [], 0);
        AssertEqual(DeliverySeverity.Good, good.Single(finding => finding.Id == "PRESENT-PATH").Severity,
            "independent flip severity");
        return Task.CompletedTask;
    }

    private static Task TestDeliveryBoundClassAsync()
    {
        var cpuBound = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            [4.0, 4.0],
            [5.0, 5.0, 5.0],
            [1.0, 1.0, 1.0],
            [], [], [], 0);
        Assert(
            cpuBound.Single(finding => finding.Id == "BOUND-CLASS").Observed.Contains("CPU-bound", StringComparison.Ordinal),
            "should classify as CPU-bound");

        var gpuBound = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            [4.0, 4.0],
            [1.0, 1.0, 1.0],
            [5.0, 5.0, 5.0],
            [], [], [], 0);
        Assert(
            gpuBound.Single(finding => finding.Id == "BOUND-CLASS").Observed.Contains("GPU-bound", StringComparison.Ordinal),
            "should classify as GPU-bound");
        return Task.CompletedTask;
    }

    private static Task TestDeliverySyncIntervalAsync()
    {
        var synced = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            [4.0, 4.0], [], [],
            [1, 1, 1, 1],
            [], [], 0);
        AssertEqual(DeliverySeverity.Advisory,
            synced.Single(finding => finding.Id == "SYNC-INTERVAL").Severity, "vsync-on severity");

        var unsynced = FrameDeliveryAnalyzer.Analyze(
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            [4.0, 4.0], [], [],
            [0, 0, 0, 0],
            [], [], 0);
        AssertEqual(DeliverySeverity.Good,
            unsynced.Single(finding => finding.Id == "SYNC-INTERVAL").Severity, "vsync-off severity");
        return Task.CompletedTask;
    }

    private static Task TestCpuTopologyAsync()
    {
        var topology = CpuTopologyScanner.Scan(null);
        Assert(topology.LogicalProcessorCount > 0, "logical processor count must be positive");
        Assert(topology.CoreGroups.Count > 0, "at least one core group must be resolved");
        Assert(topology.SystemAffinityMask != 0, "system affinity mask must be readable");

        // Every core group must be a subset of what the system actually offers.
        foreach (var group in topology.CoreGroups)
        {
            Assert(
                (group.AffinityMask & topology.SystemAffinityMask) == group.AffinityMask,
                $"core group {group.GroupIndex} escapes the system affinity mask");
        }

        // A preferred mask is only ever offered when it is a genuine subset of the whole machine.
        if (topology.HasDistinctPreferredGroup)
        {
            Assert(topology.PreferredAffinityMask != topology.SystemAffinityMask,
                "a preferred mask equal to the whole system is not a placement decision");
        }

        return Task.CompletedTask;
    }

    private static Task TestDisplayTimingAsync()
    {
        var timings = DisplayTimingScanner.Scan();
        foreach (var timing in timings)
        {
            Assert(timing.VerticalDenominator != 0, "rational refresh must have a non-zero denominator");
            Assert(timing.ExactRefreshHz is > 20 and < 1000, $"implausible refresh {timing.ExactRefreshHz}");
            Assert(timing.RecommendedVrrCap < timing.ExactRefreshHz,
                "the computed cap must sit below the refresh ceiling");
        }

        return Task.CompletedTask;
    }

    private static async Task TestExpertCatalogueReadOnlyAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        var context = await new ExpertScanCoordinator().ScanAsync(
            snapshot, measureInput: false, measureScheduler: false, TimeSpan.Zero);

        var reader = new CountingReader(new WindowsMutationExecutor());
        var cards = ExpertTweakCatalog.Evaluate(context, reader);

        Assert(cards.Count > 0, "catalogue produced no cards");
        Assert(reader.ReadCount > 0, "catalogue never read live state");

        foreach (var card in cards)
        {
            // The core invariant: a card only offers writes when it found something to fix.
            if (card.Reading.State is TweakState.Optimal or TweakState.NotApplicable or TweakState.Unknown)
            {
                AssertEqual(0, card.Plan.Count,
                    $"{card.Definition.Id} offers a mutation despite reporting {card.Reading.State}");
            }

            Assert(!string.IsNullOrWhiteSpace(card.Definition.Mechanism), $"{card.Definition.Id} has no mechanism");
            Assert(!string.IsNullOrWhiteSpace(card.Definition.Tradeoff), $"{card.Definition.Id} has no trade-off");
        }
    }

    private static Task TestSmbiosMemoryAsync()
    {
        var memory = SmbiosMemoryScanner.Scan();
        if (!memory.Available)
        {
            // Firmware tables are not guaranteed to be readable in every environment; an
            // unavailable result must still be internally coherent rather than half-populated.
            Assert(memory.Modules.Count == 0, "an unavailable reading must carry no modules");
            Assert(!string.IsNullOrWhiteSpace(memory.UnavailableReason), "unavailable must state a reason");
            Assert(!memory.IsBelowRatedSpeed && !memory.IsSingleChannel,
                "an unavailable reading must not assert a fault");
            return Task.CompletedTask;
        }

        Assert(memory.Modules.Count > 0, "an available reading must carry at least one module");
        Assert(memory.TotalMegabytes > 0, "total size must be positive");
        Assert(
            memory.TotalMegabytes == memory.Modules.Sum(module => module.SizeMegabytes),
            "total size must equal the sum of the modules");
        Assert(memory.PopulatedChannels > 0, "populated channels must be positive when modules exist");

        foreach (var module in memory.Modules)
        {
            Assert(module.SizeMegabytes > 0, "a reported module must have a positive size");
            Assert(module.SizeMegabytes <= 1024L * 1024, $"implausible module size {module.SizeMegabytes} MiB");
            Assert(module.ConfiguredSpeedMts is >= 0 and < 20000, "implausible configured speed");
            Assert(module.RatedSpeedMts is >= 0 and < 20000, "implausible rated speed");
        }

        return Task.CompletedTask;
    }

    private static CpuTopology BuildTopology(int physicalCores, ulong lastLevelCacheBytes)
        => new(
            "TestVendor", "Test CPU", physicalCores, physicalCores * 2, true, false,
            [new CoreGroup(0, (1UL << (physicalCores * 2)) - 1, physicalCores, physicalCores * 2, lastLevelCacheBytes, 0)],
            null, "test", 0, (1UL << (physicalCores * 2)) - 1, null, null, null, null, []);

    private static Task TestStackedCacheDetectionAsync()
    {
        // 8 cores sharing 96 MiB is the stacked-cache signature; 8 cores sharing 32 MiB is not.
        var stacked = BuildTopology(8, 96UL * 1024 * 1024);
        Assert(stacked.HasStackedCache, "96 MiB across 8 cores must read as stacked cache");
        AssertNear(12, stacked.LargestCachePerCoreMiB, 0.01, "cache per core");

        var conventional = BuildTopology(8, 32UL * 1024 * 1024);
        Assert(!conventional.HasStackedCache, "32 MiB across 8 cores must not read as stacked cache");

        var hybridRing = BuildTopology(10, 12UL * 1024 * 1024);
        Assert(!hybridRing.HasStackedCache, "12 MiB across 10 cores must not read as stacked cache");
        return Task.CompletedTask;
    }

    private static Task TestPlatformTimerAsync()
    {
        var (forced, frequency) = PlatformStateScanner.ReadPlatformTimer();
        Assert(frequency > 0, "performance counter frequency must be positive");

        // The classification must agree with the frequency it was derived from, in both directions.
        Assert(forced == (frequency == 14_318_180), "forced-clock classification must match the frequency");
        return Task.CompletedTask;
    }

    private static Task TestAudioEndpointsAsync()
    {
        var audio = AudioEndpointScanner.Scan();
        if (!audio.Available)
        {
            Assert(audio.Endpoints.Count == 0, "an unavailable audio reading must carry no endpoints");
            return Task.CompletedTask;
        }

        Assert(audio.Endpoints.Count > 0, "an available reading must carry at least one endpoint");
        Assert(audio.Endpoints.Count(endpoint => endpoint.IsDefault) == 1,
            "exactly one endpoint must be marked representative");

        foreach (var endpoint in audio.Endpoints)
        {
            // The stored format sits behind a property-variant header. Parsing from the wrong
            // offset yields a rate of 1 Hz or 0 channels, so these bounds are the regression guard.
            Assert(endpoint.SampleRateHz is >= 8000 and <= 768000,
                $"implausible sample rate {endpoint.SampleRateHz} — the format blob offset is wrong");
            Assert(endpoint.Channels is > 0 and <= 32,
                $"implausible channel count {endpoint.Channels}");
            Assert(endpoint.IsResampling == (endpoint.SampleRateHz != 48000),
                "resampling must be derived from the reported rate");
        }

        return Task.CompletedTask;
    }

    private static Task TestPanelIdentityAsync()
    {
        var panel = DisplayEdidScanner.Scan();
        if (!panel.Available)
        {
            Assert(!string.IsNullOrWhiteSpace(panel.Observation), "an unavailable panel must state why");
            return Task.CompletedTask;
        }

        Assert(panel.ManufacturerCode.Length == 3, "the manufacturer code must be three letters");
        Assert(panel.NativeWidth is >= 0 and <= 16384, $"implausible native width {panel.NativeWidth}");
        Assert(panel.NativeHeight is >= 0 and <= 16384, $"implausible native height {panel.NativeHeight}");
        if (panel.MaximumVerticalHz > 0)
        {
            Assert(panel.MaximumVerticalHz >= panel.MinimumVerticalHz,
                "the vertical range maximum must not sit below its minimum");
        }

        return Task.CompletedTask;
    }

    private static Task TestNvidiaProfileAsync()
    {
        var profile = NvidiaProfileScanner.Scan("cs2");

        // The driver library is absent on most build agents. The contract that matters is that a
        // missing or uncooperative driver produces an empty, explained result and never throws.
        Assert(!string.IsNullOrWhiteSpace(profile.Observation), "the profile reading must state its status");
        if (!profile.Available)
        {
            Assert(profile.Settings.Count == 0, "an unavailable profile must carry no settings");
        }
        else
        {
            Assert(profile.Settings.Count > 0, "an available profile must carry at least one setting");
            foreach (var setting in profile.Settings)
            {
                Assert(!string.IsNullOrWhiteSpace(setting.Name), "a setting must be named");
                Assert(!string.IsNullOrWhiteSpace(setting.Value), "a setting must have a value");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TestDebunkRegisterAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        var context = await new ExpertScanCoordinator().ScanAsync(
            snapshot, measureInput: false, measureScheduler: false, measureNetwork: false, TimeSpan.Zero);
        var cards = ExpertTweakCatalog.Evaluate(context, new WindowsMutationExecutor());

        var excluded = cards.Where(card => card.Definition.Id.StartsWith("EXCLUDE-", StringComparison.Ordinal))
            .ToArray();
        Assert(excluded.Length >= 5, $"expected the exclusion register, found {excluded.Length} entries");

        foreach (var card in excluded)
        {
            AssertEqual(0, card.Plan.Count, $"{card.Definition.Id} must never offer a mutation");
            Assert(!card.CanApply, $"{card.Definition.Id} must not be applicable");
            AssertEqual(EvidenceQuality.Disproven, card.Definition.Evidence,
                $"{card.Definition.Id} must be marked as disproven");
            Assert(card.Definition.Rationale.Length > 60,
                $"{card.Definition.Id} must explain why it was excluded, not just assert it");
        }
    }

    private sealed class CountingReader(ITweakStateReader inner) : ITweakStateReader
    {
        public int ReadCount { get; private set; }

        public string? Read(MutationPlan plan, out bool exists)
        {
            ReadCount++;
            return inner.Read(plan, out exists);
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

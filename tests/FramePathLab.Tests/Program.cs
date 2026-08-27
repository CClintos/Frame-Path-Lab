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
        ("CSV analyzer reads PresentMon DisplayedTime drops", TestDisplayedTimeDropsAsync),
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
        ("Expert engine journals write intent before mutation", TestExpertEngineWriteIntentAsync),
        ("Expert policy strips excluded mutation plans", TestExpertPolicyAsync),
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
        ("Excluded tweaks never offer a mutation", TestDebunkRegisterAsync),
        ("Allowlist refuses targets outside the catalogue", TestMutationAllowlistAsync),
        ("Engine refuses an off-allowlist write and revert", TestAllowlistBlocksTamperedLedgerAsync),
        ("Policy leaves the catalogue able to act", TestPolicyLeavesWritableTweaksAsync),
        ("Verifier reports no measured change inside noise", TestVerifierNoChangeAsync),
        ("Verifier flags a tail regression", TestVerifierRegressionAsync),
        ("Verifier refuses incomparable captures", TestVerifierRefusesMismatchAsync),
        ("Hardware error history reads without throwing", TestHardwareErrorScanAsync),
        ("CPU tuning plan orders the boost region before all-core", TestCpuTuningPlanAsync),
        ("Stacked-cache parts report locked boost controls", TestCpuTuningStackedCacheAsync),
        ("Autotune levels widen without ever including excluded work", TestAutoTuneLevelsAsync),
        ("Autotune reverts a regression instead of keeping it", TestAutoTuneRevertsRegressionAsync),
        ("Autotune never keeps a change it could not measure", TestAutoTuneRefusesUnmeasuredAsync),
        ("Paired schedule balances condition order against drift", TestAbScheduleBalancesDriftAsync),
        ("Paired test cancels a linear drift confound", TestAbCancelsDriftAsync),
        ("Paired test refuses to conclude from too few pairs", TestAbSmallSampleAsync),
        ("Paired test reports a real effect as conclusive", TestAbDetectsRealEffectAsync),
        ("Service allowlist admits only curated services and the start value", TestServiceAllowlistAsync),
        ("Service cards refuse a service with live dependents", TestServiceDependencyGateAsync),
        ("Device class policy refuses everything it does not name", TestDeviceClassPolicyAsync),
        ("Device cards never offer a device in use", TestDeviceInUseGateAsync),
        ("A snapshot reaches the same verdicts as the machine it came from", TestSnapshotRoundTripAsync),
        ("A plan file carries identifiers and never mutations", TestPlanCarriesNoMutationsAsync),
        ("A plan built for another machine is refused", TestPlanTargetMismatchAsync),
        ("Replay reports an unrecorded surface as absent", TestReplayOfUnknownKeyAsync),
        ("The System class is decided per device, not per class", TestSystemDevicePolicyAsync)
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

    private static async Task TestDisplayedTimeDropsAsync()
    {
        await WithTemporaryDirectoryAsync(async directory =>
        {
            var path = Path.Combine(directory, "displayed-time.csv");
            await File.WriteAllTextAsync(
                path,
                "Application,FrameTime,DisplayedTime\n"
                + "cs2.exe,4.0,1000\n"
                + "cs2.exe,4.1,NA\n"
                + "cs2.exe,4.2,1008\n"
                + "cs2.exe,4.3,N/A\n");

            var result = await new PresentMonCsvAnalyzer().AnalyzeAsync(path, new CaptureAnalysisOptions());
            var dropped = result.DeliveryFindings?.SingleOrDefault(finding => finding.Id == "DROPPED");
            Assert(dropped is not null, "DisplayedTime=NA rows must be counted as dropped presents");
            Assert(dropped!.Observed.Contains("50%", StringComparison.Ordinal),
                $"drop share should use accepted rows as denominator, observed: {dropped.Observed}");
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

    /// <summary>
    /// Permits the scratch key these tests write, and nothing else. Production always uses the
    /// sealed allowlist; this exists so the engine can be exercised without the test key ever
    /// being reachable in a shipped build.
    /// </summary>
    private sealed class ScratchGuard : IMutationGuard
    {
        public string? FindViolation(MutationPlan plan)
            => Check(plan.Kind, plan.Target, plan.ValueName);

        public string? FindViolation(MutationRecord record)
            => Check(record.Kind, record.Target, record.ValueName);

        private static string? Check(MutationKind kind, string target, string valueName)
            => kind == MutationKind.RegistryValue
               && target.StartsWith(TestRegistryPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : MutationAllowlist.FindViolation(kind, target, valueName);
    }


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
                    new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: false, new ScratchGuard());

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
                new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: false,
                new ScratchGuard());

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

            // Elevated, the same machine-scope write proceeds — the ledger is not trusted to name
            // its own target, so elevation is safe without disabling privileged writes entirely.
            var elevatedEngine = new ExpertTweakEngine(
                new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: true, new ScratchGuard());
            var elevated = typeof(ExpertTweakEngine)
                .GetMethod("GateOnElevation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(elevatedEngine, [BuildTestCard("ELEV-UI-001", [TestRegistryPlan("Elevated", "1")])])
                as ExpertTweakCard;
            Assert(elevated?.BlockedReason is null,
                "an allowlisted write must not be blocked merely because the process is elevated");
            Assert(elevated!.CanApply, "an allowlisted elevated write must remain applicable");

            // What elevation must never buy is a target off the allowlist.
            var offList = typeof(ExpertTweakEngine)
                .GetMethod("GateOnElevation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(
                    new ExpertTweakEngine(
                        new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: true),
                    [BuildTestCard("ELEV-ROGUE-001", [TestRegistryPlan("Rogue", "1")])])
                as ExpertTweakCard;
            Assert(offList?.BlockedReason?.Contains("allowlist", StringComparison.OrdinalIgnoreCase) == true,
                "elevation must not permit a target outside the allowlist");
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
                    new WindowsMutationExecutor(), new TweakJournalStore(directory), isElevated: false, new ScratchGuard());

                // The second mutation passes the allowlist and captures cleanly, but its value
                // cannot be written as the declared type, so the first one lands and the tweak
                // then fails mid-apply.
                var card = BuildTestCard("PARTIAL-001", [
                    TestRegistryPlan("PartialA", "5"),
                    new MutationPlan(
                        "test.unwritable",
                        MutationKind.RegistryValue,
                        TestRegistryPath,
                        "PartialB",
                        "not-a-number",
                        "DWord",
                        "a value that cannot be written as the declared type")
                ]);

                var result = engine.Apply(card);
                AssertEqual(TweakTransaction.StateReverted, result.State,
                    "a failed apply must automatically roll back and report a clean revert");

                var executor = new WindowsMutationExecutor();
                executor.Read(TestRegistryPlan("PartialA", "0"), out var exists);
                Assert(!exists, "the mutation that did land must be undone");
                Assert(
                    result.Mutations.Any(record =>
                        record.MutationId == "test.unwritable"
                        && record.AttemptedWrite
                        && record.VerifiedAfterWrite),
                    "a durable write intent must be conservatively reverted even when the write throws");
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

                // An unreadable target means no before-state can be captured, so nothing may be
                // written at all: applying it would create a change that could never be undone.
                var executor = new FailingCaptureExecutor(new WindowsMutationExecutor(), "test.uncapturable");
                var engine = new ExpertTweakEngine(executor, journal, isElevated: false, new ScratchGuard());

                var card = BuildTestCard("FAILCLOSED-001", [
                    TestRegistryPlan("NeverWritten", "5"),
                    new MutationPlan(
                        "test.uncapturable", MutationKind.RegistryValue, TestRegistryPath, "Uncapturable",
                        "1", "DWord", "a value whose before-state cannot be read")
                ]);

                AssertThrows<InvalidOperationException>(() => engine.Apply(card));

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

    private static async Task TestExpertEngineWriteIntentAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var journal = new TweakJournalStore(directory);
                var inner = new WindowsMutationExecutor();
                var executor = new IntentObservingExecutor(inner, plan =>
                    journal.Read().Any(transaction => transaction.Mutations.Any(record =>
                        record.MutationId == plan.MutationId && record.AttemptedWrite)));
                var engine = new ExpertTweakEngine(executor, journal, isElevated: false, new ScratchGuard());
                var card = BuildTestCard("INTENT-001", [TestRegistryPlan("Intent", "7")]);

                var applied = engine.Apply(card);
                Assert(executor.SawDurableIntent, "the mutation ran before durable write intent existed");
                AssertEqual(TweakTransaction.StateApplied, applied.State, "intent test apply state");
                engine.Revert(applied.TransactionId, "intent test cleanup");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static Task TestExpertPolicyAsync()
    {
        // The invariant is that a non-writable disposition never keeps a plan, whichever
        // non-writable disposition the policy assigns. Asserting one specific classification
        // would make the test a restatement of the policy rather than a check on it.
        foreach (var id in (string[])
                 ["SECURITY-HVCI-001", "CPU-PLACEMENT-001", "MMCSS-GAMES-001", "GPU-MSI-001", "GPU-HAGS-001"])
        {
            var gated = ExpertTweakPolicy.Apply(BuildTestCard(id, [TestRegistryPlan("Unsafe", "0")]));
            Assert(!ExpertTweakPolicy.IsWritable(gated.Definition.Disposition),
                $"{id} must not be writable");
            AssertEqual(0, gated.Plan.Count, $"{id} plans must be stripped");
            Assert(!gated.CanApply, $"{id} must never be applicable");
            Assert(gated.Definition.DispositionReason.Length > 40, $"{id} must explain its disposition");
        }

        var acceleration = ExpertTweakPolicy.Apply(
            BuildTestCard("INPUT-ACCEL-001", [TestRegistryPlan("Accel", "0")]));
        AssertEqual(TweakDisposition.RecommendDefault, acceleration.Definition.Disposition,
            "pointer acceleration should be a recommended default");
        AssertEqual(1, acceleration.Plan.Count, "a recommended default must retain its plan");

        var powerCard = BuildTestCard("POWER-OVERLAY-001", [TestRegistryPlan("Power", "1")]);
        var experiment = ExpertTweakPolicy.Apply(powerCard);
        AssertEqual(TweakDisposition.OptInExperiment, experiment.Definition.Disposition, "power disposition");
        AssertEqual(1, experiment.Plan.Count, "benchmark-only supported candidate should retain its plan");
        return Task.CompletedTask;
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
        Assert(memory.PopulatedChannels >= 0, "channel count must not be negative");

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

        Assert(forced is null, "QPC frequency alone must not be treated as proof of BCD timer state");
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

    private static Task TestMutationAllowlistAsync()
    {
        // Permitted: a key the catalogue actually writes.
        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.RegistryValue, @"HKCU\System\GameConfigStore", "GameDVR_Enabled") is null,
            "a catalogue key must be permitted");

        // Refused: anything else, including a run key and a traversal below a permitted prefix.
        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.RegistryValue,
                @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                "Evil") is not null,
            "an arbitrary key must be refused");

        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.RegistryValue,
                @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001\Evil",
                "*InterruptModeration") is not null,
            "a key below the adapter instance must be refused");

        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.RegistryValue,
                @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\0001",
                "ImagePath") is not null,
            "an unlisted value on a permitted adapter key must be refused");

        // Process and boot targets are refused by kind regardless of what they name.
        Assert(
            MutationAllowlist.FindViolation(MutationKind.ProcessAffinity, "cs2", "ProcessorAffinity") is not null,
            "process writes must be refused by kind");
        Assert(
            MutationAllowlist.FindViolation(MutationKind.BootConfigurationValue, "bcd", "x") is not null,
            "boot configuration must be refused by kind");

        // A power setting outside the processor subgroup is refused even with a bound scheme GUID.
        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.PowerSchemeValue,
                "381b4222-f694-41f0-9685-ff5bb260df2e|00000000-0000-0000-0000-000000000000:abc",
                "x") is not null,
            "a non-processor power subgroup must be refused");

        return Task.CompletedTask;
    }

    private static async Task TestAllowlistBlocksTamperedLedgerAsync()
    {
        await WithTemporaryDirectoryAsync(directory =>
        {
            var journal = new TweakJournalStore(directory);
            var engine = new ExpertTweakEngine(new WindowsMutationExecutor(), journal, isElevated: false);

            // A card naming a location outside the allowlist must never reach a write, even when
            // it is handed straight to Apply rather than coming from the catalogue.
            var rogue = BuildTestCard("ROGUE-001", [
                new MutationPlan(
                    "rogue.run",
                    MutationKind.RegistryValue,
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                    "Evil",
                    "payload.exe",
                    "String",
                    "off-allowlist write")
            ]);

            AssertThrows<InvalidOperationException>(() => engine.Apply(rogue));

            new WindowsMutationExecutor().Read(
                new MutationPlan("check", MutationKind.RegistryValue,
                    @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Evil", "", "String", "check"),
                out var exists);
            Assert(!exists, "an off-allowlist value must never be written");

            // The same check must hold on the way back out, because a restore replays ledger data
            // that a user can edit.
            var tampered = new TweakTransaction(
                Guid.NewGuid(), "ROGUE-001", "Rogue", DateTimeOffset.UtcNow, null, false,
                [
                    new MutationRecord(
                        "rogue.run", MutationKind.RegistryValue,
                        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Evil", "String",
                        "off-allowlist restore", true, "payload.exe", "x", "x", true, "applied")
                ],
                TweakTransaction.StateApplied, "applied");
            journal.Upsert(tampered);

            var reverted = engine.Revert(tampered.TransactionId, "test");
            AssertEqual(TweakTransaction.StateRevertFailed, reverted.State,
                "a tampered ledger entry must not be replayed");
            Assert(
                reverted.Mutations[0].Observation.Contains("allowlist", StringComparison.OrdinalIgnoreCase),
                "the refusal must name the allowlist");
            return Task.CompletedTask;
        });
    }

    private static async Task TestPolicyLeavesWritableTweaksAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        var context = await new ExpertScanCoordinator().ScanAsync(
            snapshot, measureInput: false, measureScheduler: false, measureNetwork: false, TimeSpan.Zero);
        var cards = ExpertTweakCatalog.Evaluate(context, new WindowsMutationExecutor());

        // The product has to be able to act. A policy that leaves nothing writable has turned the
        // tool into an advice list, which is the failure mode this gate exists to prevent.
        var writable = cards
            .Where(card => ExpertTweakPolicy.IsWritable(card.Definition.Disposition) && card.Plan.Count > 0)
            .ToArray();
        Assert(writable.Length >= 6,
            $"only {writable.Length} card(s) can be written; the catalogue can no longer act");

        // And every writable target must be on the allowlist.
        foreach (var card in writable)
        {
            foreach (var plan in card.Plan)
            {
                Assert(MutationAllowlist.FindViolation(plan) is null,
                    $"{card.Definition.Id} plans a write to an off-allowlist target: {plan.Target}");
            }
        }

        // Nothing excluded may carry a plan.
        foreach (var card in cards.Where(entry => entry.Definition.Disposition == TweakDisposition.Excluded))
        {
            AssertEqual(0, card.Plan.Count, $"{card.Definition.Id} is excluded but still carries a plan");
        }
    }

    private static CaptureAnalysis BuildAnalysis(
        string name,
        double median,
        double p99,
        double stdDev,
        double meanFps,
        long frames = 60_000)
        => new(
            DateTimeOffset.UtcNow, name, name.GetHashCode(StringComparison.Ordinal).ToString("x8"), 1024,
            "test", "cs2.exe", "FrameTime", frames, frames, 0, ResultOutcome.BaselineOnly,
            [
                new MetricSummary("median_frame_ms", "Median frame time", median, "ms", "", "Available"),
                new MetricSummary("p99_frame_ms", "P99 frame time", p99, "ms", "", "Available"),
                new MetricSummary("frame_stddev", "Frame-time consistency", stdDev, "ms", "", "Available"),
                new MetricSummary("mean_fps", "Mean frame rate", meanFps, "FPS", "", "Available")
            ],
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            []);

    private static Task TestVerifierNoChangeAsync()
    {
        var before = BuildAnalysis("before.csv", 4.000, 6.000, 0.500, 250);
        var after = BuildAnalysis("after.csv", 4.020, 6.020, 0.502, 249.5);

        var result = TweakVerifier.Compare(before, after);
        AssertEqual(VerificationVerdict.NoMeasuredChange, result.Verdict, "sub-noise movement verdict");
        Assert(result.ShouldRevert, "a change that did nothing should be reverted");
        return Task.CompletedTask;
    }

    private static Task TestVerifierRegressionAsync()
    {
        // Mean frame rate improves while the tails get materially worse. A competitive verdict must
        // follow the tails, not the average.
        var before = BuildAnalysis("before.csv", 4.000, 6.000, 0.500, 250);
        var after = BuildAnalysis("after.csv", 3.960, 7.500, 0.700, 253);

        var result = TweakVerifier.Compare(before, after);
        AssertEqual(VerificationVerdict.Regressed, result.Verdict, "tail regression verdict");
        Assert(result.ShouldRevert, "a tail regression should be reverted");
        return Task.CompletedTask;
    }

    private static Task TestVerifierRefusesMismatchAsync()
    {
        var before = BuildAnalysis("before.csv", 4.0, 6.0, 0.5, 250, frames: 100);
        var after = BuildAnalysis("after.csv", 3.5, 5.0, 0.4, 285, frames: 100);
        AssertEqual(VerificationVerdict.NotComparable, TweakVerifier.Compare(before, after).Verdict,
            "too few frames must be refused");

        var identical = BuildAnalysis("same.csv", 4.0, 6.0, 0.5, 250);
        AssertEqual(VerificationVerdict.NotComparable, TweakVerifier.Compare(identical, identical).Verdict,
            "comparing a capture against itself must be refused");
        return Task.CompletedTask;
    }

    /// <summary>Fails to capture one named mutation, leaving every other call untouched.</summary>
    private sealed class FailingCaptureExecutor(IMutationExecutor inner, string failingMutationId) : IMutationExecutor
    {
        public string? Read(MutationPlan plan, out bool exists) => inner.Read(plan, out exists);

        public MutationRecord Capture(MutationPlan plan)
            => plan.MutationId == failingMutationId
                ? throw new InvalidOperationException("The before-state could not be read.")
                : inner.Capture(plan);

        public MutationRecord Apply(MutationPlan plan) => inner.Apply(plan);

        public MutationRecord Apply(MutationPlan plan, MutationRecord captured) => inner.Apply(plan, captured);

        public MutationRecord Revert(MutationRecord record) => inner.Revert(record);

        public bool RequiresElevation(MutationPlan plan) => inner.RequiresElevation(plan);
    }

    private static Task TestHardwareErrorScanAsync()
    {
        var summary = HardwareErrorScanner.Scan(TimeSpan.FromDays(7));
        Assert(!string.IsNullOrWhiteSpace(summary.Observation), "the summary must state its status");

        if (!summary.Readable)
        {
            Assert(summary.TotalEvents == 0, "an unreadable summary must not claim events");
            Assert(!summary.HasUncorrectedErrors && !summary.HasCorrectedErrors,
                "an unreadable summary must not assert instability");
            return Task.CompletedTask;
        }

        AssertEqual(summary.TotalEvents, summary.MachineCheckExceptions + summary.CorrectedErrors,
            "the event breakdown must sum to the total");
        Assert(summary.Recent.Count <= summary.TotalEvents, "recent events cannot exceed the total");
        return Task.CompletedTask;
    }

    private static Task TestCpuTuningPlanAsync()
    {
        var state = CpuTuningAdvisor.Build(
            BuildTopology(8, 96UL * 1024 * 1024),
            HardwareErrorSummary.Unreadable("test"),
            uptimeSeconds: 3600);

        var plan = state.StabilityPlan.OrderBy(step => step.Order).ToArray();
        Assert(plan.Length >= 3, "the validation plan must have at least three steps");

        // The whole point of the sequence: the region a curve offset actually breaks is tested
        // before the all-core run, because passing all-core is the mistake being guarded against.
        var firstBoost = Array.FindIndex(plan, step => step.Region == StabilityRegion.SingleCoreBoost);
        var firstAllCore = Array.FindIndex(plan, step => step.Region == StabilityRegion.AllCoreLoad);
        Assert(firstBoost >= 0, "the plan must test the single-core boost region");
        Assert(firstAllCore >= 0, "the plan must include an all-core step");
        Assert(firstBoost < firstAllCore,
            "single-core boost must be validated before all-core load, not after");

        var idle = Array.FindIndex(plan, step => step.Region == StabilityRegion.IdleAndTransient);
        Assert(idle >= 0 && idle < firstAllCore, "the idle region must be validated before all-core load");

        foreach (var step in plan)
        {
            Assert(!string.IsNullOrWhiteSpace(step.WhatItMisses),
                $"step '{step.Name}' must state what it cannot catch");
        }

        return Task.CompletedTask;
    }

    private static Task TestCpuTuningStackedCacheAsync()
    {
        var stacked = CpuTuningAdvisor.Build(
            BuildTopology(8, 96UL * 1024 * 1024) with { Vendor = "AuthenticAMD", Brand = "Test Ryzen X3D" },
            HardwareErrorSummary.Unreadable("test"),
            uptimeSeconds: 3600);

        Assert(stacked.HasStackedCache, "96 MiB across 8 cores must read as stacked cache");
        Assert(stacked.MultiplierLocked, "a stacked-cache part must report its multiplier as locked");

        var boost = stacked.Controls.FirstOrDefault(control =>
            control.Name.Contains("Boost clock", StringComparison.OrdinalIgnoreCase));
        Assert(boost is not null, "the boost override control must be described");
        Assert(!boost!.AvailableOnThisPart,
            "boost override must be reported unavailable on a stacked-cache part");

        var curve = stacked.Controls.FirstOrDefault(control =>
            control.Name.Contains("Curve", StringComparison.OrdinalIgnoreCase));
        Assert(curve?.AvailableOnThisPart == true, "the curve must remain available on a stacked-cache part");

        var conventional = CpuTuningAdvisor.Build(
            BuildTopology(8, 32UL * 1024 * 1024) with { Vendor = "AuthenticAMD", Brand = "Test Ryzen" },
            HardwareErrorSummary.Unreadable("test"),
            uptimeSeconds: 3600);
        Assert(!conventional.MultiplierLocked, "a conventional part must not report a locked multiplier");
        return Task.CompletedTask;
    }

    /// <summary>Returns a scripted sequence of measurements so a whole run is deterministic.</summary>
    private sealed class ScriptedBenchmark(params CaptureAnalysis[] results) : IBenchmarkRunner
    {
        private int _index;

        public int Runs => _index;

        public CaptureAnalysis Run(CancellationToken cancellationToken = default)
            => results[Math.Min(_index++, results.Length - 1)];
    }

    private static ExpertTweakCard BuildAutoTuneCard(string id, TweakDisposition disposition, TweakRisk risk)
    {
        var card = BuildTestCard(id, [TestRegistryPlan(id.Replace("-", string.Empty, StringComparison.Ordinal), "1")]);
        return card with
        {
            Definition = card.Definition with { Disposition = disposition, Risk = risk }
        };
    }

    private static Task TestAutoTuneLevelsAsync()
    {
        var cards = new[]
        {
            BuildAutoTuneCard("AT-DEFAULT", TweakDisposition.RecommendDefault, TweakRisk.Low),
            BuildAutoTuneCard("AT-EXPERIMENT", TweakDisposition.OptInExperiment, TweakRisk.Moderate),
            BuildAutoTuneCard("AT-RISKY", TweakDisposition.OptInExperiment, TweakRisk.High),
            BuildAutoTuneCard("AT-GUIDED", TweakDisposition.GuidedAction, TweakRisk.Low),
            BuildAutoTuneCard("AT-EXCLUDED", TweakDisposition.Excluded, TweakRisk.Low)
        };

        var conservative = AutoTuneCoordinator.SelectCandidates(cards, AutoTuneLevel.Conservative);
        var balanced = AutoTuneCoordinator.SelectCandidates(cards, AutoTuneLevel.Balanced);
        var aggressive = AutoTuneCoordinator.SelectCandidates(cards, AutoTuneLevel.Aggressive);

        AssertEqual(1, conservative.Count, "conservative takes defaults only");
        AssertEqual(2, balanced.Count, "balanced adds bounded experiments but not high-risk ones");
        AssertEqual(3, aggressive.Count, "aggressive adds the high-risk experiment");

        // The levels widen, but no level may ever reach something policy refuses to write.
        foreach (var selection in (IReadOnlyList<ExpertTweakCard>[])[conservative, balanced, aggressive])
        {
            foreach (var card in selection)
            {
                Assert(ExpertTweakPolicy.IsWritable(card.Definition.Disposition),
                    $"{card.Definition.Id} is not writable and must never be a candidate");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TestAutoTuneRevertsRegressionAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var journal = new TweakJournalStore(directory);
                var engine = new ExpertTweakEngine(
                    new WindowsMutationExecutor(), journal, isElevated: false, new ScratchGuard());

                // Tails materially worse, average flat — the case a mean-based judgement misses.
                var benchmark = new ScriptedBenchmark(
                    BuildAnalysis("baseline.csv", 4.000, 6.000, 0.500, 250),
                    BuildAnalysis("after.csv", 3.980, 7.400, 0.760, 251));

                var card = BuildAutoTuneCard("AT-REGRESS", TweakDisposition.RecommendDefault, TweakRisk.Low);
                var report = new AutoTuneCoordinator(engine, benchmark)
                    .Run([card], AutoTuneLevel.Conservative, AutoTuneMode.Isolate);

                AssertEqual(1, report.Applied, "the candidate should have been applied");
                AssertEqual(0, report.Kept, "a tail regression must not be kept");
                AssertEqual(1, report.Reverted, "a tail regression must be reverted");

                new WindowsMutationExecutor().Read(TestRegistryPlan("ATREGRESS", "0"), out var exists);
                Assert(!exists, "the reverted change must be gone from the machine");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static async Task TestAutoTuneRefusesUnmeasuredAsync()
    {
        ClearTestRegistry();
        try
        {
            await WithTemporaryDirectoryAsync(directory =>
            {
                var journal = new TweakJournalStore(directory);
                var engine = new ExpertTweakEngine(
                    new WindowsMutationExecutor(), journal, isElevated: false, new ScratchGuard());

                // Too few frames to compare. This must not read as a pass — keeping a change
                // because the measurement failed is exactly the behaviour the tool exists to avoid.
                var benchmark = new ScriptedBenchmark(
                    BuildAnalysis("baseline.csv", 4.0, 6.0, 0.5, 250, frames: 100),
                    BuildAnalysis("after.csv", 3.5, 5.0, 0.4, 285, frames: 100));

                var card = BuildAutoTuneCard("AT-UNMEASURED", TweakDisposition.RecommendDefault, TweakRisk.Low);
                var report = new AutoTuneCoordinator(engine, benchmark)
                    .Run([card], AutoTuneLevel.Conservative, AutoTuneMode.Isolate);

                AssertEqual(0, report.Kept, "an unmeasurable result must never be kept");

                new WindowsMutationExecutor().Read(TestRegistryPlan("ATUNMEASURED", "0"), out var exists);
                Assert(!exists, "an unmeasured change must be reversed off the machine");
                Assert(
                    report.Steps.Any(step => step.Outcome.Contains("not measured", StringComparison.OrdinalIgnoreCase)),
                    "the report must say the change was not measured rather than implying it passed");
                return Task.CompletedTask;
            });
        }
        finally
        {
            ClearTestRegistry();
        }
    }

    private static Task TestAbScheduleBalancesDriftAsync()
    {
        var schedule = PairedAbTest.BuildSchedule(4);
        AssertEqual(8, schedule.Count, "four pairs means eight measurements");
        AssertEqual(4, schedule.Count(applied => applied), "each condition must be measured equally often");

        // The balance that matters is positional: if one condition sits systematically later in
        // the session, a drifting machine attributes the drift to that condition.
        var appliedPositions = schedule.Select((applied, index) => (applied, index))
            .Where(entry => entry.applied).Sum(entry => entry.index);
        var offPositions = schedule.Select((applied, index) => (applied, index))
            .Where(entry => !entry.applied).Sum(entry => entry.index);
        AssertEqual(offPositions, appliedPositions,
            "both conditions must occupy the same average position in time");
        return Task.CompletedTask;
    }

    private static Task TestAbCancelsDriftAsync()
    {
        // A machine that gets steadily slower, with the change itself doing nothing at all. Under
        // a balanced schedule the drift must not be reported as an effect.
        var schedule = PairedAbTest.BuildSchedule(4);
        var readings = schedule.Select((_, index) => 10.0 + (index * 0.20)).ToArray();

        var pairs = new List<AbPair>();
        for (var index = 0; index < schedule.Count; index += 2)
        {
            var first = schedule[index];
            var a = first ? readings[index + 1] : readings[index];
            var b = first ? readings[index] : readings[index + 1];
            pairs.Add(new AbPair(index / 2, a, b));
        }

        var result = PairedAbTest.Evaluate("p99_frame_ms", "P99 frame time", true, pairs);
        Assert(!result.Conclusive,
            $"pure drift must not read as an effect, but reported {result.MeanPercentChange:0.##}%");
        Assert(Math.Abs(result.MeanPercentChange) < PairedAbTest.PracticalThresholdPercent,
            "a balanced schedule should leave drift near zero");
        return Task.CompletedTask;
    }

    private static Task TestAbSmallSampleAsync()
    {
        // Two pairs with a large apparent difference. The t critical value at one degree of
        // freedom is punishing for a reason: two points cannot establish a trend.
        var pairs = new List<AbPair> { new(0, 10.0, 9.0), new(1, 10.0, 9.2) };
        var result = PairedAbTest.Evaluate("p99_frame_ms", "P99 frame time", true, pairs);
        Assert(!result.Conclusive, "two pairs must not produce a conclusive verdict");

        var single = PairedAbTest.Evaluate("p99_frame_ms", "P99 frame time", true,
            [new AbPair(0, 10.0, 5.0)]);
        Assert(!single.Conclusive, "a single pair can never be conclusive regardless of the difference");
        return Task.CompletedTask;
    }

    private static Task TestAbDetectsRealEffectAsync()
    {
        // A consistent eight percent improvement with realistic scatter must be found.
        var pairs = new List<AbPair>
        {
            new(0, 10.00, 9.18), new(1, 10.05, 9.25), new(2, 9.96, 9.14),
            new(3, 10.02, 9.20), new(4, 9.99, 9.22)
        };

        var result = PairedAbTest.Evaluate("p99_frame_ms", "P99 frame time", true, pairs);
        Assert(result.Conclusive, $"a consistent 8% effect must be conclusive: {result.Finding}");
        Assert(result.IsImprovement, "a lower P99 must read as an improvement");
        Assert(result.ConfidenceHighPercent < 0, "the whole interval should sit below zero");
        return Task.CompletedTask;
    }

    private static Task TestServiceAllowlistAsync()
    {
        const string root = @"HKLM\SYSTEM\CurrentControlSet\Services\";

        // A curated service's start value is permitted.
        Assert(MutationAllowlist.FindViolation(MutationKind.RegistryValue, root + "Fax", "Start") is null,
            "a curated service start value must be permitted");

        // Nothing else about that service is.
        Assert(MutationAllowlist.FindViolation(MutationKind.RegistryValue, root + "Fax", "ImagePath") is not null,
            "only the start value may be written on a service");

        // A service that is not on the curated list is refused, even though the prefix matches.
        Assert(MutationAllowlist.FindViolation(MutationKind.RegistryValue, root + "SomeOtherService", "Start") is not null,
            "an uncurated service must be refused");

        // The never-offered list is enforced independently of the candidate list.
        foreach (var protectedService in (string[])["EventLog", "RpcSs", "WinDefend", "BFE", "Dhcp"])
        {
            Assert(
                MutationAllowlist.FindViolation(MutationKind.RegistryValue, root + protectedService, "Start") is not null,
                $"{protectedService} must never be writable");
        }

        // A nested key beneath a service must not inherit the permission.
        Assert(
            MutationAllowlist.FindViolation(MutationKind.RegistryValue, root + @"Fax\Parameters", "Start") is not null,
            "a nested key under a service must be refused");

        return Task.CompletedTask;
    }

    private static async Task TestServiceDependencyGateAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        var context = await new ExpertScanCoordinator().ScanAsync(
            snapshot, measureInput: false, measureScheduler: false, measureNetwork: false, TimeSpan.Zero);
        var cards = ExpertTweakCatalog.Evaluate(context, new WindowsMutationExecutor());
        var services = cards.Where(card => card.Definition.Id.StartsWith("SERVICE-", StringComparison.Ordinal))
            .ToArray();

        if (!context.Services.Available || services.Length == 0)
        {
            return;
        }

        foreach (var card in services)
        {
            var name = card.Definition.Id["SERVICE-".Length..];

            // The invariant that keeps this safe: anything with a live dependent must carry no
            // plan, whatever else the card says about it.
            var dependents = context.Services.LiveDependentsOf(name);
            if (dependents.Count > 0)
            {
                AssertEqual(0, card.Plan.Count,
                    $"{name} has {dependents.Count} live dependent(s) and must not offer a write");
                Assert(!card.CanApply, $"{name} must not be applicable while something depends on it");
            }

            // And every offered write must be the start value on that exact service.
            foreach (var plan in card.Plan)
            {
                AssertEqual("Start", plan.ValueName, $"{name} must only write the start value");
                Assert(MutationAllowlist.FindViolation(plan) is null,
                    $"{name} plans a write the allowlist refuses");
            }

            Assert(card.Definition.Tradeoff.Contains("You lose", StringComparison.Ordinal),
                $"{name} must state what stops working");
        }
    }

    private static Task TestDeviceClassPolicyAsync()
    {
        // Losing any of these costs the use of the machine rather than costing frame time, so they
        // must be refused whatever else is true.
        foreach (var deadly in (string[])
                 ["HIDClass", "Keyboard", "Mouse", "USB", "System", "DiskDrive", "Display", "Processor",
                  "Volume", "SCSIAdapter", "SecurityDevices"])
        {
            Assert(DeviceClassPolicy.FindClassViolation(deadly) is not null,
                $"the {deadly} class must never be offerable");
        }

        // Unknown classes fail closed rather than open.
        Assert(DeviceClassPolicy.FindClassViolation("SomeVendorClass") is not null,
            "an unrecognised class must be refused");
        Assert(DeviceClassPolicy.FindClassViolation(string.Empty) is not null,
            "a device with no class must be refused");

        // And the ones that are offered must each state what stops working.
        foreach (var (deviceClass, loss) in DeviceClassPolicy.OfferableClasses)
        {
            Assert(DeviceClassPolicy.FindClassViolation(deviceClass) is null,
                $"{deviceClass} is listed as offerable but the policy refuses it");
            Assert(loss.Length > 40, $"{deviceClass} must state what is lost, not just that it is safe");
        }

        // The guard must reach the same conclusion, since a restore replays through it.
        Assert(
            MutationAllowlist.FindViolation(MutationKind.DeviceState, @"PCI\VEN_1234", "HIDClass") is not null,
            "the allowlist must refuse a never-offered device class");
        Assert(
            MutationAllowlist.FindViolation(MutationKind.DeviceState, @"PCI\VEN_1234", "Bluetooth") is null,
            "the allowlist must permit an offerable device class");
        return Task.CompletedTask;
    }

    private static async Task TestDeviceInUseGateAsync()
    {
        var snapshot = await new WindowsEnvironmentScanner().ScanAsync();
        var context = await new ExpertScanCoordinator().ScanAsync(
            snapshot, measureInput: false, measureScheduler: false, measureNetwork: false, TimeSpan.Zero);
        var cards = ExpertTweakCatalog.Evaluate(context, new WindowsMutationExecutor());
        var devices = cards.Where(card => card.Definition.Id.StartsWith("DEVICE-", StringComparison.Ordinal))
            .ToArray();

        if (!context.Devices.Available)
        {
            return;
        }

        foreach (var entry in context.Devices.Devices)
        {
            // Every enumerated candidate must sit on real hardware. A software node raises no
            // interrupts, so disabling one cannot help and offering it is noise.
            Assert(
                entry.InstanceId.Contains('\\'),
                $"{entry.Name} has no bus-qualified instance identifier");

            Assert(DeviceClassPolicy.FindDeviceViolation(entry.DeviceClass, entry.InstanceId) is null,
                $"{entry.Name} ({entry.InstanceId}) is refused by the device policy");
        }

        foreach (var card in devices)
        {
            var instanceId = card.Definition.Id["DEVICE-".Length..];
            var entry = context.Devices.Devices.FirstOrDefault(device => device.InstanceId == instanceId);
            if (entry is null)
            {
                continue;
            }

            // The invariant that stops this disconnecting someone: anything carrying work is never
            // offered, whatever its class says.
            if (entry.InUse)
            {
                AssertEqual(0, card.Plan.Count, $"{entry.Name} is in use and must not offer a write");
                Assert(!card.CanApply, $"{entry.Name} is in use and must not be applicable");
            }

            foreach (var plan in card.Plan)
            {
                AssertEqual(MutationKind.DeviceState, plan.Kind, "a device card must plan a device change");
                Assert(MutationAllowlist.FindViolation(plan) is null,
                    $"{entry.Name} plans a change the allowlist refuses");
            }
        }
    }

    /// <summary>
    /// The claim the whole cross-machine feature rests on: a snapshot answers the catalogue exactly
    /// as the machine would have. If serialization drops one field of the context, or one recorded
    /// read, the reviewing machine quietly reaches a different verdict — and the person acting on it
    /// has no way to tell. So this compares every card, not a summary count.
    /// </summary>
    private static async Task TestSnapshotRoundTripAsync()
    {
        var snapshot = await MachineSnapshotCollector.CollectAsync(measureInput: false, TimeSpan.Zero);

        var live = ExpertTweakCatalog.Evaluate(snapshot.Context, new WindowsMutationExecutor());
        Assert(live.Count > 0, "the catalogue produced no cards to compare");

        var path = Path.Combine(
            Path.GetTempPath(), $"fpl-{Guid.NewGuid():N}{MachineSnapshotStore.SnapshotExtension}");
        try
        {
            MachineSnapshotStore.WriteSnapshot(path, snapshot);
            var reloaded = MachineSnapshotStore.ReadSnapshot(path);

            AssertEqual(snapshot.Identity.Fingerprint, reloaded.Identity.Fingerprint,
                "the machine fingerprint did not survive the file");
            AssertEqual(snapshot.Reads.Count, reloaded.Reads.Count,
                "recorded reads were lost writing the snapshot out");

            var replayed = ExpertTweakCatalog.Evaluate(
                reloaded.Context, new ReplayStateReader(reloaded.Reads));

            AssertEqual(live.Count, replayed.Count, "the reloaded snapshot produced a different card count");

            var liveById = live.ToDictionary(card => card.Definition.Id, StringComparer.Ordinal);
            foreach (var card in replayed)
            {
                Assert(liveById.TryGetValue(card.Definition.Id, out var original),
                    $"{card.Definition.Id} exists only after the round trip");

                AssertEqual(original!.Reading.State, card.Reading.State,
                    $"{card.Definition.Id} reads differently from a snapshot");
                AssertEqual(original.Reading.CurrentValue, card.Reading.CurrentValue,
                    $"{card.Definition.Id} reports a different current value from a snapshot");
                AssertEqual(original.Plan.Count, card.Plan.Count,
                    $"{card.Definition.Id} plans a different number of writes from a snapshot");
                AssertEqual(original.CanApply, card.CanApply,
                    $"{card.Definition.Id} differs on whether it can be applied");
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not a test failure.
            }
        }
    }

    /// <summary>
    /// A plan file is data that an elevated process on another machine reads. If it carried the
    /// writes themselves it would be a command channel into that process — the same hole the
    /// allowlist exists to close. This asserts the format stays incapable of expressing a write.
    /// </summary>
    private static async Task TestPlanCarriesNoMutationsAsync()
    {
        var snapshot = await MachineSnapshotCollector.CollectAsync(measureInput: false, TimeSpan.Zero);
        var review = RemoteMachineReview.Review(snapshot);
        var applicable = review.Cards.Where(card => card.CanApply).Take(5).ToArray();
        if (applicable.Length == 0)
        {
            return;
        }

        var plan = RemoteMachineReview.BuildPlan(snapshot, applicable, "test");
        AssertEqual(applicable.Length, plan.TweakIds.Count, "the plan lost a selected tweak");

        var path = Path.Combine(
            Path.GetTempPath(), $"fpl-{Guid.NewGuid():N}{MachineSnapshotStore.PlanExtension}");
        try
        {
            MachineSnapshotStore.WritePlan(path, plan);
            var text = File.ReadAllText(path);

            foreach (var mutation in applicable.SelectMany(card => card.Plan))
            {
                Assert(!text.Contains(mutation.Target, StringComparison.OrdinalIgnoreCase),
                    $"the plan file leaked a write target: {mutation.Target}");
            }

            Assert(!text.Contains("HKEY", StringComparison.OrdinalIgnoreCase),
                "the plan file contains a registry path");

            foreach (var id in plan.TweakIds)
            {
                Assert(text.Contains(id, StringComparison.Ordinal), $"the plan file lost {id}");
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Ignored, as above.
            }
        }
    }

    private static Task TestPlanTargetMismatchAsync()
    {
        var here = new MachineIdentity(
            "THIS-PC",
            MachineSnapshotStore.Fingerprint("THIS-PC", "Some CPU", 8, 16, 34_359_738_368),
            "Some CPU", 8, 16, 34_359_738_368, "10.0.26200", "Some GPU");

        var elsewhere = here with
        {
            MachineName = "GAMING-PC",
            Fingerprint = MachineSnapshotStore.Fingerprint(
                "GAMING-PC", "AMD Ryzen 7 5800X3D", 8, 16, 34_359_738_368),
            ProcessorBrand = "AMD Ryzen 7 5800X3D"
        };

        var plan = new TweakPlanFile(
            TweakPlanFile.CurrentFormatVersion, DateTimeOffset.UtcNow, elsewhere, ["GAMEDVR-001"], "test");

        Assert(RemoteMachineReview.FindTargetMismatch(plan, here) is not null,
            "a plan for a different machine must be refused");
        Assert(RemoteMachineReview.FindTargetMismatch(plan, elsewhere) is null,
            "a plan for this machine must be accepted");

        // The same box read twice must fingerprint the same, or every plan would be refused.
        AssertEqual(
            MachineSnapshotStore.Fingerprint("gaming-pc", "AMD Ryzen 7 5800X3D", 8, 16, 34_359_738_368),
            elsewhere.Fingerprint,
            "the fingerprint must not depend on the case of the machine name");

        // Reported physical memory wobbles by a few megabytes between boots, and a fingerprint that
        // moved with it would refuse every plan the day after it was written.
        AssertEqual(
            MachineSnapshotStore.Fingerprint(
                "GAMING-PC", "AMD Ryzen 7 5800X3D", 8, 16, 34_359_738_368 - 200_000_000),
            elsewhere.Fingerprint,
            "the fingerprint must tolerate small differences in reported memory");
        return Task.CompletedTask;
    }

    private static Task TestSystemDevicePolicyAsync()
    {
        // The class as a whole stays refused, so any caller that only knows the class fails closed.
        Assert(DeviceClassPolicy.FindClassViolation("System") is not null,
            "the System class must not be offerable on class alone");

        // The things that stop the machine coming back must be refused whatever else changes.
        foreach (var deadly in (string[])
                 [
                     @"ACPI_HAL\PNP0C08\0",
                     @"ACPI\PNP0A08\0",
                     @"ACPI\PNP0C02\IOTRAPS",
                     @"ACPI\PNP0100\4&104CD1ED&0",
                     @"ACPI\PNP0B00\4&104CD1ED&0",
                     @"ACPI\PNP0000\4&104CD1ED&0",
                     @"ACPI\PNP0C09\0",
                     @"PCI\VEN_8086&DEV_7D30&SUBSYS_0D631028&REV_05"
                 ])
        {
            Assert(DeviceClassPolicy.FindDeviceViolation("System", deadly) is not null,
                $"{deadly} must never be offerable");
        }

        // Unknown System devices fail closed rather than open.
        Assert(DeviceClassPolicy.FindDeviceViolation("System", @"ACPI\VEND1234\0") is not null,
            "an unlisted System device must be refused");
        Assert(DeviceClassPolicy.FindDeviceViolation("System", string.Empty) is not null,
            "a System device with no instance identifier must be refused");

        // And the named ones are permitted, with a statement of what is lost.
        foreach (var (prefix, loss) in DeviceClassPolicy.OfferableSystemDevices)
        {
            Assert(DeviceClassPolicy.FindDeviceViolation("System", prefix + @"\0") is null,
                $"{prefix} is listed as offerable but the policy refuses it");
            Assert(loss.Length > 40, $"{prefix} must state what is lost");
        }

        // The guard must agree, since a restore replays through it rather than through the scanner.
        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.DeviceState, @"ACPI\PNP0A08\0", "System") is not null,
            "the allowlist must refuse the PCI root complex");
        Assert(
            MutationAllowlist.FindViolation(
                MutationKind.DeviceState, @"ACPI\PNP0103\0", "System") is null,
            "the allowlist must permit the event timer");
        return Task.CompletedTask;
    }

    private static Task TestReplayOfUnknownKeyAsync()
    {
        var reader = new ReplayStateReader([new RecordedRead("RegistryValue|A|B", true, "1")]);
        var known = new MutationPlan("m1", MutationKind.RegistryValue, "A", "B", "0", "DWord", "known");
        var unknown = new MutationPlan("m2", MutationKind.RegistryValue, "C", "D", "0", "DWord", "unknown");

        Assert(reader.Read(known, out var knownExists) == "1", "a recorded read must replay its value");
        Assert(knownExists, "a recorded read must replay as present");

        // Not knowing must present as absent rather than as a default, so the card degrades to
        // unreadable and offers no write. Guessing here would invent state for a machine that is
        // not even switched on.
        Assert(reader.Read(unknown, out var unknownExists) is null, "an unrecorded read must be null");
        Assert(!unknownExists, "an unrecorded read must report the surface as absent");
        return Task.CompletedTask;
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

    private sealed class IntentObservingExecutor(
        IMutationExecutor inner,
        Func<MutationPlan, bool> hasDurableIntent) : IMutationExecutor
    {
        public bool SawDurableIntent { get; private set; }

        public string? Read(MutationPlan plan, out bool exists) => inner.Read(plan, out exists);

        public MutationRecord Capture(MutationPlan plan) => inner.Capture(plan);

        public MutationRecord Apply(MutationPlan plan)
            => throw new InvalidOperationException("Engine must apply from its journalled capture.");

        public MutationRecord Apply(MutationPlan plan, MutationRecord captured)
        {
            SawDurableIntent = hasDurableIntent(plan);
            if (!SawDurableIntent)
            {
                throw new InvalidOperationException("No durable write intent was observed.");
            }

            return inner.Apply(plan, captured);
        }

        public MutationRecord Revert(MutationRecord record) => inner.Revert(record);

        public bool RequiresElevation(MutationPlan plan) => inner.RequiresElevation(plan);
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

using System.Globalization;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

/// <summary>
/// The expert tier: tweaks that change measurable frame delivery or input timing on a machine that
/// already has the obvious settings right.
///
/// Every entry states the mechanism it acts on, not just a value to set. An entry that cannot be
/// read on this machine reports Unknown and offers no write, and an entry whose recommended value
/// is already live reports Optimal and offers no write, so the list never manufactures work.
/// </summary>
public static class ExpertTweakCatalog
{
    private const string ProcessorSubgroup = "54533251-82be-4824-96c1-47b60b740d00";
    private const string MinProcessorState = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string CoreParkingMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string PerformanceBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";
    private const string BestPerformanceOverlay = "ded574b5-45a0-4f42-8737-46345c09c238";

    private const string MmcssProfilePath =
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private const string MmcssGamesPath =
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    private const string KernelPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string GraphicsDriversPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string HvciPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string PriorityControlPath = @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string GpuPreferencesPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string GameDvrPath = @"HKCU\System\GameConfigStore";
    private const string GameDvrAppPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameBarPath = @"HKCU\Software\Microsoft\GameBar";
    private const string AppCompatLayersPath = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private static readonly EvidenceSource MicrosoftMmcss = new(
        "Microsoft multimedia class scheduler service",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service"));

    private static readonly EvidenceSource MicrosoftProcessorPolicy = new(
        "Microsoft processor power management options",
        new Uri("https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/processor-power-management-options"));

    private static readonly EvidenceSource MicrosoftPowerMode = new(
        "Microsoft PowerGetUserConfiguredACPowerMode",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode"));

    private static readonly EvidenceSource MicrosoftHags = new(
        "Microsoft hardware-accelerated GPU scheduling",
        new Uri("https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/"));

    private static readonly EvidenceSource MicrosoftEcoQoS = new(
        "Microsoft PROCESS_POWER_THROTTLING_STATE",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_power_throttling_state"));

    private static readonly EvidenceSource MicrosoftAffinity = new(
        "Microsoft SetProcessAffinityMask",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setprocessaffinitymask"));

    private static readonly EvidenceSource MicrosoftMemoryIntegrity = new(
        "Microsoft memory integrity and device security",
        new Uri("https://support.microsoft.com/en-us/windows/core-isolation-e30ed737-17d8-42f3-a2a9-87521df09b78"));

    private static readonly EvidenceSource MicrosoftPointer = new(
        "Microsoft SystemParametersInfo pointer settings",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow"));

    private static readonly EvidenceSource MicrosoftTimers = new(
        "Microsoft high-resolution timers",
        new Uri("https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps"));

    private static readonly EvidenceSource NvidiaReflex = new(
        "NVIDIA Reflex low latency",
        new Uri("https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/"));

    public static IReadOnlyList<ExpertTweakCard> Evaluate(ExpertScanContext context, ITweakStateReader reader)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reader);

        var cards = new List<ExpertTweakCard>
        {
            CorePlacement(context),
            CoreParking(context, reader),
            MinimumProcessorState(context, reader),
            BoostMode(context, reader),
            ProcessPowerThrottling(context, reader),
            ClockCeiling(context),
            PowerModeOverlay(context, reader),
            GlobalTimerRequests(context, reader),
            SchedulerPunctuality(context),
            MmcssResponsiveness(reader),
            MmcssGamesTask(reader),
            QuantumPolicy(reader),
            HardwareScheduling(context, reader),
            PcieLink(context),
            GpuClockLimiter(context),
            WindowedOptimizations(reader),
            GpuPreference(context, reader),
            FullscreenOptimizations(context, reader),
            AdvancedColor(context),
            FrameCapTarget(context),
            PointerAcceleration(context, reader),
            PointerSpeed(context, reader),
            PollingIntegrity(context),
            GameDvr(reader),
            GameMode(reader),
            MemoryIntegrity(reader),
            MemoryProfile(context),
            MemoryChannels(context),
            StackedCacheProfile(context),
            ResizableBar(context),
            GpuInterruptMode(context, reader),
            ForcedPlatformClock(context),
            SteamTransfer(context),
            AudioSampleRate(context),
            AudioSpatialProcessing(context),
            AudioEffects(context),
            NetworkPathStability(context),
            PanelNativeTiming(context),
            FastStartup(context, reader),
            InterruptAffinityPolicy(context),
            DefenderExclusion(context),
            FrameLimiterStrategy(context),
            NetworkThrottling(reader),
            PowerThrottling(reader),
            DeliveryOptimization(reader),
            BackgroundApplications(reader),
            PagedKernel(reader),
            GameDvrPolicy(reader),
            SystemWideFullscreenBehaviour(reader),
            DesktopTransparency(reader),
            TelemetryAutologger(reader),
            NetworkInterruptMode(context),
            BootTiming(context),
            SpeculativeMitigations(context)
        };

        cards.AddRange(NvidiaProfileCards(context));
        cards.AddRange(DebunkRegister());

        cards.AddRange(NetworkTweaks(context, reader));

        return cards
            .Select(ExpertTweakPolicy.Apply)
            .OrderBy(card => StateOrder(card.Reading.State))
            .ThenBy(card => card.Definition.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => card.Definition.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int StateOrder(TweakState state)
        => state switch
        {
            TweakState.Suboptimal => 0,
            TweakState.Blocked => 1,
            TweakState.Unknown => 2,
            TweakState.Optimal => 3,
            _ => 4
        };

    // ---- CPU placement and power policy ---------------------------------------------------

    private static ExpertTweakCard CorePlacement(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "CPU-PLACEMENT-001",
            "CPU",
            "Game thread placement across dies and core classes",
            "Windows schedules threads across every core group it is given. On an asymmetric CPU that means a "
            + "cache-sensitive engine can land on the die without the stacked L3, or on efficiency cores.",
            "A competitive engine is latency- and cache-bound rather than throughput-bound. Confining it to the "
            + "die carrying the large last-level cache, or to the performance-core set, removes cross-die cache "
            + "misses and efficiency-core scheduling excursions. This is the largest single scheduling factor on "
            + "a modern asymmetric desktop CPU.",
            "Applied at launch rather than to a running game. Confining threads reduces total available cores, "
            + "which can cost throughput in a CPU-saturated workload, and the mask has to be reapplied each time "
            + "the game starts.",
            TweakRisk.Moderate,
            TweakScope.RunningProcess,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            [MicrosoftAffinity]);

        var cpu = context.Cpu;
        if (!cpu.HasDistinctPreferredGroup)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.NotApplicable,
                    $"{cpu.CoreGroups.Count} core group(s), symmetric",
                    "No placement change",
                    cpu.PreferredGroupReason),
                [],
                null);
        }

        var preferredMask = cpu.PreferredAffinityMask;
        var recommended = $"0x{preferredMask:X} ({System.Numerics.BitOperations.PopCount(preferredMask)} logical processors)";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                $"Preferred topology mask would be {recommended}",
                "Leave game affinity unmanaged",
                cpu.PreferredGroupReason + " The running game process is not opened or inspected."),
            [],
            "Excluded: affinity folklore and game-process mutation are outside the product boundary.");
    }

    private static ExpertTweakCard CoreParking(ExpertScanContext context, ITweakStateReader reader)
        => PowerPolicyCard(
            "CPU-PARKING-001",
            "Processor core parking floor",
            "Core parking takes cores offline at low utilisation. Unparking one under load costs a scheduling "
            + "excursion at exactly the moment a frame needs it.",
            "A game that alternates between light and heavy frames repeatedly triggers park and unpark cycles. "
            + "Holding every core available removes that transition from the frame path.",
            "All cores stay clocked and drawing power, which raises idle power and temperature.",
            CoreParkingMinCores,
            100,
            "100% of cores kept unparked",
            reader,
            context);

    private static ExpertTweakCard MinimumProcessorState(ExpertScanContext context, ITweakStateReader reader)
    {
        // On a stacked-cache part the power budget is the scarce resource, not the ramp. Holding
        // every core at its floor spends that budget on idle cores and competes with the boost
        // headroom the active ones need, so the recommendation stops being a default there.
        var stacked = context.Cpu.HasStackedCache;
        return PowerPolicyCard(
            "CPU-MINSTATE-001",
            "Minimum processor performance state",
            "The processor performance floor governs how far the CPU is allowed to drop between bursts, and how "
            + "far it must ramp back up when a frame arrives.",
            "Ramp-up is not instant. A high floor removes the ramp from the frame path, which shows up in 1% lows "
            + "rather than in average frame rate.",
            stacked
                ? "This CPU carries stacked cache, so it is power- and thermally-limited by design. Raising the "
                + "floor keeps idle cores clocked and spends budget the boost algorithm would otherwise give to "
                + "the cores running the frame. Benchmark both settings; do not assume the higher floor wins."
                : "The CPU holds higher clocks at idle, raising power draw, temperature and fan noise. On a "
                + "thermally-limited machine this can reduce sustained boost rather than improve it.",
            MinProcessorState,
            100,
            "100%",
            reader,
            context,
            stacked ? TweakRisk.High : TweakRisk.Moderate,
            stacked
                ? " On a stacked-cache CPU this is an A/B, not a recommendation."
                : string.Empty);
    }

    private static ExpertTweakCard BoostMode(ExpertScanContext context, ITweakStateReader reader)
        => PowerPolicyCard(
            "CPU-BOOST-001",
            "Processor performance boost mode",
            "Boost mode selects how aggressively the platform grants opportunistic frequency above the "
            + "guaranteed base clock.",
            "An aggressive boost policy shortens the delay between a load arriving and the clock responding to it.",
            "Raises power and temperature. On a chassis that is already power- or thermally-limited this can "
            + "trade sustained clocks for short peaks.",
            PerformanceBoostMode,
            2,
            "Aggressive",
            reader,
            context);

    private static ExpertTweakCard PowerPolicyCard(
        string id,
        string title,
        string mechanism,
        string rationale,
        string tradeoff,
        string settingGuid,
        uint desired,
        string desiredLabel,
        ITweakStateReader reader,
        ExpertScanContext context,
        TweakRisk risk = TweakRisk.Moderate,
        string detailSuffix = "")
    {
        var definition = new ExpertTweakDefinition(
            id,
            "CPU",
            title,
            mechanism,
            rationale,
            tradeoff,
            risk,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            [MicrosoftProcessorPolicy]);

        var target = $"{ProcessorSubgroup}:{settingGuid}";
        var plan = new MutationPlan(
            $"{id}.value",
            MutationKind.PowerSchemeValue,
            target,
            title,
            desired.ToString(CultureInfo.InvariantCulture),
            "UInt32",
            $"Set {title} to {desiredLabel} on the active power scheme");

        var current = reader.Read(plan, out var exists);
        if (!exists || current is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.Unknown,
                    "Not exposed by the active power scheme",
                    desiredLabel,
                    "This platform does not expose the setting, so no write is offered."),
                [],
                null);
        }

        if (!context.Environment.Power.IsOnAc)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Blocked, current, desiredLabel,
                    "Processor power policy is only changed on AC power."),
                [],
                "AC power was not positively detected.");
        }

        var matches = uint.TryParse(current, out var value) && value == desired;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                matches ? TweakState.Optimal : TweakState.Suboptimal,
                current,
                desiredLabel,
                (matches ? "Already at the recommended value." : "The active scheme is below the recommended value.")
                + detailSuffix),
            matches ? [] : [plan],
            null);
    }

    private static ExpertTweakCard ProcessPowerThrottling(ExpertScanContext context, ITweakStateReader reader)
    {
        _ = reader;
        var definition = new ExpertTweakDefinition(
            "CPU-ECOQOS-001",
            "CPU",
            "Efficiency-mode throttling on the game process",
            "Windows can place a process into EcoQoS, which biases it onto efficiency cores and lower clocks.",
            "A game that has been marked for efficiency execution runs at a deliberately reduced performance "
            + "target. Clearing the throttle returns it to the normal quality-of-service class.",
            "Removes an energy-saving behaviour for that process only. Reverts on its own when the game exits.",
            TweakRisk.Low,
            TweakScope.RunningProcess,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            [MicrosoftEcoQoS]);

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                "Game process not inspected",
                "Leave game process scheduling under Windows and the game",
                $"FramePath Lab does not open {context.GameExecutableName} to query or change EcoQoS."),
            [],
            "Excluded: game-process manipulation is outside the product integrity boundary.");
    }

    private static ExpertTweakCard ClockCeiling(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "CPU-CEILING-001",
            "CPU",
            "Processor frequency ceiling",
            "The platform reports a maximum frequency and a currently enforced limit. A limit below the maximum "
            + "means firmware, thermal or power policy is holding the CPU down.",
            "No Windows setting can recover clocks that the platform itself is capping. Naming this explicitly "
            + "prevents chasing software tweaks for a firmware or cooling problem.",
            "Diagnostic only. FramePath Lab does not change firmware or power limits.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            [MicrosoftProcessorPolicy]);

        var cpu = context.Cpu;
        if (cpu.MaxMhz is null or 0 || cpu.MhzLimit is null or 0)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Not reported", "Limit equal to maximum",
                    "The platform did not return processor frequency information."),
                [],
                null);
        }

        var limited = cpu.IsClockLimited;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                limited ? TweakState.Suboptimal : TweakState.Optimal,
                $"{cpu.CurrentMhz} MHz now; ceiling {cpu.MhzLimit} MHz of {cpu.MaxMhz} MHz rated",
                $"{cpu.MaxMhz} MHz",
                limited
                    ? $"The platform is enforcing a {cpu.MhzLimit} MHz ceiling against a {cpu.MaxMhz} MHz rating. "
                      + "Investigate cooling, chassis power limits or firmware before changing Windows settings."
                    : "No frequency ceiling below the rated maximum is being enforced."),
            [],
            limited ? "Firmware or thermal limits are outside the scope of any software change." : null);
    }

    private static ExpertTweakCard PowerModeOverlay(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "POWER-OVERLAY-001",
            "Windows",
            "Windows AC power mode preference",
            "Windows 11 records a user-selected AC power mode separately from the legacy power plan. The system "
            + "may still override that preference in response to policy, thermal or battery signals.",
            "Best performance can reduce frequency ramp-up delay on a platform that honors the preference, but "
            + "the effect is configuration-specific and must be measured rather than assumed.",
            "Raises power draw and temperature across the whole session, not just in game.",
            TweakRisk.Moderate,
            TweakScope.CurrentUser,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            [MicrosoftPowerMode]);

        var plan = new MutationPlan(
            "POWER-OVERLAY-001.overlay",
            MutationKind.PowerOverlayScheme,
            "user-ac-power-mode",
            "ConfiguredACPowerMode",
            BestPerformanceOverlay,
            "Guid",
            "Set the Windows power mode overlay to Best performance");

        var current = reader.Read(plan, out var exists);
        if (!exists || current is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Overlay not exposed", "Best performance",
                    "This Windows build did not return the user-configured AC power mode."),
                [],
                null);
        }

        if (!context.Environment.Power.IsOnAc)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Blocked, DescribeOverlay(current), "Best performance",
                    "Power mode is only changed on AC power."),
                [],
                "AC power was not positively detected.");
        }

        var optimal = Guid.TryParse(current, out var currentGuid)
                      && currentGuid == Guid.Parse(BestPerformanceOverlay);
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                optimal ? TweakState.Optimal : TweakState.Suboptimal,
                DescribeOverlay(current),
                "Best performance",
                optimal
                    ? "The overlay is already set to the highest performance mode."
                    : "The plan may already be a performance plan while this overlay still biases toward efficiency."),
            optimal ? [] : [plan],
            null);
    }

    private static string DescribeOverlay(string guid)
        => guid.ToUpperInvariant() switch
        {
            "DED574B5-45A0-4F42-8737-46345C09C238" => "Best performance",
            "961CC777-2547-4F9D-8174-7D86181B8A7A" => "Best power efficiency",
            "00000000-0000-0000-0000-000000000000" => "Balanced",
            _ => $"Overlay {guid}"
        };

    // ---- Timing and scheduling ------------------------------------------------------------

    private static ExpertTweakCard GlobalTimerRequests(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "TIMER-GLOBAL-001",
            "Timing",
            "Global timer resolution requests",
            "Windows 11 22H2 made timer-resolution requests process-scoped. A game requesting a fine timer no "
            + "longer necessarily raises the tick it is actually scheduled against.",
            "Restoring global honouring of timer requests returns the finer system tick that sleep- and "
            + "wait-driven engine loops depend on for punctual wake-ups.",
            "A finer system-wide tick raises idle power slightly. Requires a restart to take effect. This is a "
            + "documented policy value, not a forced platform clock override.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            true,
            false,
            [MicrosoftTimers]);

        var plan = new MutationPlan(
            "TIMER-GLOBAL-001.value",
            MutationKind.RegistryValue,
            KernelPath,
            "GlobalTimerResolutionRequests",
            "1",
            "DWord",
            "Restore global honouring of timer-resolution requests");

        var current = reader.Read(plan, out var exists);
        var enabled = exists && current == "1";
        var timerDetail = context.Latency is null
            ? string.Empty
            : $" Current system timer resolution is {context.Latency.CurrentTimerResolutionMs:0.###} ms.";

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Optimal : TweakState.Suboptimal,
                enabled ? "Enabled" : exists ? current ?? "0" : "Not set (per-process behaviour)",
                "Enabled",
                (enabled
                    ? "Timer requests are honoured system-wide."
                    : "Timer requests stay scoped to the requesting process.") + timerDetail),
            enabled ? [] : [plan],
            null);
    }

    private static ExpertTweakCard SchedulerPunctuality(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "TIMING-JITTER-001",
            "Timing",
            "Thread wake-up punctuality",
            "Measures how late a managed thread wakes after requesting a short sleep.",
            "The result is sensitive to timer state, the managed runtime, power state and background activity. "
            + "It can flag a noisy session for follow-up, but cannot attribute delay to DPC/ISR work or CS2.",
            "Diagnostic only. No system change is offered from this measurement.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            [MicrosoftTimers]);

        if (context.Latency is null || context.Latency.JitterSampleCount == 0)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Not measured", "Compare repeated controlled runs",
                    "Run the expert scan to sample scheduler punctuality."),
                [],
                null);
        }

        var latency = context.Latency;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                $"median {latency.SchedulerJitterMedianMs:0.###} ms, P99 {latency.SchedulerJitterP99Ms:0.###} ms, "
                + $"worst {latency.SchedulerJitterWorstMs:0.###} ms",
                "Compare repeated controlled runs; use WPR/ETW for attribution",
                "This single probe is descriptive only and cannot classify the system as good or bad."),
            [],
            "DPC/ISR or driver attribution requires a qualified WPR/ETW trace.");
    }

    private static ExpertTweakCard MmcssResponsiveness(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "MMCSS-001",
            "Windows",
            "Multimedia scheduler CPU reservation",
            "The multimedia class scheduler reserves a share of CPU time for non-multimedia work. The default "
            + "reserves twenty percent.",
            "Lowering the reservation returns scheduling headroom to registered multimedia and game threads under "
            + "contention. It only has an effect when the system is actually contended.",
            "Reduces the guaranteed share for background work. Setting it to zero can starve background tasks; "
            + "this catalogue uses ten rather than zero for that reason.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            true,
            false,
            [MicrosoftMmcss]);

        var plan = new MutationPlan(
            "MMCSS-001.value",
            MutationKind.RegistryValue,
            MmcssProfilePath,
            "SystemResponsiveness",
            "10",
            "DWord",
            "Reduce the multimedia scheduler CPU reservation to 10%");

        var current = reader.Read(plan, out var exists);
        var value = exists && int.TryParse(current, out var parsed) ? parsed : 20;
        var optimal = value <= 10;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                optimal ? TweakState.Optimal : TweakState.Suboptimal,
                $"{value}% reserved for background work",
                "10% reserved",
                optimal
                    ? "The reservation is already at or below the recommended value."
                    : "The default reservation is in effect."),
            optimal ? [] : [plan],
            null);
    }

    private static ExpertTweakCard MmcssGamesTask(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "MMCSS-GAMES-001",
            "Windows",
            "Multimedia scheduler Games task priority",
            "Threads that register with the Games multimedia task inherit the priority and GPU priority defined "
            + "for that task.",
            "Raising the task's scheduling category and GPU priority gives registered game threads precedence "
            + "over background work at the moments contention actually occurs.",
            "Affects any application registering under the Games task, not only the intended game.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            true,
            false,
            [MicrosoftMmcss]);

        var plans = new[]
        {
            new MutationPlan("MMCSS-GAMES-001.gpu", MutationKind.RegistryValue, MmcssGamesPath,
                "GPU Priority", "8", "DWord", "Raise Games task GPU priority to 8"),
            new MutationPlan("MMCSS-GAMES-001.priority", MutationKind.RegistryValue, MmcssGamesPath,
                "Priority", "6", "DWord", "Raise Games task priority to 6"),
            new MutationPlan("MMCSS-GAMES-001.category", MutationKind.RegistryValue, MmcssGamesPath,
                "Scheduling Category", "High", "String", "Set Games task scheduling category to High"),
            new MutationPlan("MMCSS-GAMES-001.sfio", MutationKind.RegistryValue, MmcssGamesPath,
                "SFIO Priority", "High", "String", "Set Games task scheduled I/O priority to High")
        };

        var pending = plans.Where(plan =>
        {
            var current = reader.Read(plan, out var exists);
            return !exists || !string.Equals(current, plan.DesiredValue, StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        var currentCategory = reader.Read(plans[2], out _) ?? "not set";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                pending.Length == 0 ? TweakState.Optimal : TweakState.Suboptimal,
                $"Scheduling category {currentCategory}; {pending.Length} of {plans.Length} values below target",
                "GPU priority 8, priority 6, category High",
                pending.Length == 0
                    ? "The Games task is already configured for high-priority scheduling."
                    : "The Games task is at its default configuration."),
            pending,
            null);
    }

    private static ExpertTweakCard QuantumPolicy(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "SCHED-QUANTUM-001",
            "Windows",
            "Foreground thread quantum policy",
            "Priority separation controls the length of a thread's scheduling quantum and how much extra the "
            + "foreground application receives.",
            "Short, variable quanta with a strong foreground bias hand the active game more consecutive CPU time "
            + "per scheduling decision, which reduces preemption inside a frame.",
            "A system-wide scheduling change. It biases against background work and its measurable benefit varies "
            + "widely by workload; treat it as an A/B rather than a guaranteed gain.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Weak,
            true,
            true,
            false,
            [MicrosoftMmcss]);

        var plan = new MutationPlan(
            "SCHED-QUANTUM-001.value",
            MutationKind.RegistryValue,
            PriorityControlPath,
            "Win32PrioritySeparation",
            "38",
            "DWord",
            "Set priority separation to short, variable quanta with a 3:1 foreground bias");

        var current = reader.Read(plan, out var exists);
        var optimal = exists && current == "38";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                optimal ? TweakState.Optimal : TweakState.Suboptimal,
                exists ? current ?? "unset" : "unset (Windows default 2)",
                "38",
                optimal
                    ? "Already at the foreground-biased quantum policy."
                    : "Windows is using its default quantum policy. Benefit is workload-dependent; measure it."),
            optimal ? [] : [plan],
            null);
    }

    // ---- GPU and presentation -------------------------------------------------------------

    private static ExpertTweakCard HardwareScheduling(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-HAGS-001",
            "GPU",
            "Hardware-accelerated GPU scheduling",
            "Hardware scheduling moves queue management from the operating system onto the GPU's own scheduler, "
            + "shortening the submission path.",
            "It is a prerequisite for some vendor low-latency paths and can reduce submission overhead. It can "
            + "equally do nothing or regress a specific driver and card, so this build sends the user to the "
            + "supported Windows Settings surface rather than inferring effective state.",
            "Requires a restart. Can help, do nothing, or regress depending on hardware and driver revision.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            true,
            false,
            [MicrosoftHags]);

        var gpu = context.Gpus.FirstOrDefault(device => device.HardwareSchedulingSupported.HasValue);
        if (gpu?.HardwareSchedulingSupported is not true)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    gpu is null ? TweakState.Unknown : TweakState.NotApplicable,
                    gpu is null ? "Capability query returned nothing" : "Not supported by this adapter or driver",
                    "Enabled",
                    "This build does not infer effective HAGS state from a registry value or reserved driver structure."),
                [],
                null);
        }

        var enabled = gpu.HardwareSchedulingEnabled == true;
        var plan = new MutationPlan(
            "GPU-HAGS-001.mode",
            MutationKind.RegistryValue,
            GraphicsDriversPath,
            "HwSchMode",
            "2",
            "DWord",
            "Enable hardware-accelerated GPU scheduling at the next restart");

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Optimal : TweakState.Suboptimal,
                enabled ? "Enabled and active" : "Supported but not active",
                "Enabled",
                enabled
                    ? "The driver reports hardware scheduling as active."
                    : "The adapter supports hardware scheduling but it is not currently active. "
                      + "Test it as a reboot-separated A/B, not as a default."),
            enabled ? [] : [plan],
            null);
    }

    private static ExpertTweakCard PcieLink(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-PCIE-001",
            "GPU",
            "PCIe link negotiation",
            "The GPU negotiates a link width and generation with the platform. A link below the card's maximum "
            + "halves or quarters available host bandwidth.",
            "A card silently running at reduced width is a hardware or firmware condition no Windows setting can "
            + "fix, and it caps performance regardless of every other tweak on this list.",
            "Diagnostic only. Investigate slot population, riser cables and firmware settings.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            []);

        var gpu = context.Gpus.FirstOrDefault(device => device.TelemetryAvailable);
        if (gpu is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "No GPU telemetry source available", "Full width and generation",
                    "PCIe link state is read through NVIDIA's management library; other vendors are not read by this build."),
                [],
                null);
        }

        var degraded = gpu.IsPcieDegraded;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                degraded ? TweakState.Suboptimal : TweakState.Optimal,
                gpu.Observation,
                $"x{gpu.PcieMaxLinkWidth} at Gen {gpu.PcieMaxLinkGeneration}",
                degraded
                    ? "The link is narrower than the card supports. Note that idle link-width reduction is normal; "
                      + "confirm under load before treating this as a fault."
                    : "The card negotiated its full link width."),
            [],
            degraded ? "Link width is a platform and firmware condition, not a Windows setting." : null);
    }

    private static ExpertTweakCard GpuClockLimiter(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-LIMITER-001",
            "GPU",
            "Active GPU clock limiter",
            "The driver reports why it is currently holding clocks below their opportunistic maximum: power cap, "
            + "thermal slowdown, or an explicit clock setting.",
            "A thermal or power limiter engaging mid-session is the usual cause of 1% lows collapsing partway "
            + "through a match while average frame rate still looks healthy.",
            "Diagnostic only. Cooling and power limits are outside the scope of any Windows setting.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            []);

        var gpu = context.Gpus.FirstOrDefault(device => device.TelemetryAvailable);
        if (gpu is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "No GPU telemetry source available", "No limiter under load",
                    "Clock limiter reasons are read through NVIDIA's management library."),
                [],
                null);
        }

        var limited = gpu.HasNonPowerThrottle;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                limited ? TweakState.Suboptimal : TweakState.Optimal,
                gpu.ThrottleReasons.Count > 0 ? string.Join(", ", gpu.ThrottleReasons) : "No limiter reported",
                "No limiter under load",
                limited
                    ? "A limiter is engaged. Re-read this during a match: a limiter at idle means little, one "
                      + "under load explains lost frames."
                    : "No clock limiter was engaged at scan time. This is an idle reading unless the game is running."),
            [],
            null);
    }

    private static ExpertTweakCard WindowedOptimizations(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "DX-SWAPCHAIN-001",
            "Presentation",
            "Optimisations for windowed games",
            "This setting lets Windows upgrade a legacy windowed swap chain onto the flip presentation model.",
            "Without the flip model a windowed or borderless game is composited by the desktop window manager, "
            + "which adds a frame of latency versus an independent flip. This is the setting that decides whether "
            + "borderless can reach the same present path as exclusive fullscreen.",
            "Applies to windowed and borderless presentation. Confirm the result in a capture: the present mode "
            + "is the evidence, not the toggle.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            []);

        var plan = new MutationPlan(
            "DX-SWAPCHAIN-001.value",
            MutationKind.RegistryValue,
            GpuPreferencesPath,
            "DirectXUserGlobalSettings",
            "SwapEffectUpgradeEnable=1;",
            "String",
            "Enable optimisations for windowed games");

        var current = reader.Read(plan, out var exists);
        var enabled = exists && current is not null
                              && current.Contains("SwapEffectUpgradeEnable=1", StringComparison.OrdinalIgnoreCase);
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Optimal : TweakState.Suboptimal,
                exists ? current ?? "unset" : "unset (Windows default)",
                "SwapEffectUpgradeEnable=1",
                enabled
                    ? "Windowed swap-chain upgrade is enabled."
                    : "Windowed presentation may be composited rather than flipped."),
            enabled ? [] : [plan],
            null);
    }

    private static ExpertTweakCard GpuPreference(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "DX-GPUPREF-001",
            "Presentation",
            "Per-application GPU preference",
            "Windows records a per-executable GPU preference that overrides the driver's own automatic selection.",
            "On any system with more than one adapter this pins the game to the high-performance device instead "
            + "of leaving the choice to heuristics that can change with a driver update.",
            "Only meaningful where more than one adapter exists. Takes effect at the next game launch.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            []);

        if (context.Gpus.Count < 2)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.NotApplicable, $"{context.Gpus.Count} adapter(s) present",
                    "High performance", "Only one adapter was resolved, so there is no selection to make."),
                [],
                null);
        }

        var executablePath = context.GameExecutableName + ".exe";
        var plan = new MutationPlan(
            "DX-GPUPREF-001.value",
            MutationKind.RegistryValue,
            GpuPreferencesPath,
            executablePath,
            "GpuPreference=2;",
            "String",
            $"Pin {executablePath} to the high-performance adapter");

        var current = reader.Read(plan, out var exists);
        var pinned = exists && current is not null
                             && current.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase);
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                pinned ? TweakState.Optimal : TweakState.Suboptimal,
                exists ? current ?? "unset" : "unset (automatic selection)",
                "GpuPreference=2 (high performance)",
                pinned ? "The executable is pinned to the high-performance adapter." : "Adapter selection is left to Windows."),
            pinned ? [] : [plan],
            null);
    }

    private static ExpertTweakCard FullscreenOptimizations(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "DX-FSO-001",
            "Presentation",
            "Fullscreen optimisations compatibility flag",
            "Fullscreen optimisations run a fullscreen title through a borderless flip path instead of an "
            + "exclusive one.",
            "Which path is faster is genuinely engine- and driver-dependent. This entry exists so the flag can be "
            + "set deliberately and measured, rather than left at an unknown default while a capture is analysed.",
            "This is an A/B, not an improvement. Verify against the present mode in a capture before keeping it.",
            TweakRisk.Moderate,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false,
            false,
            true,
            []);

        var gamePath = context.GameExecutableName + ".exe";
        var plan = new MutationPlan(
            "DX-FSO-001.value",
            MutationKind.RegistryValue,
            AppCompatLayersPath,
            gamePath,
            "~ DISABLEDXMAXIMIZEDWINDOWEDMODE",
            "String",
            $"Disable fullscreen optimisations for {gamePath}");

        var current = reader.Read(plan, out var exists);
        var disabled = exists && current is not null
                               && current.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase);
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                disabled ? TweakState.Optimal : TweakState.Suboptimal,
                disabled ? "Fullscreen optimisations disabled" : "Windows default (enabled)",
                "Disabled, then measured",
                "Set this deliberately and compare present mode and latency between the two states."),
            disabled ? [] : [plan],
            null);
    }

    private static ExpertTweakCard AdvancedColor(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "DISPLAY-HDR-001",
            "Display",
            "High dynamic range on the primary display",
            "HDR changes the composition and scan-out path, and on many configurations forces desktop "
            + "composition where an independent flip would otherwise occur.",
            "For a competitive title rendered at low settings, HDR adds presentation cost and can cost the "
            + "independent flip path without any competitive benefit.",
            "Diagnostic only in this build. HDR is a documented Windows display setting the user controls.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            []);

        var timing = context.PrimaryTiming;
        if (timing is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Display timing not resolved", "Off for competitive play",
                    "Exact display timing could not be read."),
                [],
                null);
        }

        if (!timing.AdvancedColorSupported)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.NotApplicable, "Not supported by this display", "Off for competitive play",
                    "The display does not advertise advanced colour."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                timing.AdvancedColorEnabled ? TweakState.Suboptimal : TweakState.Optimal,
                timing.AdvancedColorEnabled ? "Enabled" : "Disabled",
                "Off for competitive play",
                timing.AdvancedColorEnabled
                    ? "HDR is active. Confirm the present mode in a capture; it is a common cause of losing an "
                      + "independent flip."
                    : "HDR is off, which keeps the simplest presentation path available."),
            [],
            timing.AdvancedColorEnabled ? "HDR is changed in Windows display settings, not written by this build." : null);
    }

    private static ExpertTweakCard FrameCapTarget(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "DISPLAY-CAP-001",
            "Display",
            "Frame cap derived from exact refresh timing",
            "A variable-refresh display with vertical sync enabled re-introduces queued latency the moment the "
            + "frame rate reaches the refresh ceiling. The cap has to sit below that ceiling.",
            "This value is computed from the display's true rational refresh rate rather than from a rounded "
            + "integer, so it holds for 59.94-class timings and for high-refresh panels alike.",
            "The correct cap depends on whether variable refresh and a vendor latency path are actually engaged. "
            + "Verify against a capture rather than assuming.",
            TweakRisk.Low,
            TweakScope.VendorControlPanel,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            [NvidiaReflex]);

        var timing = context.PrimaryTiming;
        if (timing is null || timing.ExactRefreshHz <= 0)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Exact refresh not resolved", "Computed cap",
                    "QueryDisplayConfig did not return a usable rational refresh rate."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Optimal,
                $"{timing.ExactRefreshHz:0.###} Hz exact "
                + $"({timing.VerticalNumerator}/{timing.VerticalDenominator})",
                $"{timing.RecommendedVrrCap} FPS with variable refresh and vertical sync engaged",
                $"With variable refresh plus vertical sync plus a vendor low-latency path, cap at "
                + $"{timing.RecommendedVrrCap}. With vertical sync off and tearing accepted, run uncapped. "
                + "Set one, measure, then change the other."),
            [],
            null);
    }

    // ---- Input ----------------------------------------------------------------------------

    private static ExpertTweakCard PointerAcceleration(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "INPUT-ACCEL-001",
            "Input",
            "Pointer acceleration",
            "Pointer acceleration scales cursor travel by the speed of the physical movement, so identical hand "
            + "movements produce different on-screen distances.",
            "Consistent muscle memory requires a fixed relationship between hand distance and view angle. "
            + "Acceleration breaks that relationship by design.",
            "Changes desktop pointer feel as well as in-game feel.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            [MicrosoftPointer]);

        var plan = new MutationPlan(
            "INPUT-ACCEL-001.value",
            MutationKind.SystemParameter,
            "pointer.acceleration",
            "EnhancePointerPrecision",
            "0",
            "Flag",
            "Disable enhanced pointer precision");

        var current = reader.Read(plan, out _);
        var enabled = current == "1";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Suboptimal : TweakState.Optimal,
                enabled ? "Enabled" : "Disabled",
                "Disabled",
                enabled
                    ? "Cursor travel currently varies with movement speed."
                    : "Pointer movement is already a fixed ratio."),
            enabled ? [plan] : [],
            null);
    }

    private static ExpertTweakCard PointerSpeed(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "INPUT-SPEED-001",
            "Input",
            "Pointer speed multiplier",
            "The Windows pointer speed slider multiplies incoming mouse counts. Only the middle notch passes "
            + "counts through unscaled.",
            "Any other value scales counts before the game receives them, discarding resolution on the way down "
            + "or interpolating on the way up. The middle notch is the only lossless setting.",
            "Changes desktop pointer speed. Compensate with the mouse's own resolution setting rather than with "
            + "the Windows slider.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            [MicrosoftPointer]);

        var plan = new MutationPlan(
            "INPUT-SPEED-001.value",
            MutationKind.SystemParameter,
            "pointer.speed",
            "MouseSensitivity",
            "10",
            "Value",
            "Set pointer speed to the unscaled middle notch");

        var current = reader.Read(plan, out _);
        var optimal = current == "10";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                optimal ? TweakState.Optimal : TweakState.Suboptimal,
                $"{current ?? "unknown"} of 20",
                "10 of 20 (1:1)",
                optimal ? "Mouse counts pass through unscaled." : "Mouse counts are being scaled before the game sees them."),
            optimal ? [] : [plan],
            null);
    }

    private static ExpertTweakCard PollingIntegrity(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "INPUT-POLL-001",
            "Input",
            "Mouse report delivery integrity",
            "Samples arrival intervals from the app's raw-input message pump.",
            "The observation can reveal obvious batching, but it mixes devices and includes message-pump delay. "
            + "It does not read the mouse's configured/advertised polling rate and cannot grade 2-8 kHz delivery.",
            "Diagnostic only. Report-rate problems are resolved at the device, its port, or its driver.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false,
            false,
            false,
            []);

        var input = context.Input;
        if (input is null || !input.Measured)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Not measured", "Stable delivery at the configured rate",
                    input?.Observation ?? "Run the input measurement with the mouse moving continuously."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                input.Observation,
                "Per-device, event-driven measurement with configured-rate provenance",
                "Descriptive sample only. Do not infer missing USB reports or device capability from this run."),
            [],
            "Decision-grade polling recommendations require per-device raw-input attribution and an event-driven collector.");
    }

    private static string BuildPollingDiagnosis(InputChainReport input)
    {
        var parts = new List<string>();
        if (input.IsRateDegraded)
        {
            parts.Add(
                $"The device reaches {input.NominalHz:0} Hz at best but only sustains {input.MeasuredHz:0} Hz, "
                + $"with roughly {input.MissedReportEstimate} reports missing from the sample.");
        }

        if (input.IsJitterHigh)
        {
            parts.Add(
                $"Interval scatter is high: {input.IntervalStdDevMs:0.###} ms deviation against a "
                + $"{input.MedianIntervalMs:0.###} ms median.");
        }

        parts.Add("Try a different port and controller, remove hubs, and check for a device power-saving policy.");
        return string.Join(" ", parts);
    }

    // ---- Background and security ----------------------------------------------------------

    private static ExpertTweakCard GameDvr(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "GAMEDVR-001",
            "Background",
            "Background game recording",
            "Background recording keeps an encoder attached to the game's presentation path so the last minutes "
            + "of play can be saved retroactively.",
            "The capture path runs continuously whether or not anything is ever saved, consuming GPU encode "
            + "capacity and adding presentation work on every frame.",
            "Retroactive clip capture stops working. Manual recording remains available.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            []);

        var plans = new[]
        {
            new MutationPlan("GAMEDVR-001.config", MutationKind.RegistryValue, GameDvrPath,
                "GameDVR_Enabled", "0", "DWord", "Disable game DVR"),
            new MutationPlan("GAMEDVR-001.capture", MutationKind.RegistryValue, GameDvrAppPath,
                "AppCaptureEnabled", "0", "DWord", "Disable background app capture")
        };

        var pending = plans.Where(plan =>
        {
            var current = reader.Read(plan, out var exists);
            return !exists || current != "0";
        }).ToArray();

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                pending.Length == 0 ? TweakState.Optimal : TweakState.Suboptimal,
                pending.Length == 0 ? "Disabled" : $"{pending.Length} of {plans.Length} recording paths still enabled",
                "Disabled",
                pending.Length == 0
                    ? "Background recording is off."
                    : "A continuous capture path is attached to game presentation."),
            pending,
            null);
    }

    private static ExpertTweakCard GameMode(ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "GAMEMODE-001",
            "Background",
            "Windows Game Mode",
            "Game Mode changes how Windows prioritises the foreground game against background scheduling and "
            + "defers some system activity while it runs.",
            "On a contended system it holds back background work during play. On a clean system its effect is "
            + "frequently nil, which is why it belongs in an A/B rather than in a default recipe.",
            "Results are genuinely system-dependent and can regress a specific setup. Measure both states.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false,
            false,
            false,
            []);

        var plan = new MutationPlan(
            "GAMEMODE-001.value",
            MutationKind.RegistryValue,
            GameBarPath,
            "AutoGameModeEnabled",
            "1",
            "DWord",
            "Enable Windows Game Mode");

        var current = reader.Read(plan, out var exists);
        // Game Mode ships enabled; an absent value means enabled, not disabled.
        var enabled = !exists || current != "0";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Optimal : TweakState.Suboptimal,
                enabled ? "Enabled" : "Disabled",
                "Enabled, then measured both ways",
                enabled ? "Game Mode is on, which is the Windows default." : "Game Mode has been turned off."),
            enabled ? [] : [plan],
            null);
    }

    private static ExpertTweakCard MemoryIntegrity(ITweakStateReader reader)
    {
        _ = reader;
        var definition = new ExpertTweakDefinition(
            "SECURITY-HVCI-001",
            "Security trade-off",
            "Memory integrity (hypervisor-enforced code integrity)",
            "Memory integrity runs kernel-mode driver verification inside a hypervisor-protected context, adding "
            + "overhead to kernel transitions and driver interaction.",
            "The cost concentrates in exactly the CPU-bound, draw-call-heavy, high-refresh case a competitive "
            + "title creates. Independent testing has repeatedly measured this in the mid single digits to low "
            + "double digits of frame rate in CPU-bound games.",
            "This reduces a real kernel security guarantee. It is the one entry in this catalogue that trades "
            + "safety for speed. Requires a restart, and some titles and anti-cheat systems expect it enabled.",
            TweakRisk.SecurityTradeOff,
            TweakScope.Machine,
            EvidenceQuality.Strong,
            true,
            true,
            false,
            [MicrosoftMemoryIntegrity]);

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                "Not queried by this build",
                "Keep Windows security protections enabled",
                "A policy registry value does not prove the effective runtime state. FramePath Lab does not "
                + "offer a security-for-performance trade."),
            [],
            "Excluded: disabling Memory Integrity is outside the product safety boundary.");
    }

    // ---- Memory and platform ---------------------------------------------------------------

    private static ExpertTweakCard MemoryProfile(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "MEMORY-PROFILE-001",
            "Memory",
            "SMBIOS memory-speed consistency",
            "Firmware exposes a configured speed and a maximum-capable speed through SMBIOS. Their relationship "
            + "can flag a configuration worth checking, but does not prove an XMP/EXPO profile exists.",
            "A large mismatch can justify checking firmware and the module specification. The performance effect "
            + "and stable limit are CPU, board, firmware and kit dependent.",
            "Any memory profile above the processor's official specification is an overclock and needs memory, "
            + "CPU and WHEA stability validation.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Weak,
            false,
            false,
            false,
            []);

        var memory = context.Memory;
        if (!memory.Available || memory.RatedSpeedMts == 0 || memory.ConfiguredSpeedMts == 0)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, memory.Describe(), "Running at the rated profile speed",
                    "Firmware did not report both a rated and a configured memory speed."),
                [],
                null);
        }

        var below = memory.IsBelowRatedSpeed;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                below ? TweakState.Suboptimal : TweakState.Optimal,
                $"{memory.ConfiguredSpeedMts} MT/s configured against {memory.RatedSpeedMts} MT/s rated",
                $"{memory.RatedSpeedMts} MT/s",
                below
                    ? $"The modules advertise {memory.RatedSpeedMts} MT/s but are running at "
                      + $"{memory.ConfiguredSpeedMts} MT/s. Check the module and motherboard specifications; "
                      + "SMBIOS alone cannot identify the correct profile or prove it is stable."
                    : "The modules are running at the speed they advertise. Note that some firmware reports "
                      + "both fields identically once a profile is applied, so treat a match as consistent "
                      + "rather than as proof."),
            [],
            below ? "Memory speed is set in firmware and is never written by this application." : null);
    }

    private static ExpertTweakCard MemoryChannels(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "MEMORY-CHANNELS-001",
            "Memory",
            "Memory channel population",
            "Modules must occupy the correct slot pair for the controller to interleave across every available "
            + "channel.",
            "A configuration that populates only one channel halves memory bandwidth regardless of how many "
            + "modules are installed or how fast they are rated.",
            "Diagnostic only. Correcting it means physically moving modules to the slots the board's manual "
            + "specifies.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            []);

        var memory = context.Memory;
        if (!memory.Available || memory.PopulatedChannels == 0)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, memory.Describe(), "All channels populated",
                    "Firmware did not expose a channel layout that this build can identify reliably."),
                [],
                null);
        }

        var slots = string.Join(", ", memory.Modules.Select(module =>
            $"{module.DeviceLocator} {module.SizeMegabytes / 1024} GiB"));
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                memory.IsSingleChannel ? TweakState.Suboptimal : TweakState.Optimal,
                $"{memory.PopulatedChannels} channel(s) — {slots}",
                "At least two populated channels",
                memory.IsSingleChannel
                    ? "Only one channel is populated. Move the modules to the slot pair the board's manual "
                      + "specifies for dual-channel operation."
                    : $"Modules are spread across {memory.PopulatedChannels} channels."),
            [],
            null);
    }

    private static ExpertTweakCard StackedCacheProfile(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "CPU-STACKED-CACHE-001",
            "CPU",
            "Stacked-cache CPU profile",
            "A CPU carrying vertically stacked cache holds several times the last-level cache per core of a "
            + "conventional part. That silicon sits above the cores and lowers the voltage and thermal ceiling "
            + "the boost algorithm is allowed to use.",
            "Such a part wins through cache residency rather than through frequency. That changes which tuning "
            + "levers are worth pulling: the cache already absorbs most memory traffic, the multiplier is "
            + "locked, and forcing every core to a high performance floor spends the limited power budget on "
            + "idle cores instead of leaving it for the ones running the frame.",
            "Informational. The tuning levers for this class of part live in firmware, not in Windows.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            [MicrosoftProcessorPolicy]);

        var cpu = context.Cpu;
        if (!cpu.HasStackedCache)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.NotApplicable,
                    $"{cpu.LargestCachePerCoreMiB:0.#} MiB last-level cache per core",
                    "Not applicable",
                    "This CPU does not carry the stacked-cache signature."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Optimal,
                $"{cpu.Brand} — {cpu.LargestCachePerCoreMiB:0.#} MiB last-level cache per core across "
                + $"{cpu.CoreGroups.Count} group(s)",
                "Tune in firmware, not in Windows",
                "Stacked cache detected. Because every core shares one cache domain here, thread placement has "
                + "nothing to choose between and this catalogue offers no affinity change. The levers that do "
                + "may be firmware power/voltage policy and memory stability. Treat the processor performance "
                + "floor below as an A/B rather than a default, because a raised floor competes with boost "
                + "headroom on a power- and thermally-limited part."),
            [],
            null);
    }

    private static ExpertTweakCard ResizableBar(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-REBAR-001",
            "GPU",
            "Resizable BAR",
            "Without a resizable base address register the host can only address video memory through a small "
            + "aperture, classically 256 MiB, and must move data through it in pieces.",
            "A full-size aperture removes that staging step from asset and buffer updates. Vendors enable it per "
            + "title rather than globally, so a card can support it, have it enabled in firmware, and still not "
            + "use it for a given game.",
            "Requires firmware support and above-4G decoding. The effect is title-dependent and can be neutral "
            + "or slightly negative in some engines.",
            TweakRisk.Low,
            TweakScope.Firmware,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            []);

        var gpu = context.Gpus.FirstOrDefault(device => device.Vendor == "NVIDIA");
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                gpu?.Observation ?? "No supported telemetry",
                "Verify platform capability and the vendor's current per-game profile",
                "BAR1 aperture size does not prove that Resizable BAR is enabled for CS2. NVIDIA enables it "
                + "selectively through tested game profiles because some titles regress."),
            [],
            "No firmware or hidden driver-profile change is offered.");
    }

    private static ExpertTweakCard GpuInterruptMode(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-MSI-001",
            "GPU",
            "Display adapter interrupt mode",
            "Message-signalled interrupts let a device raise an interrupt by writing to memory rather than by "
            + "asserting a shared physical line, which removes the shared-line arbitration from the path.",
            "Line-based interrupts on a display adapter can lengthen deferred procedure calls and show up as "
            + "periodic frame-delivery hitches. Modern display drivers already default to message-signalled "
            + "interrupts, so this check usually confirms rather than corrects.",
            "Interrupt configuration is a driver-level change that takes effect after a restart. An incorrect "
            + "value on a device that does not support the mode can prevent the device from starting.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Weak,
            true,
            true,
            false,
            []);

        if (context.GpuMessageSignalledInterrupts is null || context.GpuInterruptRegistryPath is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.Unknown,
                    context.GpuInterruptObservation,
                    "Message-signalled interrupts",
                    "No explicit value is set, so the driver default applies. FramePath Lab will not write a "
                    + "value into an unset default it cannot verify the device supports."),
                [],
                null);
        }

        if (context.GpuMessageSignalledInterrupts == true)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Optimal, "Message-signalled interrupts enabled",
                    "Message-signalled interrupts", context.GpuInterruptObservation),
                [],
                null);
        }

        var plan = new MutationPlan(
            "GPU-MSI-001.value",
            MutationKind.RegistryValue,
            context.GpuInterruptRegistryPath,
            "MSISupported",
            "1",
            "DWord",
            "Enable message-signalled interrupts for the display adapter");

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Suboptimal,
                "Line-based interrupts explicitly selected",
                "Message-signalled interrupts",
                context.GpuInterruptObservation
                + " This was explicitly turned off, which is unusual on a modern adapter."),
            [plan],
            null);
    }

    private static ExpertTweakCard ForcedPlatformClock(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "TIMER-PLATFORM-001",
            "Timing",
            "Forced platform timer",
            "Boot configuration can force the high-precision event timer to back the performance counter instead "
            + "of letting Windows use the processor's invariant timestamp counter.",
            "Reading the platform timer costs far more than reading the timestamp counter, and engines query it "
            + "thousands of times a second. Forcing it is a change tweak guides recommend and rarely reverse; "
            + "removing it is one of the few reliable wins available on an already-tuned system.",
            "Diagnostic only. The fix is to clear the forced boot option, which this application does not write.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            false,
            true,
            false,
            [MicrosoftTimers]);

        var forced = context.ForcedPlatformClock;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                forced.HasValue
                    ? forced.Value ? TweakState.Suboptimal : TweakState.Optimal
                    : TweakState.Unknown,
                $"Performance counter frequency {context.PerformanceCounterFrequency:N0} Hz",
                "Inspect boot configuration explicitly if timer forcing is suspected",
                forced == true
                    ? "The counter is running at the legacy platform-timer frequency, which means the platform "
                      + "clock has been forced on in boot configuration. Clear it from an elevated prompt with "
                      + "\"bcdedit /deletevalue useplatformclock\" and restart, then re-scan."
                    : forced == false
                        ? "An authoritative boot-state query found no forced platform clock."
                        : "QPC frequency alone cannot prove whether useplatformclock was forced; no timer change is recommended."),
            [],
            forced == true ? "Boot configuration is never written by this application." : null);
    }

    private static ExpertTweakCard SteamTransfer(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "STEAM-TRANSFER-001",
            "Background",
            "Steam transfer in progress",
            "A content transfer saturates disk writes and network receive processing while it runs.",
            "This is one of the most common causes of stutter in an otherwise clean session, and it is entirely "
            + "invisible in a settings audit. On a fast connection the disk and decompression cost is usually "
            + "the larger half of the problem, not the bandwidth.",
            "Diagnostic only. Pause the transfer in Steam before a session.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            []);

        var steam = context.Steam;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                steam.DownloadInProgress ? TweakState.Suboptimal : TweakState.Optimal,
                steam.DownloadInProgress ? string.Join("; ", steam.ActiveDownloads) : "No transfer in progress",
                "No transfer during play",
                steam.Observation),
            [],
            steam.DownloadInProgress ? "Pause the transfer in Steam; this application does not control it." : null);
    }

    // ---- Audio ------------------------------------------------------------------------------

    private static ExpertTweakCard AudioSampleRate(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "AUDIO-FORMAT-001",
            "Audio",
            "Shared-mode audio format",
            "Windows mixes every application to one shared format. When that format does not match the rate the "
            + "game renders at, the audio engine resamples every buffer on the way out.",
            "If the game's source rate differs from the shared format, Windows may resample it. This scanner "
            + "cannot establish CS2's current source rate or prove that a format change improves localisation or latency.",
            "Changing the endpoint format affects every application using that device.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false,
            false,
            false,
            []);

        var endpoint = context.Audio.Default;
        if (!context.Audio.Available || endpoint is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, context.Audio.Observation, "48 kHz shared format",
                    "No active render endpoint reported a readable format."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                $"{endpoint.FriendlyName}: {endpoint.SampleRateHz / 1000d:0.###} kHz, {endpoint.BitsPerSample}-bit, "
                + $"{endpoint.Channels} channel(s)",
                "No automatic recommendation",
                "The endpoint format is reported as context. Insufficient evidence exists here to claim that "
                + "changing it improves CS2 input-to-photon latency or directional accuracy."),
            [],
            null);
    }

    private static ExpertTweakCard AudioSpatialProcessing(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "AUDIO-SPATIAL-001",
            "Audio",
            "Layered spatial audio processing",
            "A modern shooter applies its own head-related transfer function to place a sound around the "
            + "listener. A virtual-surround renderer applies a second one to whatever it receives.",
            "Two spatial models in series do not compound into better localisation. The engine's cue and the "
            + "renderer's cue disagree about phase, and the result is a footstep that is harder to place, not "
            + "easier. For competitive play the engine should be the only thing doing spatialisation.",
            "Virtual surround can be preferable for single-player immersion. This is a competitive-play "
            + "recommendation, not a general one.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            []);

        var endpoint = context.Audio.Default;
        if (!context.Audio.Available || endpoint is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, context.Audio.Observation, "Engine spatialisation only",
                    "No active render endpoint was readable."),
                [],
                null);
        }

        var providers = context.Audio.SpatialProviders;
        var suspect = endpoint.IsMultichannel || providers.Count > 0;

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                suspect ? TweakState.Blocked : TweakState.Optimal,
                endpoint.IsMultichannel
                    ? $"{endpoint.Channels}-channel shared format"
                    + (providers.Count > 0 ? $"; {string.Join(", ", providers)} running" : string.Empty)
                    : providers.Count > 0
                        ? $"Stereo format, but {string.Join(", ", providers)} running"
                        : "Stereo format, no spatial service observed",
                "Engine spatialisation only, stereo endpoint",
                suspect
                    ? "A second spatial or effects layer may be active. Confirm in Windows that spatial sound is "
                      + "set to Off for this device, and that any vendor surround mode is disabled. FramePath Lab "
                      + "cannot read the spatial-sound selection itself, so this needs eyes on the setting."
                    : "The endpoint is stereo and no third-party spatial service was observed, which is the "
                      + "configuration that leaves the engine as the only spatial model."),
            [],
            suspect ? "The spatial-sound mode is verified manually in Windows sound settings." : null);
    }

    private static ExpertTweakCard AudioEffects(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "AUDIO-EFFECTS-001",
            "Audio",
            "Endpoint audio effects",
            "Endpoint effects run in the audio pipeline after the application: loudness equalisation, bass "
            + "management, virtualisation and similar.",
            "Effects can alter level and spectral cues. Their latency and competitive value are device- and "
            + "effect-specific, so process presence or one registry flag cannot establish a performance win.",
            "Disabling effects removes any vendor tuning the headset relies on for its intended tonality.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false,
            false,
            false,
            []);

        var endpoint = context.Audio.Default;
        if (endpoint?.EnhancementsDisabled is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown,
                    endpoint is null ? context.Audio.Observation : $"{endpoint.FriendlyName}: not reported",
                    "Effects disabled",
                    "This endpoint does not record an effects flag; verify it in the sound control panel."),
                [],
                null);
        }

        var disabled = endpoint.EnhancementsDisabled == true;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                $"{endpoint.FriendlyName}: effects {(disabled ? "disabled" : "enabled")}",
                "No automatic recommendation",
                disabled
                    ? "No endpoint effects are applied after the game."
                    : "Endpoint effects may be active. Verify the actual endpoint and evaluate audibility and latency before changing it."),
            [],
            null);
    }

    // ---- Network, panel and limiter ----------------------------------------------------------

    private static ExpertTweakCard NetworkPathStability(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "NET-PATH-001",
            "Network",
            "Local network path stability",
            "Measures round-trip time and its variation to the default gateway, which is the first hop every "
            + "packet crosses.",
            "First-hop loss or variable round-trip time can reveal a local Wi-Fi, cable, router, or contention "
            + "problem. It does not measure CS2 server latency, packet processing, or hit registration.",
            "This measures the local path only. It says nothing about the route to any game server, and a clean "
            + "result here does not rule out a problem further upstream.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            []);

        var path = context.NetworkPath;
        if (!path.Measured)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, path.Observation, "Low jitter, no loss",
                    "The local path was not measured for this scan."),
                [],
                null);
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                path.IsUnstable ? TweakState.Suboptimal : TweakState.Optimal,
                path.Observation,
                "Jitter under 2 ms with no loss",
                path.IsUnstable
                    ? "The first hop is unstable. On a wireless link, move to wired. On a wired link, check the "
                      + "cable and the port, and confirm nothing else on the connection is saturating the uplink."
                    : "The first hop is stable, so any remaining instability is upstream rather than local."),
            [],
            path.IsUnstable ? "The physical link is the user's to change." : null);
    }

    private static ExpertTweakCard PanelNativeTiming(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "DISPLAY-PANEL-001",
            "Display",
            "Panel native timing and refresh range",
            "Windows only enumerates the modes the current link can carry. The panel's own description of itself "
            + "states its preferred timing and vertical rate range independently of how it is connected.",
            "A display running below its native resolution reports its reduced ceiling as though it were the "
            + "panel's ceiling, so a refresh check that compares only within the current resolution will call "
            + "that already-optimal. Reading the panel directly is the second opinion that catches it.",
            "Diagnostic only. Changing resolution or refresh is done in Windows display settings.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Strong,
            false,
            false,
            false,
            []);

        var panel = context.Panel;
        var timing = context.PrimaryTiming;
        if (!panel.Available)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, panel.Observation, "Running at native timing",
                    "No attached display exposed a readable descriptor."),
                [],
                null);
        }

        var belowNative = timing is not null
                          && panel.NativeWidth > 0
                          && (timing.Width < panel.NativeWidth || timing.Height < panel.NativeHeight);
        var belowRange = timing is not null
                         && panel.MaximumVerticalHz > 0
                         && timing.ExactRefreshHz < panel.MaximumVerticalHz - 1.5;

        var suboptimal = belowNative || belowRange;
        var detail = suboptimal
            ? string.Join(" ", new[]
            {
                belowNative
                    ? $"The display is running {timing!.Width}x{timing.Height} against a native "
                      + $"{panel.NativeWidth}x{panel.NativeHeight}."
                    : string.Empty,
                belowRange
                    ? $"The panel states a vertical range up to {panel.MaximumVerticalHz} Hz but is running at "
                      + $"{timing!.ExactRefreshHz:0.###} Hz. Check the cable standard and the port before "
                      + "assuming the mode is unavailable."
                    : string.Empty
            }.Where(part => part.Length > 0))
            : "The active mode matches what the panel describes as its capability.";

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                suboptimal ? TweakState.Suboptimal : TweakState.Optimal,
                panel.Observation,
                panel.NativeWidth > 0
                    ? $"{panel.NativeWidth}x{panel.NativeHeight}"
                      + (panel.MaximumVerticalHz > 0 ? $" at up to {panel.MaximumVerticalHz} Hz" : string.Empty)
                    : "Native timing",
                detail),
            [],
            suboptimal ? "Resolution and refresh are changed in Windows display settings." : null);
    }

    private static ExpertTweakCard FrameLimiterStrategy(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "LIMITER-STRATEGY-001",
            "Presentation",
            "Frame limiter choice",
            "A frame limiter can sit inside the engine, inside the vendor latency path, in the driver, or in an "
            + "external overlay. Each sits at a different point in the frame pipeline.",
            "Limiter placement can change queueing and frame cadence, but the best choice depends on the game's "
            + "latency path, VRR/V-Sync policy, bottleneck and target cap. It must be measured on the target system.",
            "Two limiters active at once interact, and the lower one usually wins in an unpredictable way. Pick "
            + "exactly one.",
            TweakRisk.Low,
            TweakScope.VendorControlPanel,
            EvidenceQuality.Moderate,
            false,
            false,
            true,
            [NvidiaReflex]);

        var timing = context.PrimaryTiming;
        var cap = timing?.RecommendedVrrCap ?? 0;
        var external = context.OverlayProcessObserved;

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                external ? TweakState.Blocked : TweakState.Optimal,
                external
                    ? "An overlay or statistics tool that can impose its own limit is running"
                    : "No external limiter process was observed",
                cap > 0 ? $"One limiter only, at {cap} FPS when using variable refresh" : "One limiter only",
                (cap > 0
                    ? $"With variable refresh and vertical sync engaged, cap at {cap}. With vertical sync off and "
                      + "tearing accepted, run uncapped. "
                    : string.Empty)
                + "Prefer the vendor latency path's own limiter, then the engine's, then the driver's, then an "
                + "external overlay. "
                + (external
                    ? "Because an overlay capable of limiting frames is running, confirm it is not also applying "
                      + "a cap."
                    : "Set exactly one and measure it.")),
            [],
            null);
    }

    private static ExpertTweakCard FastStartup(ExpertScanContext context, ITweakStateReader reader)
    {
        var definition = new ExpertTweakDefinition(
            "BOOT-FASTSTART-001",
            "Windows",
            "Fast startup",
            "Fast startup hibernates the kernel session rather than shutting it down, so powering on resumes the "
            + "previous kernel state and the driver state along with it.",
            "This does not raise steady-state frame rate, and it is not offered as though it does. What it fixes "
            + "is reproducibility: with fast startup on, a shutdown resumes the previous kernel and driver state, "
            + "which is why a fault can survive a shutdown and vanish after a restart. A machine that is tuned "
            + "and then measured has to start from the same state every session or none of the measurements "
            + "compare.",
            "Cold starts take longer. A restart already gives a clean kernel session, so the value here is that "
            + "shutdown does too, without depending on the user remembering which one they used.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            false,
            false,
            []);

        var plan = new MutationPlan(
            "BOOT-FASTSTART-001.value",
            MutationKind.RegistryValue,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            "HiberbootEnabled",
            "0",
            "DWord",
            "Disable fast startup so a shutdown ends the kernel session");

        if (context.FastStartupEnabled is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Not reported", "Disabled",
                    "This system does not record a fast startup setting."),
                [],
                null);
        }

        var enabled = context.FastStartupEnabled == true;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                enabled ? TweakState.Suboptimal : TweakState.Optimal,
                enabled ? "Enabled" : "Disabled",
                "Disabled, for reproducible sessions",
                enabled
                    ? "A shutdown will hibernate the kernel session rather than ending it, so two sessions can "
                      + "start from different driver state."
                    : "A shutdown ends the kernel session, so every boot starts clean."),
            enabled ? [plan] : [],
            null);
    }

    private static ExpertTweakCard InterruptAffinityPolicy(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "GPU-IRQ-AFFINITY-001",
            "GPU",
            "Display adapter interrupt affinity policy",
            "An affinity policy pins a device's interrupt servicing onto chosen processors instead of letting "
            + "the platform place it.",
            "Steering adapter interrupts away from the processors running the game's critical threads can reduce "
            + "the chance of an interrupt landing mid-frame. It is a genuine lever and an equally genuine way to "
            + "make a machine worse, because a policy pointing at processors that no longer exist or that are "
            + "already loaded is harder to diagnose than the problem it was meant to fix.",
            "Reported only. FramePath Lab does not write interrupt affinity policy, because an incorrect value "
            + "can prevent a device from starting and is not reversible from inside Windows if it does.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Weak,
            true,
            true,
            false,
            []);

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                context.HasInterruptAffinityPolicy ? TweakState.Blocked : TweakState.Optimal,
                context.HasInterruptAffinityPolicy ? "A policy is set" : "No policy set (platform default)",
                "Platform default unless measured otherwise",
                context.InterruptAffinityObservation
                + (context.HasInterruptAffinityPolicy
                    ? " A policy set by an earlier tuning attempt is worth re-validating against a capture; "
                      + "it is not self-evidently helping."
                    : " The platform is placing adapter interrupts, which is the right default.")),
            [],
            "Interrupt affinity policy is never written by this application.");
    }

    private static ExpertTweakCard DefenderExclusion(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "SECURITY-EXCLUSION-001",
            "Security trade-off",
            "Real-time scanning exclusions",
            "Real-time protection inspects file activity as it happens, including the reads and shader-cache "
            + "writes a game performs while loading and while compiling.",
            "Excluding a game directory removes that inspection from its file path, which can reduce load-time "
            + "and shader-compilation stalls. It also removes protection from a directory that regularly "
            + "receives downloaded content.",
            "This reduces a real security boundary on a directory that content is downloaded into. FramePath Lab "
            + "reports the configured exclusions and does not add any.",
            TweakRisk.SecurityTradeOff,
            TweakScope.Machine,
            EvidenceQuality.Weak,
            true,
            false,
            false,
            []);

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                context.DefenderExclusionsReadable ? TweakState.Optimal : TweakState.Unknown,
                context.DefenderExclusions.Count > 0
                    ? $"{context.DefenderExclusions.Count} path exclusion(s) configured"
                    : context.DefenderObservation,
                "Your decision, not a recommendation",
                context.DefenderObservation
                + " Any exclusion is a security decision for the account holder, so this card reports and does "
                + "not advise."),
            [],
            "Exclusions are never added by this application.");
    }

    private static IEnumerable<ExpertTweakCard> NvidiaProfileCards(ExpertScanContext context)
    {
        var profile = context.NvidiaProfile;
        var definition = new ExpertTweakDefinition(
            "NVIDIA-PROFILE-001",
            "GPU",
            "Driver profile for the game",
            "The display driver keeps a per-application profile that overrides what the game asks for: "
            + "performance-state policy, the render queue depth, vertical sync, frame limiting and shader cache.",
            "This is the one settings surface no Windows API exposes. A player can have every Windows and "
            + "in-game setting correct and still run against a profile that lets the GPU drop performance states "
            + "between frames or that overrides the in-game latency path.",
            "Reported only. FramePath Lab reads the profile and never saves to it, because a driver profile "
            + "write applies to the game itself rather than to Windows.",
            TweakRisk.Low,
            TweakScope.VendorControlPanel,
            EvidenceQuality.Strong,
            false,
            false,
            true,
            [NvidiaReflex]);

        if (!profile.Available)
        {
            yield return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, profile.Observation, "Read from the driver",
                    "The driver settings interface was not available on this system."),
                [],
                null);
            yield break;
        }

        yield return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Unknown,
                string.Join("; ", profile.Settings.Select(setting => $"{setting.Name}: {setting.Value}")),
                "No universal profile preset",
                profile.Observation + " Values are reported for study only. Reflex-aware games, driver versions, "
                + "VRR policy and workload bottlenecks can change which overrides are appropriate; benchmark one change at a time."),
            [],
            null);
    }

    // ---- Background contention and platform policy -------------------------------------------

    /// <summary>
    /// Builds a card for a single registry value whose correct state is a fixed number.
    /// </summary>
    private static ExpertTweakCard RegistryToggle(
        string id,
        string category,
        string title,
        string mechanism,
        string rationale,
        string tradeoff,
        TweakRisk risk,
        TweakScope scope,
        EvidenceQuality evidence,
        bool requiresElevation,
        bool requiresReboot,
        bool requiresGameRestart,
        string key,
        string valueName,
        string desiredValue,
        string valueType,
        string description,
        Func<string?, bool, bool> isOptimal,
        Func<string?, bool, string> describeCurrent,
        string recommendedLabel,
        string optimalDetail,
        string suboptimalDetail,
        ITweakStateReader reader,
        IReadOnlyList<EvidenceSource>? sources = null)
    {
        var definition = new ExpertTweakDefinition(
            id, category, title, mechanism, rationale, tradeoff,
            risk, scope, evidence, requiresElevation, requiresReboot, requiresGameRestart,
            sources ?? []);

        var plan = new MutationPlan(
            $"{id}.value", MutationKind.RegistryValue, key, valueName, desiredValue, valueType, description);

        var current = reader.Read(plan, out var exists);
        var optimal = isOptimal(current, exists);

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                optimal ? TweakState.Optimal : TweakState.Suboptimal,
                describeCurrent(current, exists),
                recommendedLabel,
                optimal ? optimalDetail : suboptimalDetail),
            optimal ? [] : [plan],
            null);
    }

    private static ExpertTweakCard NetworkThrottling(ITweakStateReader reader)
        => RegistryToggle(
            "NET-THROTTLE-001",
            "Network",
            "Multimedia network throttling",
            "While any process is registered with the multimedia scheduler, the network stack caps how many "
            + "packets per millisecond it will process for everything that is not that multimedia stream. The "
            + "documented default is ten.",
            "A game is exactly the kind of traffic this cap applies to, and ten packets per millisecond is a "
            + "ceiling a busy session can reach. Unlike the reservation value on the same key, this one has "
            + "published semantics and a defined default, so what removing it does is not in question.",
            "Removes a protection intended to keep media playback smooth under heavy network load. On a machine "
            + "that also does bulk transfers while playing, that protection was doing something.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Strong,
            true, true, false,
            MmcssProfilePath,
            "NetworkThrottlingIndex",
            "4294967295",
            "DWord",
            "Disable multimedia network throttling",
            (current, exists) => exists && current is "4294967295" or "-1",
            (current, exists) => exists ? $"{current} packets per millisecond" : "Not set (default: 10 per millisecond)",
            "Disabled",
            "Network throttling is already switched off.",
            "The stack is capping non-multimedia packet processing while a multimedia policy is active.",
            reader,
            [MicrosoftMmcss]);

    private static ExpertTweakCard PowerThrottling(ITweakStateReader reader)
        => RegistryToggle(
            "CPU-POWERTHROTTLE-001",
            "CPU",
            "System-wide power throttling",
            "Windows places processes it judges to be non-critical into a reduced performance state, biasing "
            + "them onto efficiency cores and lower clocks.",
            "This reaches the same outcome as clearing the throttle on the game process, without opening a "
            + "handle to the game to do it. That distinction matters: the per-process route requires rights over "
            + "a protected process, and this one does not touch the game at all.",
            "Disables an energy-saving behaviour for every process, not just the game. On a laptop that is a "
            + "real battery cost; on a desktop it is mostly idle power.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, true, false,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
            "PowerThrottlingOff",
            "1",
            "DWord",
            "Disable system-wide power throttling",
            (current, exists) => exists && current == "1",
            (current, exists) => exists && current == "1" ? "Disabled" : "Enabled (Windows default)",
            "Disabled",
            "No system-wide power throttling is applied.",
            "Windows may place the game into a reduced performance state.",
            reader,
            [MicrosoftEcoQoS]);

    private static ExpertTweakCard DeliveryOptimization(ITweakStateReader reader)
        => RegistryToggle(
            "BACKGROUND-DO-001",
            "Background",
            "Update delivery peer-to-peer sharing",
            "Delivery optimisation uploads update content to other machines on the network and the internet, "
            + "using the connection while it does.",
            "This is upload contention that arrives without warning and has nothing to do with the game. It is "
            + "one of the few background causes that can add jitter to a session on an otherwise idle machine.",
            "Updates download from Microsoft directly rather than from peers, which can be slower on a "
            + "connection where several machines update together.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, false, false,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
            "DODownloadMode",
            "0",
            "DWord",
            "Disable peer-to-peer update sharing",
            (current, exists) => exists && current == "0",
            (current, exists) => exists ? $"Mode {current}" : "Not set (peers on the network and internet)",
            "Disabled (0)",
            "Update content is not shared with peers.",
            "Update content may be uploaded to other machines while you play.",
            reader);

    private static ExpertTweakCard BackgroundApplications(ITweakStateReader reader)
        => RegistryToggle(
            "BACKGROUND-APPS-001",
            "Background",
            "Background application activity",
            "Packaged applications may keep running after they lose focus, holding processor time, memory and "
            + "network.",
            "None of it is doing anything for the player during a match, and the wake-ups arrive on their own "
            + "schedule rather than yours.",
            "Background notifications, live tiles and sync for those applications stop until they are opened.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Moderate,
            false, false, false,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
            "GlobalUserDisabled",
            "1",
            "DWord",
            "Disabled",
            (current, exists) => exists && current == "1",
            (current, exists) => exists && current == "1" ? "Disabled" : "Allowed (Windows default)",
            "Disabled",
            "Packaged applications do not run in the background.",
            "Packaged applications may run and wake while the game is in focus.",
            reader);

    private static ExpertTweakCard PagedKernel(ITweakStateReader reader)
        => RegistryToggle(
            "MEMORY-KERNEL-001",
            "Memory",
            "Kernel and driver paging",
            "By default the kernel and driver code may be paged out of physical memory when it has not been "
            + "used recently.",
            "Paging kernel code out means a page fault at the moment something needs it again, and that fault "
            + "lands inside the frame that needed it. On a machine with memory to spare there is nothing to "
            + "gain by evicting it in the first place.",
            "Holds kernel and driver code resident permanently. On a machine short of memory this makes matters "
            + "worse rather than better.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, true, false,
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            "DisablePagingExecutive",
            "1",
            "DWord",
            "Kernel held resident",
            (current, exists) => exists && current == "1",
            (current, exists) => exists && current == "1" ? "Held resident" : "May be paged out (Windows default)",
            "Held resident",
            "Kernel and driver code stays in physical memory.",
            "Kernel and driver code may be paged out and faulted back in mid-frame.",
            reader);

    private static ExpertTweakCard GameDvrPolicy(ITweakStateReader reader)
        => RegistryToggle(
            "GAMEDVR-POLICY-001",
            "Background",
            "Machine-wide game recording policy",
            "A machine-level policy switches the capture service off for every account, rather than for the "
            + "signed-in user only.",
            "The per-user switch leaves the service able to run; the policy stops it being enabled at all. "
            + "Setting both is what actually removes the capture path rather than hiding its toggle.",
            "Retroactive clip capture stops working for every account on the machine.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, false, true,
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
            "AllowGameDVR",
            "0",
            "DWord",
            "Disabled",
            (current, exists) => exists && current == "0",
            (current, exists) => exists && current == "0" ? "Disabled by policy" : "Not set (allowed)",
            "Disabled by policy",
            "The capture service is disabled machine-wide.",
            "The capture service is permitted machine-wide even if the per-user switch is off.",
            reader);

    private static ExpertTweakCard SystemWideFullscreenBehaviour(ITweakStateReader reader)
        => RegistryToggle(
            "DX-FSE-001",
            "Presentation",
            "System-wide fullscreen behaviour",
            "This value selects the default presentation path for fullscreen titles across the whole account, "
            + "where the compatibility flag does it for one executable.",
            "Setting it once is what makes the behaviour consistent for every title, rather than remembering to "
            + "flag each executable. Which path is faster remains engine-dependent, so this decides what gets "
            + "measured rather than deciding the answer.",
            "Applies to every fullscreen application for this account. Confirm the result in a capture; the "
            + "present mode is the evidence, not the setting.",
            TweakRisk.Moderate,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false, false, true,
            @"HKCU\System\GameConfigStore",
            "GameDVR_FSEBehavior",
            "2",
            "DWord",
            "Optimisations off system-wide",
            (current, exists) => exists && current == "2",
            (current, exists) => exists ? $"Mode {current}" : "Not set (Windows default)",
            "2 (optimisations off), then measured",
            "Fullscreen optimisations are switched off for this account.",
            "Fullscreen titles use the Windows default presentation path.",
            reader);

    private static ExpertTweakCard DesktopTransparency(ITweakStateReader reader)
        => RegistryToggle(
            "VISUAL-DWM-001",
            "Presentation",
            "Desktop transparency effects",
            "Transparency makes the desktop compositor blur and blend behind window surfaces every time it "
            + "composes.",
            "Small, free, and entirely outside the game. It matters most in borderless presentation, where the "
            + "compositor is in the frame path rather than beside it.",
            "The desktop loses its translucency. No effect on anything else.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Weak,
            false, false, false,
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "EnableTransparency",
            "0",
            "DWord",
            "Off",
            (current, exists) => exists && current == "0",
            (current, exists) => exists && current == "0" ? "Off" : "On (Windows default)",
            "Off",
            "Transparency is already off.",
            "The compositor is blending transparency on every composition pass.",
            reader);

    private static ExpertTweakCard TelemetryAutologger(ITweakStateReader reader)
        => RegistryToggle(
            "TELEMETRY-TRACE-001",
            "Background",
            "Diagnostics trace session",
            "An always-on kernel trace session writes diagnostic events to disk continuously, independently of "
            + "whether the telemetry service itself is running.",
            "It is background disk activity that runs whether or not anything ever reads the result. Stopping "
            + "the trace session is what actually ends the writes; disabling the service alone does not.",
            "Diagnostic traces are no longer collected, which removes information Microsoft support would use "
            + "to investigate a fault.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, true, false,
            @"HKLM\SYSTEM\CurrentControlSet\Control\WMI\Autologger\DiagTrack-Listener",
            "Start",
            "0",
            "DWord",
            "Stopped",
            (current, exists) => exists && current == "0",
            (current, exists) => exists && current == "0" ? "Stopped" : "Running",
            "Stopped",
            "The trace session is not started at boot.",
            "A kernel trace session is writing diagnostic events to disk continuously.",
            reader);

    private static ExpertTweakCard NetworkInterruptMode(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "NET-MSI-001",
            "Network",
            "Network adapter interrupt mode",
            "Message-signalled interrupts let the adapter raise an interrupt by writing to memory rather than "
            + "asserting a shared line, removing shared-line arbitration from the receive path.",
            "The receive path is where a server tick becomes a packet the game can read, so interrupt handling "
            + "delay here lands directly on tick arrival. Modern adapters default to message-signalled "
            + "interrupts, so this usually confirms rather than corrects.",
            "Reported only. An incorrect interrupt configuration can leave an adapter unable to start, which is "
            + "considerably worse than the delay it was meant to remove.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Weak,
            true, true, false,
            []);

        var state = context.NetworkMessageSignalledInterrupts;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                state switch
                {
                    true => TweakState.Optimal,
                    false => TweakState.Suboptimal,
                    null => TweakState.Unknown
                },
                state switch
                {
                    true => "Message-signalled interrupts enabled",
                    false => "Line-based interrupts explicitly selected",
                    null => "No explicit value; the driver default applies"
                },
                "Message-signalled interrupts",
                context.NetworkInterruptObservation),
            [],
            "Adapter interrupt configuration is never written by this application.");
    }

    private static ExpertTweakCard BootTiming(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "BOOT-TIMING-001",
            "Timing",
            "Boot timing options",
            "Boot configuration can force the performance counter onto the platform timer instead of the "
            + "processor's own timestamp counter, and can pin the kernel to a fixed tick.",
            "Reading the platform timer costs far more than reading the timestamp counter, and an engine queries "
            + "it thousands of times a second. Forcing it is a change tweak guides recommend and almost never "
            + "reverse, so it is worth checking directly rather than inferring.",
            "Reported only. Boot configuration is never written by this application; the exact command to clear "
            + "a forced value is given instead.",
            TweakRisk.Low,
            TweakScope.Machine,
            EvidenceQuality.Strong,
            true, true, false,
            [MicrosoftTimers]);

        var boot = context.BootTiming;
        if (!boot.Readable)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Not read", "No forced platform timer", boot.Observation),
                [],
                null);
        }

        var forced = boot.HasForcedPlatformTimer;
        var detail = forced
            ? "A forced platform timer is set. Clear it from an elevated prompt with "
              + "\"bcdedit /deletevalue useplatformclock\" (and useplatformtick if present), then restart."
            : "No forced platform timer is set, which is the correct state on a modern platform.";

        var extras = new List<string>();
        if (boot.DisableDynamicTick == true)
        {
            extras.Add("Dynamic tick is disabled, which raises idle timer interrupts; this is a change worth "
                       + "measuring rather than assuming.");
        }

        if (!string.IsNullOrWhiteSpace(boot.TscSyncPolicy))
        {
            extras.Add($"Timestamp counter sync policy is set to {boot.TscSyncPolicy}.");
        }

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                forced ? TweakState.Suboptimal : TweakState.Optimal,
                $"platform clock {Describe(boot.UsePlatformClock)}, platform tick {Describe(boot.UsePlatformTick)}"
                + $", dynamic tick {(boot.DisableDynamicTick == true ? "disabled" : "default")}",
                "No forced platform timer",
                string.Join(" ", new[] { detail }.Concat(extras))),
            [],
            forced ? "Boot configuration is never written by this application." : null);
    }

    private static string Describe(bool? value)
        => value switch { true => "forced on", false => "explicitly off", null => "not set" };

    private static ExpertTweakCard SpeculativeMitigations(ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            "SECURITY-SPECULATIVE-001",
            "Security trade-off",
            "Speculative-execution mitigations",
            "The processor mitigations for speculative-execution vulnerabilities add work to kernel entry and "
            + "exit, and to branch prediction behaviour around it.",
            "The cost concentrates in the same place memory integrity does: syscall-heavy, CPU-bound work at "
            + "high frame rates. It is one of the larger numbers available on this class of processor, and it "
            + "is also one of the larger security guarantees on the machine.",
            "Turning these off re-exposes the processor to the vulnerability class they exist to mitigate. "
            + "FramePath Lab reports the state and does not change it.",
            TweakRisk.SecurityTradeOff,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true, true, false,
            []);

        var overridden = context.SpeculativeMitigationsOverridden;

        // The state label must not read as approval. A machine running without these mitigations is
        // notable and is deliberately not something this application changes, so it reports as
        // blocked rather than as optimal — "optimal" would tell a reader that security being off is
        // the state we wanted.
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                overridden switch
                {
                    true => TweakState.Blocked,
                    false => TweakState.Optimal,
                    null => TweakState.Unknown
                },
                overridden switch
                {
                    true => "Mitigations overridden off",
                    false => "Mitigations active",
                    null => "Not readable"
                },
                "Your decision, not a recommendation",
                context.SpeculativeMitigationObservation
                + (overridden == true
                    ? " This machine is running without those mitigations. That is a defensible choice on a "
                      + "dedicated gaming machine and a poor one on a machine that does anything else."
                    : " FramePath Lab surfaces this because the trade is real in both directions, and does not "
                      + "make it for you.")),
            [],
            "Processor mitigation state is never written by this application.");
    }

    // ---- Excluded, with the reason stated ----------------------------------------------------

    /// <summary>
    /// Things that are widely recommended and do not survive scrutiny.
    ///
    /// For someone whose ranking is their income, the failure mode is not missing a tweak — it is
    /// an endless spiral of applying changes that do nothing and attributing variance to them.
    /// Saying "we checked this and it does not help, here is why" is worth as much as another
    /// setting, because silence sends people to a forum thread instead.
    /// </summary>
    private static IEnumerable<ExpertTweakCard> DebunkRegister()
    {
        yield return Debunk(
            "EXCLUDE-USBSUSPEND-001",
            "USB selective suspend for the mouse",
            "Selective suspend only powers down a device that has gone idle. A mouse being used is never idle, "
            + "so it is never suspended, so disabling the feature changes nothing about its report timing during "
            + "play. The setting has a real effect on devices that genuinely idle; a mouse in a match is not one.");

        yield return Debunk(
            "EXCLUDE-PAGEFILE-001",
            "Disabling the page file",
            "The page file is not a slower substitute for memory that Windows uses once memory runs out. It backs "
            + "allocations that are never resident, and removing it makes some applications fail to allocate "
            + "rather than making anything faster. On a machine with ample memory it is already barely touched.");

        yield return Debunk(
            "EXCLUDE-SUPERFETCH-001",
            "Disabling SysMain and memory compression",
            "Both exist to avoid disk reads. On a system with ample memory and solid-state storage they cost "
            + "little and occasionally save a stall. Disabling them removes a mitigation without removing a cost.");

        yield return Debunk(
            "EXCLUDE-SMT-001",
            "Disabling simultaneous multithreading",
            "Turning it off removes scheduling capacity from a workload that has background threads to place "
            + "somewhere. It was occasionally defensible on old schedulers and specific titles; on a current "
            + "platform it usually costs frame consistency rather than gaining it. Measure it before believing it.");

        yield return Debunk(
            "EXCLUDE-DEBLOAT-001",
            "Service removal and debloat scripts",
            "These bundle dozens of unrelated changes at once, which makes any result impossible to attribute and "
            + "any regression impossible to isolate. They also break servicing, which turns a small future problem "
            + "into a reinstall. Change one thing and measure it.");

        yield return Debunk(
            "EXCLUDE-LAUNCHOPTS-001",
            "Legacy launch options",
            "Thread-count, priority and renderer flags inherited from older engines are either ignored by a modern "
            + "engine or actively worse than its own detection. An inherited launch string is worth removing, not "
            + "extending.");

        yield return Debunk(
            "EXCLUDE-NETTWEAK-001",
            "Nagle and TCP acknowledgement tuning",
            "These change how the transmission-control protocol batches small packets and acknowledgements. "
            + "Competitive shooters do not use that protocol — they send datagrams, which are never batched by "
            + "Nagle and never acknowledged by the stack. The settings are real and they do what they claim; "
            + "they simply do not touch game traffic. Measured jitter on the local hop is the useful number.");

        yield return Debunk(
            "EXCLUDE-TDR-001",
            "Disabling graphics timeout detection",
            "Timeout detection is what lets Windows reset a hung graphics driver and carry on. Disabling it does "
            + "not make a hang less likely; it converts a recoverable two-second stall into a machine that has "
            + "to be power-cycled. This one is commonly listed as a low-risk latency tweak and is neither.");

        yield return Debunk(
            "EXCLUDE-FLIPQUEUE-001",
            "Flip queue size registry value",
            "Widely published as cutting queued frames on NVIDIA hardware. The queue depth is a driver profile "
            + "setting, not a graphics-driver registry value, and this application reads the real one directly "
            + "from the driver. Writing the registry name changes nothing.");

        yield return Debunk(
            "EXCLUDE-PREEMPTION-001",
            "GPU preemption granularity keys",
            "A block of eight or more values usually written under the graphics-driver key. None has published "
            + "semantics, and the community sources that name them place several under different keys entirely, "
            + "so as commonly written they are probably landing nowhere. Unverifiable in both directions, which "
            + "is reason enough not to ship them as a one-click fix.");

        yield return Debunk(
            "EXCLUDE-INPUTQUEUE-001",
            "Mouse and keyboard data queue size",
            "Reducing the class driver's queue is described as cutting input buffering. The queue is headroom "
            + "for reports that have arrived but not yet been read, not a delay applied to them: reports are not "
            + "held until it fills. Shrinking it cannot make delivery earlier, and under load it can drop "
            + "reports that would otherwise have been kept.");

        yield return Debunk(
            "EXCLUDE-IRQ8-001",
            "Real-time clock interrupt priority",
            "An undocumented value with no published effect on any current Windows version, carried forward from "
            + "guides written for operating systems that scheduled interrupts differently.");
    }

    private static ExpertTweakCard Debunk(string id, string title, string reason)
        => new(
            new ExpertTweakDefinition(
                id,
                "Checked and excluded",
                title,
                "Commonly recommended; evaluated and not adopted.",
                reason,
                "Applying it spends time and attention without a measurable return, and makes real changes harder "
                + "to attribute.",
                TweakRisk.Low,
                TweakScope.Machine,
                EvidenceQuality.Disproven,
                false,
                false,
                false,
                []),
            new TweakReading(
                TweakState.NotApplicable,
                "Excluded by evidence",
                "No change",
                reason),
            [],
            null);

    private static IEnumerable<ExpertTweakCard> NetworkTweaks(ExpertScanContext context, ITweakStateReader reader)
    {
        foreach (var adapter in context.NetworkAdapters.Where(entry => entry.IsActiveRoute))
        {
            if (adapter.IsWireless)
            {
                yield return new ExpertTweakCard(
                    new ExpertTweakDefinition(
                        $"NET-WIRELESS-{adapter.Name}",
                        "Network",
                        $"Wireless active route ({adapter.Name})",
                        "Wireless links add variable airtime scheduling between the client and the access point.",
                        "This does not change frame rate. It changes tick delivery consistency, which players "
                        + "reliably experience as input delay and misattribute to rendering.",
                        "Diagnostic only. Use a wired link for competitive play.",
                        TweakRisk.Low,
                        TweakScope.Machine,
                        EvidenceQuality.Strong,
                        false,
                        false,
                        false,
                        []),
                    new TweakReading(TweakState.Suboptimal, adapter.InterfaceDescription, "Wired link",
                        "The active route is wireless."),
                    [],
                    "Changing the physical link is a user action.");
                continue;
            }

            if (adapter.InterruptModeration is { } moderation)
            {
                var plan = new MutationPlan(
                    $"NET-MODERATION-{adapter.Name}.value",
                    MutationKind.RegistryValue,
                    adapter.RegistryKeyPath,
                    "*InterruptModeration",
                    "0",
                    "String",
                    $"Disable interrupt moderation on {adapter.Name}");

                yield return new ExpertTweakCard(
                    new ExpertTweakDefinition(
                        $"NET-MODERATION-{adapter.Name}",
                        "Network",
                        $"Interrupt moderation ({adapter.Name})",
                        "Interrupt moderation batches receive interrupts so the CPU is disturbed less often.",
                        "Batching deliberately delays delivery of the packets that carry server ticks. For a "
                        + "competitive tick rate the CPU saving is worth less than the delay it introduces.",
                        "Raises interrupt load and CPU utilisation slightly. Requires the adapter to reset, which "
                        + "briefly drops the link.",
                        TweakRisk.Moderate,
                        TweakScope.Machine,
                        EvidenceQuality.Moderate,
                        true,
                        false,
                        false,
                        []),
                    new TweakReading(
                        moderation == 0 ? TweakState.Optimal : TweakState.Suboptimal,
                        moderation == 0 ? "Disabled" : "Enabled",
                        "Disabled",
                        adapter.Observation),
                    moderation == 0 ? [] : [plan],
                    null);
            }

            if (adapter.ReceiveCoalescing is { } coalescing)
            {
                var plan = new MutationPlan(
                    $"NET-RSC-{adapter.Name}.value",
                    MutationKind.RegistryValue,
                    adapter.RegistryKeyPath,
                    "*RscIPv4",
                    "0",
                    "String",
                    $"Disable receive segment coalescing on {adapter.Name}");

                yield return new ExpertTweakCard(
                    new ExpertTweakDefinition(
                        $"NET-RSC-{adapter.Name}",
                        "Network",
                        $"Receive segment coalescing ({adapter.Name})",
                        "Receive coalescing merges several arriving segments into one larger unit before handing "
                        + "them up the stack, so the processor is interrupted once instead of repeatedly.",
                        "Merging requires waiting to see whether another segment arrives to merge with. That wait "
                        + "is spent on every batch, and it is spent on the packets carrying server ticks. The "
                        + "processor saving it buys is worth far less than the delay on a connection that is "
                        + "nowhere near saturating a modern adapter.",
                        "Raises interrupt and processor load slightly, and reduces throughput efficiency on bulk "
                        + "transfers. Applying resets the adapter, which briefly drops the link.",
                        TweakRisk.Moderate,
                        TweakScope.Machine,
                        EvidenceQuality.Moderate,
                        true,
                        false,
                        false,
                        []),
                    new TweakReading(
                        coalescing == 0 ? TweakState.Optimal : TweakState.Suboptimal,
                        coalescing == 0 ? "Disabled" : "Enabled",
                        "Disabled",
                        adapter.Observation),
                    coalescing == 0 ? [] : [plan],
                    null);
            }

            if (adapter.EnergyEfficientEthernet is { } energy)
            {
                var plan = new MutationPlan(
                    $"NET-EEE-{adapter.Name}.value",
                    MutationKind.RegistryValue,
                    adapter.RegistryKeyPath,
                    "*EEE",
                    "0",
                    "String",
                    $"Disable energy-efficient Ethernet on {adapter.Name}");

                yield return new ExpertTweakCard(
                    new ExpertTweakDefinition(
                        $"NET-EEE-{adapter.Name}",
                        "Network",
                        $"Energy-efficient Ethernet ({adapter.Name})",
                        "Energy-efficient Ethernet places the link into a low-power idle state between bursts and "
                        + "wakes it when traffic resumes.",
                        "The wake transition adds delay to the first packets after any idle gap, which is a poor "
                        + "trade against a steady stream of server ticks.",
                        "Slightly higher link power draw. Requires an adapter reset.",
                        TweakRisk.Moderate,
                        TweakScope.Machine,
                        EvidenceQuality.Moderate,
                        true,
                        false,
                        false,
                        []),
                    new TweakReading(
                        energy == 0 ? TweakState.Optimal : TweakState.Suboptimal,
                        energy == 0 ? "Disabled" : "Enabled",
                        "Disabled",
                        adapter.Observation),
                    energy == 0 ? [] : [plan],
                    null);
            }
        }
    }
}

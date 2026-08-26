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

    private static readonly EvidenceSource MicrosoftHags = new(
        "Microsoft hardware-accelerated GPU scheduling",
        new Uri("https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/"));

    private static readonly EvidenceSource MicrosoftWddmCaps = new(
        "Microsoft D3DKMT_WDDM_2_7_CAPS",
        new Uri("https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmdt/ns-d3dkmdt-d3dkmt_wddm_2_7_caps"));

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
            MemoryIntegrity(reader)
        };

        cards.AddRange(NetworkTweaks(context, reader));

        return cards
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
            "Affinity applies to the running process only and is lost when the game restarts. Confining threads "
            + "reduces total available cores, which can cost throughput in a CPU-saturated workload.",
            TweakRisk.Moderate,
            TweakScope.RunningProcess,
            EvidenceQuality.Strong,
            false,
            false,
            false,
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

        if (context.GameProcessId is null || cpu.GameAffinityMask is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.Blocked,
                    "Game is not running",
                    recommended,
                    cpu.PreferredGroupReason + " Start the game, then rescan to apply placement."),
                [],
                "The target process must be running before affinity can be captured and changed.");
        }

        var current = cpu.GameAffinityMask.Value;
        if (current == preferredMask)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    TweakState.Optimal,
                    $"0x{current:X}",
                    recommended,
                    "The game is already confined to the preferred core group."),
                [],
                null);
        }

        var plan = new[]
        {
            new MutationPlan(
                "CPU-PLACEMENT-001.affinity",
                MutationKind.ProcessAffinity,
                context.GameExecutableName,
                "ProcessorAffinity",
                preferredMask.ToString(CultureInfo.InvariantCulture),
                "Mask",
                $"Confine {context.GameExecutableName} to core group {cpu.PreferredGroupIndex}")
        };

        return new ExpertTweakCard(
            definition,
            new TweakReading(
                TweakState.Suboptimal,
                $"0x{current:X} ({System.Numerics.BitOperations.PopCount(current)} logical processors)",
                recommended,
                cpu.PreferredGroupReason),
            plan,
            null);
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
        => PowerPolicyCard(
            "CPU-MINSTATE-001",
            "Minimum processor performance state",
            "The processor performance floor governs how far the CPU is allowed to drop between bursts, and how "
            + "far it must ramp back up when a frame arrives.",
            "Ramp-up is not instant. A high floor removes the ramp from the frame path, which shows up in 1% lows "
            + "rather than in average frame rate.",
            "The CPU holds higher clocks at idle, raising power draw, temperature and fan noise. On a "
            + "thermally-limited machine this can reduce sustained boost rather than improve it.",
            MinProcessorState,
            100,
            "100%",
            reader,
            context);

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
        ExpertScanContext context)
    {
        var definition = new ExpertTweakDefinition(
            id,
            "CPU",
            title,
            mechanism,
            rationale,
            tradeoff,
            TweakRisk.Moderate,
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
                matches ? "Already at the recommended value." : "The active scheme is below the recommended value."),
            matches ? [] : [plan],
            null);
    }

    private static ExpertTweakCard ProcessPowerThrottling(ExpertScanContext context, ITweakStateReader reader)
    {
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

        var plan = new MutationPlan(
            "CPU-ECOQOS-001.state",
            MutationKind.ProcessPowerThrottling,
            context.GameExecutableName,
            "ExecutionSpeedThrottling",
            "0",
            "Flag",
            $"Clear efficiency-mode throttling on {context.GameExecutableName}");

        var current = reader.Read(plan, out var exists);
        if (!exists || current is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Blocked, "Game is not running", "Throttling cleared",
                    "Start the game, then rescan."),
                [],
                "The target process must be running.");
        }

        var throttled = current == "1";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                throttled ? TweakState.Suboptimal : TweakState.Optimal,
                throttled ? "Efficiency throttling active" : "Normal quality of service",
                "Throttling cleared",
                throttled
                    ? "The process is running under an explicit efficiency cap."
                    : "No efficiency cap is applied to this process."),
            throttled ? [plan] : [],
            null);
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
            "Windows power mode overlay",
            "Modern Windows layers a power mode on top of the selected power plan. The overlay, not the plan, is "
            + "what drives the energy-performance preference the platform actually acts on.",
            "Switching the plan while the overlay stays on a balanced or efficiency mode frequently changes "
            + "nothing measurable. Setting the overlay is what moves the platform's performance bias.",
            "Raises power draw and temperature across the whole session, not just in game.",
            TweakRisk.Moderate,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            false,
            false,
            false,
            [MicrosoftProcessorPolicy]);

        var plan = new MutationPlan(
            "POWER-OVERLAY-001.overlay",
            MutationKind.PowerOverlayScheme,
            "overlay",
            "EffectiveOverlayScheme",
            BestPerformanceOverlay,
            "Guid",
            "Set the Windows power mode overlay to Best performance");

        var current = reader.Read(plan, out var exists);
        if (!exists || current is null)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(TweakState.Unknown, "Overlay not exposed", "Best performance",
                    "This Windows build did not return an effective power mode overlay."),
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
            "Measures how late a thread actually wakes after requesting a short sleep, which is the symptom a "
            + "driver holding the CPU in a deferred procedure call produces.",
            "This is the unprivileged stand-in for a kernel latency trace. It cannot name the offending driver, "
            + "but a high P99 here is evidence that stutter originates below the game rather than inside it.",
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
                new TweakReading(TweakState.Unknown, "Not measured", "P99 under 1 ms overshoot",
                    "Run the expert scan to sample scheduler punctuality."),
                [],
                null);
        }

        var latency = context.Latency;
        // A P99 overshoot beyond a millisecond means one wake-up in a hundred lands after the next
        // frame boundary at high refresh, which is where felt stutter starts.
        var poor = latency.SchedulerJitterP99Ms > 1.0;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                poor ? TweakState.Suboptimal : TweakState.Optimal,
                $"median {latency.SchedulerJitterMedianMs:0.###} ms, P99 {latency.SchedulerJitterP99Ms:0.###} ms, "
                + $"worst {latency.SchedulerJitterWorstMs:0.###} ms",
                "P99 under 1 ms overshoot",
                poor
                    ? "Wake-ups are landing late enough to miss frame boundaries at high refresh. Investigate "
                      + "driver-level contention rather than game settings."
                    : "Thread wake-ups are punctual on this machine."),
            [],
            poor ? "Identifying the responsible driver requires a kernel trace, which this build does not run." : null);
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
            + "equally do nothing or regress a specific driver and card, which is why the state is read from the "
            + "documented capability query rather than assumed.",
            "Requires a restart. Can help, do nothing, or regress depending on hardware and driver revision.",
            TweakRisk.High,
            TweakScope.Machine,
            EvidenceQuality.Moderate,
            true,
            true,
            false,
            [MicrosoftHags, MicrosoftWddmCaps]);

        var gpu = context.Gpus.FirstOrDefault(device => device.HardwareSchedulingSupported.HasValue);
        if (gpu?.HardwareSchedulingSupported is not true)
        {
            return new ExpertTweakCard(
                definition,
                new TweakReading(
                    gpu is null ? TweakState.Unknown : TweakState.NotApplicable,
                    gpu is null ? "Capability query returned nothing" : "Not supported by this adapter or driver",
                    "Enabled",
                    "The WDDM capability query is the authority here; no registry value is inferred."),
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
            "Measures the interval between mouse reports as this PC delivers them, including USB scheduling and "
            + "driver batching.",
            "A device set to a high report rate that does not sustain it, or that delivers it with heavy interval "
            + "scatter, produces inconsistent aim while every frame-time metric stays clean. Frame capture cannot "
            + "see this because it happens before the engine samples input.",
            "Diagnostic only. Report-rate problems are resolved at the device, its port, or its driver.",
            TweakRisk.Low,
            TweakScope.CurrentUser,
            EvidenceQuality.Moderate,
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

        var degraded = input.IsRateDegraded || input.IsJitterHigh;
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                degraded ? TweakState.Suboptimal : TweakState.Optimal,
                input.Observation,
                $"Sustained {input.NominalHz:0} Hz with low interval scatter",
                degraded
                    ? BuildPollingDiagnosis(input)
                    : "Report delivery is sustained and evenly spaced."),
            [],
            degraded ? "Resolve this at the device, port or driver; no Windows setting corrects it." : null);
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

        var plan = new MutationPlan(
            "SECURITY-HVCI-001.value",
            MutationKind.RegistryValue,
            HvciPath,
            "Enabled",
            "0",
            "DWord",
            "Disable memory integrity at the next restart");

        var current = reader.Read(plan, out var exists);
        var active = !exists || current != "0";
        return new ExpertTweakCard(
            definition,
            new TweakReading(
                active ? TweakState.Suboptimal : TweakState.Optimal,
                active ? "Enabled" : "Disabled",
                "Your decision, not a recommendation",
                active
                    ? "Memory integrity is active and costing frame rate in CPU-bound scenes. FramePath Lab does "
                      + "not recommend disabling it; it surfaces the trade-off and will reverse the change on request."
                    : "Memory integrity is currently off. Re-enabling it restores kernel driver verification."),
            active ? [plan] : [],
            null);
    }

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

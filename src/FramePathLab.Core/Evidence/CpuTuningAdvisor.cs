using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

/// <summary>
/// Builds the firmware-level tuning picture for the processor in this machine.
///
/// None of this is written. It lives in firmware, and on a cache-stacked part most of it is not
/// reachable from an operating system at any privilege level. What the application can usefully do
/// is say which controls this specific part actually exposes, what each one is for, and — the part
/// almost every guide gets wrong — how to tell whether the result is stable.
/// </summary>
public static class CpuTuningAdvisor
{
    public static CpuTuningState Build(CpuTopology cpu, HardwareErrorSummary errors, long uptimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(errors);

        var stacked = cpu.HasStackedCache;
        var amd = cpu.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                  || cpu.Brand.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);

        return new CpuTuningState(
            cpu.Brand,
            stacked,
            MultiplierLocked: stacked,
            uptimeSeconds,
            errors,
            amd ? BuildAmdControls(stacked) : BuildGenericControls(),
            BuildStabilityPlan(),
            BuildObservation(cpu, errors, uptimeSeconds, stacked, amd));
    }

    private static string BuildObservation(
        CpuTopology cpu,
        HardwareErrorSummary errors,
        long uptimeSeconds,
        bool stacked,
        bool amd)
    {
        var uptime = TimeSpan.FromSeconds(uptimeSeconds);
        var parts = new List<string> { cpu.Brand };

        if (stacked)
        {
            parts.Add(
                $"stacked cache ({cpu.LargestCachePerCoreMiB:0.#} MiB per core), so the multiplier and most "
                + "boost controls are locked and the voltage curve is the tuning lever that remains");
        }
        else if (amd)
        {
            parts.Add("conventional cache layout; boost and curve controls are both available");
        }

        parts.Add(uptime.TotalHours >= 1
            ? $"up {uptime.TotalHours:0.#} hours"
            : $"up {uptime.TotalMinutes:0} minutes");

        parts.Add(errors.Readable
            ? errors.HasUncorrectedErrors
                ? $"{errors.MachineCheckExceptions} uncorrected machine check(s) logged — treat any voltage "
                  + "offset as the first suspect"
                : errors.HasCorrectedErrors
                    ? $"{errors.CorrectedErrors} corrected hardware error(s) logged — margin is being consumed"
                    : "no hardware errors logged"
            : "hardware error history unavailable");

        return string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// The controls an AMD platform exposes, and which of them survive on a cache-stacked part.
    /// </summary>
    private static IReadOnlyList<PlatformTuningControl> BuildAmdControls(bool stacked)
    {
        var controls = new List<PlatformTuningControl>
        {
            new(
                "Curve Optimizer",
                "Firmware, or a runtime tuner where firmware omits it",
                stacked
                    ? "Start at −10 all-core, step by −5, stop at −30. Validate every step before the next."
                    : "Start at −10 all-core, then move to per-core once an all-core value is proven.",
                "The curve shifts voltage down at every point on the frequency ladder. It is the only way to "
                + "raise sustained clocks on a part whose multiplier is locked, because the processor spends "
                + "the voltage it saves on boosting further. The offset is not free headroom — it is margin "
                + "being removed, which is why the validation below matters more than the number.",
                TweakRisk.High,
                true),

            new(
                "Package power limit",
                "Firmware, processor common options",
                stacked
                    ? "Functional across roughly 35 W to 142 W. Lowering it cuts temperature with little effect "
                      + "on single-core boost, because single-core work never approaches the package limit."
                    : "Raise only with cooling that can absorb it; verify against sustained clocks, not peaks.",
                "This bounds sustained all-core draw. It is the one power control that generally remains "
                + "available on a cache-stacked part, and lowering it is a thermal lever rather than a "
                + "performance cost for a workload that is mostly a few threads.",
                TweakRisk.Moderate,
                true),

            new(
                "Current limits",
                "Firmware, processor common options",
                "Board-dependent on a cache-stacked part. Change only if firmware genuinely applies them.",
                "These bound sustained and peak current. Where they are locked, writing a value silently does "
                + "nothing, so confirm the applied value in monitoring rather than trusting the setup screen.",
                TweakRisk.Moderate,
                !stacked),

            new(
                "Boost clock override and scalar",
                "Firmware, precision boost overdrive",
                stacked
                    ? "Not available on this part. Any value entered is ignored."
                    : "Small overrides only, and validate at the single-core boost region.",
                "These raise the ceiling the boost algorithm may target. A cache-stacked part has them locked "
                + "because the stacked silicon constrains the voltage it can safely be given.",
                TweakRisk.High,
                !stacked),

            new(
                "Power supply idle control",
                "Firmware, processor common options",
                "Set to typical current idle if the machine ever reboots or throws machine checks while idle.",
                "This governs how deep a low-current state the processor may request when nothing is running. "
                + "The deepest state is the single most common cause of Ryzen instability that appears only at "
                + "idle — which is exactly the failure a voltage offset also produces, so the two get confused "
                + "constantly. Rule this out before blaming the curve.",
                TweakRisk.Low,
                true),

            new(
                "Global C-state control",
                "Firmware, processor common options",
                "Leave enabled. Disable only as a diagnostic when chasing idle instability.",
                "Disabling it holds the processor out of low-power states entirely. That masks idle instability "
                + "rather than fixing it, and costs boost headroom, because the budget the idle cores were "
                + "giving back is what the active core was boosting into.",
                TweakRisk.Moderate,
                true),

            new(
                "Data fabric C-states",
                "Firmware, processor common options",
                "Disable when chasing latency; re-enable if idle power matters more.",
                "These let the fabric connecting the cores to memory drop into a low-power state. Waking it adds "
                + "delay to the first memory access after an idle gap, which is a real cost on a workload that "
                + "alternates between light and heavy frames.",
                TweakRisk.Low,
                true),

            new(
                "Preferred cores",
                "Firmware, processor common options",
                "Leave enabled so the scheduler knows which cores boost highest.",
                "The processor reports a ranking of its own cores. With it enabled the scheduler places "
                + "latency-sensitive work on the best ones. It also means those cores boost highest, so they "
                + "tolerate the least negative curve offset — which is why per-core offsets end up uneven.",
                TweakRisk.Low,
                true),

            new(
                "Memory profile and fabric ratio",
                "Firmware, memory options",
                "Enable the rated profile, then confirm the fabric clock runs synchronously with memory.",
                "A profile sets frequency and primary timings and leaves the rest on automatic. The fabric ratio "
                + "matters more than the frequency: falling to a divided ratio costs more latency than the "
                + "extra speed returns, and it happens silently.",
                TweakRisk.Moderate,
                true),

            new(
                "Secondary memory timings",
                "Firmware, memory options",
                "Tune the refresh interval first; it carries most of the remaining gain.",
                "A rated profile leaves secondary and tertiary timings loose. On a cache-sensitive part the "
                + "refresh interval is the one worth attention, because it governs how often the controller "
                + "stalls to refresh rather than serve a request.",
                TweakRisk.High,
                true)
        };

        return controls;
    }

    private static IReadOnlyList<PlatformTuningControl> BuildGenericControls()
        =>
        [
            new(
                "Processor voltage and frequency",
                "Firmware",
                "Follow the vendor's documented controls for this platform.",
                "This build models the controls of one processor family in detail and does not guess at another "
                + "vendor's names for them.",
                TweakRisk.High,
                false)
        ];

    /// <summary>
    /// The validation sequence, ordered so each step covers what the previous one structurally
    /// cannot. The reason this is a sequence rather than a single test is the point most guides
    /// miss: passing an all-core stress run says almost nothing about a voltage offset.
    /// </summary>
    private static IReadOnlyList<StabilityStep> BuildStabilityPlan()
        =>
        [
            new(
                1,
                StabilityRegion.SingleCoreBoost,
                "Cycle a single-core load across every core",
                "A core-cycling harness driving one worker thread per physical core",
                "At least one full pass per core; overnight for a value you intend to keep",
                "The region a curve offset actually breaks: maximum single-core boost, where the clock is "
                + "highest and the voltage for that clock is lowest. Reports which core failed, so the offset "
                + "can be relaxed on that core alone rather than across the whole part.",
                "Idle and low-power state transitions. A configuration can pass every core here and still "
                + "reboot sitting at the desktop."),

            new(
                2,
                StabilityRegion.IdleAndTransient,
                "Leave the machine idle and watch for machine checks",
                "Uptime, plus the hardware error count on this page",
                "Several hours minimum, and across at least one sleep or wake cycle",
                "The failure nothing can provoke on demand. At idle the processor makes brief opportunistic "
                + "boosts to its highest clocks at the lowest sustained voltage, and enters and leaves "
                + "low-power states constantly. This is where an offset that passed every load test fails.",
                "Nothing further, but it is slow and it is the step people skip. If this fails, rule out the "
                + "idle power state setting before assuming the curve is at fault — they produce the same "
                + "symptom and are constantly mistaken for one another."),

            new(
                3,
                StabilityRegion.AllCoreLoad,
                "Sustained all-core load",
                "Any conventional all-core stress test",
                "Thirty minutes, mainly to confirm cooling and power delivery",
                "Thermal and power-delivery limits, and a genuinely broken memory configuration.",
                "Almost everything a curve offset does. Loading every core drops boost clocks and raises "
                + "voltage per clock, so this exercises the safest part of the curve. Passing it is not "
                + "evidence the offset is stable, and treating it as such is the single most common mistake "
                + "in undervolt validation."),

            new(
                4,
                StabilityRegion.SingleCoreBoost,
                "Play the game you actually play",
                "The title itself, plus the frame-delivery capture in this application",
                "A full session",
                "Whether any of it produced a measurable improvement, and whether the machine holds up under "
                + "the mixed light-and-heavy load a real workload creates rather than a synthetic one.",
                "Nothing, but it proves benefit rather than stability. Capture before and after and compare "
                + "the frame-time tails; a curve offset that raises average frame rate while widening the "
                + "tails is not worth keeping.")
        ];
}

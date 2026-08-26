namespace FramePathLab.Core.Models;

/// <summary>
/// Which part of the frequency/voltage curve a stress test actually exercises.
///
/// This distinction is the whole problem with validating an undervolt. A curve offset lowers
/// voltage at every point, but the margin it eats is not evenly distributed: the danger is at the
/// top of the boost range, where the silicon is fastest and the voltage is lowest for that speed.
/// A test that pins every core cannot reach those clocks, so it exercises the safest region and
/// reports success.
/// </summary>
public enum StabilityRegion
{
    /// <summary>
    /// Every core loaded. Boost clocks fall, voltage per clock is generous. This is the region
    /// almost every stress guide tests and almost the only one that does not matter here.
    /// </summary>
    AllCoreLoad,

    /// <summary>
    /// One core loaded at a time, cycled across all of them. Reaches maximum single-core boost at
    /// the lowest voltage for that clock, which is where a negative offset actually bites.
    /// </summary>
    SingleCoreBoost,

    /// <summary>
    /// Idle and light transient load. Covers low-power state entry and exit and the brief
    /// opportunistic boosts a desktop makes constantly. Nothing stresses this on demand; it is
    /// caught by uptime and by watching for machine-check events.
    /// </summary>
    IdleAndTransient
}

/// <summary>A hardware error the platform reported, which is the ground truth for instability.</summary>
public sealed record HardwareErrorEvent(
    DateTimeOffset TimestampUtc,
    int EventId,
    string Source,
    string Summary);

/// <summary>
/// Machine-check and corrected-error activity over a window.
///
/// This is the only objective stability signal available without rebooting into a test suite. An
/// undervolt that is a step too far usually announces itself here long before it produces a visible
/// crash, and a machine that logs none of these over a week of real use has passed a test no
/// synthetic load can substitute for.
/// </summary>
public sealed record HardwareErrorSummary(
    bool Readable,
    int TotalEvents,
    int MachineCheckExceptions,
    int CorrectedErrors,
    DateTimeOffset? MostRecentUtc,
    TimeSpan Window,
    IReadOnlyList<HardwareErrorEvent> Recent,
    string Observation)
{
    public static HardwareErrorSummary Unreadable(string reason)
        => new(false, 0, 0, 0, null, TimeSpan.Zero, [], reason);

    /// <summary>
    /// An uncorrected machine check is a hard instability signal. On a machine running a voltage
    /// offset it is the offset until proven otherwise.
    /// </summary>
    public bool HasUncorrectedErrors => Readable && MachineCheckExceptions > 0;

    /// <summary>
    /// Corrected errors are survivable by definition, but a rising count on a tuned machine means
    /// the margin is being consumed rather than that nothing is wrong.
    /// </summary>
    public bool HasCorrectedErrors => Readable && CorrectedErrors > 0;
}

/// <summary>
/// One firmware-level tuning control, what it does, and whether this platform exposes it.
///
/// These are reported rather than written: they live in firmware, and the ones that matter on a
/// cache-stacked part are mostly not reachable from an operating system at all.
/// </summary>
public sealed record PlatformTuningControl(
    string Name,
    string Location,
    string Recommendation,
    string Reasoning,
    TweakRisk Risk,
    bool AvailableOnThisPart);

/// <summary>Everything the CPU and platform view needs, assembled once per scan.</summary>
public sealed record CpuTuningState(
    string ProcessorBrand,
    bool HasStackedCache,
    bool MultiplierLocked,
    long SystemUptimeSeconds,
    HardwareErrorSummary HardwareErrors,
    IReadOnlyList<PlatformTuningControl> Controls,
    IReadOnlyList<StabilityStep> StabilityPlan,
    string Observation);

/// <summary>One step of a validation sequence, in the order it has to be done.</summary>
public sealed record StabilityStep(
    int Order,
    StabilityRegion Region,
    string Name,
    string Tool,
    string Duration,
    string WhatItCatches,
    string WhatItMisses);

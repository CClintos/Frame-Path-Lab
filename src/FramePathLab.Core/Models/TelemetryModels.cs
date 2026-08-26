namespace FramePathLab.Core.Models;

/// <summary>One physical core group sharing a last-level cache. On AMD this is a CCD/CCX.</summary>
public sealed record CoreGroup(
    int GroupIndex,
    ulong AffinityMask,
    int PhysicalCoreCount,
    int LogicalProcessorCount,
    ulong LastLevelCacheBytes,
    int EfficiencyClass)
{
    /// <summary>
    /// AMD stacks additional L3 on one CCD only. A group whose LLC is materially larger than its
    /// sibling is the vertical-cache die, which is where a cache-sensitive game belongs.
    /// </summary>
    public double LastLevelCacheMiB => LastLevelCacheBytes / 1024d / 1024d;
}

public sealed record CpuTopology(
    string Vendor,
    string Brand,
    int PhysicalCoreCount,
    int LogicalProcessorCount,
    bool SimultaneousMultithreading,
    bool IsHybrid,
    IReadOnlyList<CoreGroup> CoreGroups,
    int? PreferredGroupIndex,
    string PreferredGroupReason,
    ulong PreferredAffinityMask,
    ulong SystemAffinityMask,
    ulong? GameAffinityMask,
    int? MaxMhz,
    int? CurrentMhz,
    int? MhzLimit,
    IReadOnlyList<string> Notes)
{
    public bool HasDistinctPreferredGroup
        => PreferredGroupIndex.HasValue && CoreGroups.Count > 1 && PreferredAffinityMask != 0;

    /// <summary>True when the CPU is being held below its rated maximum by policy or thermals.</summary>
    public bool IsClockLimited
        => MaxMhz is > 0 && MhzLimit is > 0 && MhzLimit < MaxMhz;
}

public sealed record GpuTelemetry(
    string Name,
    string Vendor,
    bool TelemetryAvailable,
    string TelemetrySource,
    int? PcieLinkWidth,
    int? PcieMaxLinkWidth,
    int? PcieLinkGeneration,
    int? PcieMaxLinkGeneration,
    string? PerformanceState,
    IReadOnlyList<string> ThrottleReasons,
    bool? ResizableBarActive,
    bool? HardwareSchedulingSupported,
    bool? HardwareSchedulingEnabled,
    string Observation)
{
    public bool IsPcieDegraded
        => PcieLinkWidth is > 0 && PcieMaxLinkWidth is > 0 && PcieLinkWidth < PcieMaxLinkWidth;

    public bool HasNonPowerThrottle
        => ThrottleReasons.Any(reason =>
            !reason.Contains("Idle", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("GpuIdle", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Exact display timing from QueryDisplayConfig. Unlike EnumDisplaySettings this carries the true
/// rational refresh rate, so 59.94 Hz never has to be guessed at from a truncated integer.
/// </summary>
public sealed record DisplayTiming(
    string DeviceName,
    string MonitorFriendlyName,
    uint VerticalNumerator,
    uint VerticalDenominator,
    int Width,
    int Height,
    bool IsPrimary,
    bool AdvancedColorSupported,
    bool AdvancedColorEnabled)
{
    public double ExactRefreshHz
        => VerticalDenominator == 0 ? 0 : VerticalNumerator / (double)VerticalDenominator;

    /// <summary>
    /// The cap that keeps a G-SYNC/FreeSync + V-Sync + Reflex configuration inside the variable
    /// window instead of hitting the refresh ceiling and re-introducing queued V-Sync latency.
    /// </summary>
    public int RecommendedVrrCap
        => ExactRefreshHz <= 0 ? 0 : (int)Math.Floor(ExactRefreshHz) - (ExactRefreshHz >= 240 ? 3 : 2);
}

public sealed record InputReportSample(double IntervalMilliseconds);

public sealed record InputChainReport(
    bool Measured,
    int SampleCount,
    double MeasuredHz,
    double NominalHz,
    double MedianIntervalMs,
    double IntervalStdDevMs,
    double P99IntervalMs,
    double WorstIntervalMs,
    int MissedReportEstimate,
    bool PointerAccelerationEnabled,
    int PointerSpeed,
    string DeviceName,
    string Observation)
{
    /// <summary>
    /// A mouse that advertises a rate but delivers a materially lower one is the single most common
    /// cause of aim that feels inconsistent while every frame metric looks clean.
    /// </summary>
    public bool IsRateDegraded
        => Measured && NominalHz > 0 && MeasuredHz < NominalHz * 0.85;

    public bool IsJitterHigh
        => Measured && MedianIntervalMs > 0 && IntervalStdDevMs > MedianIntervalMs * 0.5;
}

public sealed record SystemLatencyReport(
    double CurrentTimerResolutionMs,
    double MinimumTimerResolutionMs,
    double MaximumTimerResolutionMs,
    bool GlobalTimerRequestsHonored,
    double SchedulerJitterMedianMs,
    double SchedulerJitterP99Ms,
    double SchedulerJitterWorstMs,
    int JitterSampleCount,
    string Observation)
{
    /// <summary>
    /// Windows 11 22H2 made timer-resolution requests per-process. A game asking for 0.5 ms no
    /// longer necessarily gets a 0.5 ms system tick, which shows up as scheduling jitter.
    /// </summary>
    public bool IsCoarseTimer => CurrentTimerResolutionMs > 1.2;
}

public sealed record NetworkAdapterState(
    string Name,
    string InterfaceDescription,
    string RegistryKeyPath,
    bool IsWireless,
    bool IsActiveRoute,
    int? InterruptModeration,
    int? EnergyEfficientEthernet,
    int? FlowControl,
    string Observation);

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

    /// <summary>
    /// A die carrying vertically stacked cache holds several times the last-level cache per core of
    /// an ordinary part. That extra silicon sits over the cores and constrains the voltage and
    /// thermal ceiling, so such a part trades peak frequency for cache and reacts differently to
    /// power-policy floors than a conventional CPU does.
    /// </summary>
    public bool HasStackedCache
        => CoreGroups.Any(group =>
            group.PhysicalCoreCount > 0
            && group.LastLevelCacheBytes / (ulong)group.PhysicalCoreCount >= 8UL * 1024 * 1024);

    public double LargestCachePerCoreMiB
        => CoreGroups.Count == 0
            ? 0
            : CoreGroups.Max(group => group.PhysicalCoreCount > 0
                ? group.LastLevelCacheBytes / (double)group.PhysicalCoreCount / 1024 / 1024
                : 0);
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

public sealed record MemoryModule(
    string DeviceLocator,
    string BankLocator,
    string PartNumber,
    string Manufacturer,
    long SizeMegabytes,
    int RatedSpeedMts,
    int ConfiguredSpeedMts);

public sealed record MemoryConfiguration(
    bool Available,
    IReadOnlyList<MemoryModule> Modules,
    long TotalMegabytes,
    int ConfiguredSpeedMts,
    int RatedSpeedMts,
    int PopulatedChannels,
    string UnavailableReason)
{
    public static MemoryConfiguration Unavailable(string reason)
        => new(false, [], 0, 0, 0, 0, reason);

    /// <summary>
    /// True when the modules are running below the speed they advertise, which is what a kit looks
    /// like when its rated profile was never enabled in firmware.
    /// </summary>
    public bool IsBelowRatedSpeed
        => Available && RatedSpeedMts > 0 && ConfiguredSpeedMts > 0
           && ConfiguredSpeedMts < RatedSpeedMts - 40;

    /// <summary>A single populated channel halves available bandwidth regardless of module count.</summary>
    public bool IsSingleChannel => Available && Modules.Count > 0 && PopulatedChannels <= 1;

    public string Describe()
        => !Available
            ? UnavailableReason
            : $"{Modules.Count} module(s), {TotalMegabytes / 1024} GiB across {PopulatedChannels} channel(s), "
              + $"running {ConfiguredSpeedMts} MT/s of {RatedSpeedMts} MT/s rated";
}

/// <summary>Whether Steam is currently moving bytes, which is a common cause of in-game stutter.</summary>
public sealed record SteamActivity(
    bool DownloadInProgress,
    IReadOnlyList<string> ActiveDownloads,
    string Observation);

/// <summary>
/// The shared-mode format Windows mixes to for one render endpoint, plus the processing that sits
/// between the engine and the transducer.
/// </summary>
public sealed record AudioEndpointState(
    string FriendlyName,
    bool IsDefault,
    int SampleRateHz,
    int BitsPerSample,
    int Channels,
    bool? EnhancementsDisabled,
    bool? ExclusiveModeAllowed,
    string Observation)
{
    /// <summary>
    /// Game engines author and mix at 48 kHz. A shared-mode format at any other rate forces the
    /// audio engine to resample every buffer, which adds processing and can smear transient
    /// detail — the exact cue a footstep is localised from.
    /// </summary>
    public bool IsResampling => SampleRateHz > 0 && SampleRateHz != 48000;

    /// <summary>
    /// More than two channels on a headset endpoint means a virtual-surround renderer is folding a
    /// multichannel bed down to two ears, which is a second spatial model layered on the engine's.
    /// </summary>
    public bool IsMultichannel => Channels > 2;
}

public sealed record AudioState(
    bool Available,
    IReadOnlyList<AudioEndpointState> Endpoints,
    IReadOnlyList<string> SpatialProviders,
    string Observation)
{
    public AudioEndpointState? Default => Endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)
                                          ?? Endpoints.FirstOrDefault();
}

/// <summary>Measured quality of the first network hops, not of the game route.</summary>
public sealed record NetworkPathQuality(
    bool Measured,
    string Target,
    int Sent,
    int Received,
    double MedianRttMs,
    double JitterMs,
    double P99RttMs,
    double WorstRttMs,
    string Observation)
{
    public double LossPercent => Sent == 0 ? 0 : 100d * (Sent - Received) / Sent;

    /// <summary>
    /// Jitter on the local hop is what a wireless link, a failing cable or a congested uplink looks
    /// like. It moves tick delivery, which players experience as inconsistent registration.
    /// </summary>
    public bool IsUnstable => Measured && (JitterMs > 2.0 || LossPercent > 0.5);
}

/// <summary>Panel capability read from EDID, which is the panel's own description of itself.</summary>
public sealed record PanelIdentity(
    bool Available,
    string ManufacturerCode,
    string ProductName,
    int NativeWidth,
    int NativeHeight,
    int MinimumVerticalHz,
    int MaximumVerticalHz,
    string Observation);

public sealed record NvidiaProfileSetting(string Name, string Value, string Recommended, bool IsOptimal);

public sealed record NvidiaProfileState(
    bool Available,
    string ProfileName,
    IReadOnlyList<NvidiaProfileSetting> Settings,
    string Observation);

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

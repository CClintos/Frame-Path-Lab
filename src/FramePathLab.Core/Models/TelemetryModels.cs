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
    bool LegacyGlobalTimerPolicyPresent,
    double SchedulerJitterMedianMs,
    double SchedulerJitterP99Ms,
    double SchedulerJitterWorstMs,
    int JitterSampleCount,
    string Observation)
{
    /// <summary>
    /// Describes the queried system timer period only; it is not a performance grade.
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
    /// True when configured speed is below the maximum speed reported through SMBIOS. This is a
    /// consistency flag, not proof that an XMP/EXPO profile exists or is stable.
    /// </summary>
    public bool IsBelowRatedSpeed
        => Available && RatedSpeedMts > 0 && ConfiguredSpeedMts > 0
           && ConfiguredSpeedMts < RatedSpeedMts - 40;

    /// <summary>Only a positively parsed single channel is classified; zero means unknown.</summary>
    public bool IsSingleChannel => Available && Modules.Count > 0 && PopulatedChannels == 1;

    public string Describe()
        => !Available
            ? UnavailableReason
            : $"{Modules.Count} module(s), {TotalMegabytes / 1024} GiB across "
              + (PopulatedChannels > 0 ? $"{PopulatedChannels} inferred channel(s), " : "an unknown channel layout, ")
              + $"running {ConfiguredSpeedMts} MT/s; SMBIOS maximum {RatedSpeedMts} MT/s";
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

/// <summary>
/// Boot options that affect timing. Reading these needs elevation, so <see cref="Readable"/>
/// distinguishes "nothing is set" from "we could not look" — claims that are easy to conflate and
/// mean opposite things.
/// </summary>
public sealed record BootTimingState(
    bool Readable,
    bool? UsePlatformClock,
    bool? UsePlatformTick,
    bool? DisableDynamicTick,
    string? TscSyncPolicy,
    string Observation,
    string? HypervisorLaunchType = null)
{
    public static BootTimingState Unreadable(string reason)
        => new(false, null, null, null, null, reason);

    /// <summary>
    /// When the hypervisor launches at boot, Windows itself runs as a guest on top of it. That is
    /// a cost paid by everything on the machine, and it is separate from — and larger than — the
    /// memory-integrity feature people usually go looking for.
    /// </summary>
    public bool HypervisorActive
        => HypervisorLaunchType is not null
           && !HypervisorLaunchType.Equals("Off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Either forced option routes the performance counter onto the platform timer, which costs
    /// far more per read than the processor's own timestamp counter.
    /// </summary>
    public bool HasForcedPlatformTimer
        => UsePlatformClock == true || UsePlatformTick == true;
}

/// <summary>What Windows actually bound to one device, as opposed to what is available for it.</summary>
public sealed record InstalledDriver(
    string DeviceName,
    string DeviceClass,
    string Provider,
    string Version,
    string DriverDate,
    string InfName,
    string Service)
{
    /// <summary>
    /// Whether this is the driver Windows ships rather than one the hardware vendor supplies.
    ///
    /// Microsoft-provided is not a synonym for worse. It is the right answer for storage and
    /// usually for USB, and the wrong one for an onboard audio codec, where the class driver
    /// cannot do jack detection or channel configuration at all. What matters is knowing which
    /// one is bound.
    /// </summary>
    public bool IsInboxDriver
        => Provider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

    public string Describe()
        => $"{DeviceName} — {(string.IsNullOrWhiteSpace(Provider) ? "provider unknown" : Provider)}"
           + (string.IsNullOrWhiteSpace(Version) ? string.Empty : $" {Version}")
           + (string.IsNullOrWhiteSpace(DriverDate) ? string.Empty : $", {DriverDate}")
           + (string.IsNullOrWhiteSpace(Service) ? string.Empty : $", via {Service}");
}

public sealed record DriverInventory(
    bool Available,
    IReadOnlyList<InstalledDriver> Drivers,
    string Observation)
{
    public static DriverInventory Unavailable(string reason) => new(false, [], reason);

    public IEnumerable<InstalledDriver> InClass(string deviceClass)
        => Drivers.Where(driver =>
            driver.DeviceClass.Equals(deviceClass, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One present device in a class the policy permits offering.</summary>
public sealed record DeviceEntry(
    string InstanceId,
    string Name,
    string DeviceClass,
    bool Disabled,
    bool InUse);

public sealed record DeviceInventory(
    bool Available,
    IReadOnlyList<DeviceEntry> Devices,
    string Observation,
    int SystemDevicesSeen,
    IReadOnlyList<string> SystemDevicesRefused)
{
    public static DeviceInventory Unavailable(string reason) => new(false, [], reason, 0, []);
}

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
    int? ReceiveCoalescing,
    int? PowerManagementCapabilities,
    int? LargeSendOffload,
    string Observation)
{
    /// <summary>
    /// Whether Windows is allowed to power the adapter down when it judges it idle.
    ///
    /// 0x18 in PnPCapabilities is the combination the device properties checkbox clears. An adapter
    /// that has not been told otherwise is left able to do this, which on some controllers means
    /// renegotiating the link during a quiet moment.
    /// </summary>
    public bool? CanBePoweredDown
        => PowerManagementCapabilities is { } capabilities ? (capabilities & 0x18) != 0x18 : null;
}

/// <summary>One service's start configuration as the control manager records it.</summary>
public sealed record ServiceState(string Name, string DisplayName, int StartType, int ServiceType)
{
    public bool IsDisabled => StartType == 4;

    public bool StartsAutomatically => StartType is 0 or 1 or 2;

    public string StartTypeName => StartType switch
    {
        0 => "Boot",
        1 => "System",
        2 => "Automatic",
        3 => "Manual",
        4 => "Disabled",
        _ => $"Unknown ({StartType})"
    };
}

/// <summary>
/// Every service plus the inverted dependency graph.
///
/// Windows records dependencies one way — each service lists what it needs — so answering "what
/// breaks if this stops" requires walking the whole set and inverting it. That inversion is the
/// safety mechanism: without it, disabling a service that something else quietly requires looks
/// exactly like disabling one that nothing needs.
/// </summary>
public sealed record ServiceInventory(
    bool Available,
    IReadOnlyDictionary<string, ServiceState> Services,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Dependents,
    string Observation)
{
    public static ServiceInventory Unavailable(string reason)
        => new(false,
            new Dictionary<string, ServiceState>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            reason);

    /// <summary>
    /// Services that would lose a dependency, counting only those not already disabled — a
    /// dependent that is itself switched off cannot be broken by this.
    /// </summary>
    public IReadOnlyList<string> LiveDependentsOf(string serviceName)
    {
        if (!Dependents.TryGetValue(serviceName, out var names))
        {
            return [];
        }

        return names
            .Where(name => !Services.TryGetValue(name, out var state) || !state.IsDisabled)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

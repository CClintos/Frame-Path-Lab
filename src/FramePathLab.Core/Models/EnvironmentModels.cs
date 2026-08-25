namespace FramePathLab.Core.Models;

public sealed record DisplaySnapshot(
    string DeviceName,
    string AdapterDescription,
    string MonitorDescription,
    bool IsPrimary,
    bool IsAttached,
    int Width,
    int Height,
    int BitsPerPixel,
    double CurrentRefreshHz,
    double MaximumRefreshAtCurrentResolutionHz,
    IReadOnlyList<double> AvailableRefreshRatesHz);

public sealed record SteamGameSnapshot(
    bool SteamDetected,
    bool Cs2Installed,
    bool Cs2Running,
    string BuildId,
    string InstallState);

public sealed record PowerSnapshot(
    bool IsOnAc,
    int BatteryPercent,
    string ActiveSchemeId,
    string Status);

public sealed record EnvironmentSnapshot(
    DateTimeOffset CapturedAtUtc,
    string OsDescription,
    string OsVersion,
    bool Is64BitOperatingSystem,
    bool IsProcessElevated,
    bool IsRemoteSession,
    int LogicalProcessorCount,
    ulong TotalPhysicalMemoryBytes,
    IReadOnlyList<DisplaySnapshot> Displays,
    SteamGameSnapshot SteamGame,
    PowerSnapshot Power,
    IReadOnlyList<string> ObservedOptionalApplications,
    CapabilityState DecisionGradeCapability,
    IReadOnlyList<string> CapabilityLimitations);

public sealed record ScanReport(
    EnvironmentSnapshot Snapshot,
    IReadOnlyList<FindingCard> Findings,
    string Summary,
    string SchemaVersion);

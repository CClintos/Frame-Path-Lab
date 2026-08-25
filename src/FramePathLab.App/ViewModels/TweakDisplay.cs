namespace FramePathLab.App.ViewModels;

public enum TweakUiStatus
{
    Enabled,
    ActionAvailable,
    ManualCheck,
    Blocked
}

public enum TweakActionKind
{
    None,
    StartPowerSession,
    RestorePowerSession,
    OpenAdvancedDisplay,
    OpenGameMode,
    OpenGraphicsDefaults,
    ShowCs2Checklist,
    ShowOverlayReview
}

public sealed record TweakDisplay(
    string Id,
    string Category,
    string Title,
    TweakUiStatus Status,
    string StatusLabel,
    string CurrentValue,
    string RecommendedValue,
    string Summary,
    string WhyItMatters,
    string Risk,
    string EvidenceLabel,
    TweakActionKind ActionKind,
    string ActionLabel,
    int SortOrder)
{
    public bool HasAction => ActionKind != TweakActionKind.None;

    public string StatusForeground => Status switch
    {
        TweakUiStatus.Enabled => "#82E6B1",
        TweakUiStatus.ActionAvailable => "#7EDBFF",
        TweakUiStatus.ManualCheck => "#FFD477",
        TweakUiStatus.Blocked => "#FF9F9A",
        _ => "#D7E1EF"
    };

    public string StatusBackground => Status switch
    {
        TweakUiStatus.Enabled => "#12382A",
        TweakUiStatus.ActionAvailable => "#12374A",
        TweakUiStatus.ManualCheck => "#3B2E13",
        TweakUiStatus.Blocked => "#3C1F25",
        _ => "#253244"
    };

    public string StatusBorder => Status switch
    {
        TweakUiStatus.Enabled => "#2C8A5C",
        TweakUiStatus.ActionAvailable => "#2F8DB4",
        TweakUiStatus.ManualCheck => "#9C762B",
        TweakUiStatus.Blocked => "#A94A52",
        _ => "#44536A"
    };
}

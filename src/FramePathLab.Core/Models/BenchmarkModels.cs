namespace FramePathLab.Core.Models;

public sealed record MetricSummary(
    string Id,
    string Label,
    double? Value,
    string Unit,
    string Formula,
    string Availability);

public sealed record CaptureAnalysis(
    DateTimeOffset AnalyzedAtUtc,
    string SourceFileName,
    string SourceSha256,
    long SourceSizeBytes,
    string ParserSchemaVersion,
    string SelectedApplication,
    string FrameTimeColumn,
    long TotalRows,
    long AcceptedRows,
    long RejectedRows,
    ResultOutcome Outcome,
    IReadOnlyList<MetricSummary> Metrics,
    IReadOnlyDictionary<string, long> PresentModeCounts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FrameDeliveryFinding>? DeliveryFindings = null);

/// <summary>How severely a delivery finding bears on competitive latency.</summary>
public enum DeliverySeverity
{
    Good,
    Advisory,
    Costly
}

/// <summary>
/// A conclusion drawn from the capture itself rather than from a settings read. These are the only
/// statements in the product that can say what the presentation path actually did.
/// </summary>
public sealed record FrameDeliveryFinding(
    string Id,
    string Title,
    DeliverySeverity Severity,
    string Observed,
    string Meaning,
    string NextStep);

public enum VerificationVerdict
{
    /// <summary>Consistency metrics improved beyond run-to-run noise.</summary>
    Improved,

    /// <summary>Consistency metrics got worse beyond run-to-run noise.</summary>
    Regressed,

    /// <summary>Movement in both directions of a similar size.</summary>
    Mixed,

    /// <summary>Everything moved less than the noise band. The change did nothing here.</summary>
    NoMeasuredChange,

    /// <summary>The two captures do not describe the same thing.</summary>
    NotComparable
}

/// <summary>One metric measured either side of a change.</summary>
public sealed record MetricDelta(
    string MetricId,
    string Label,
    double Before,
    double After,
    double ChangePercent,
    bool LowerIsBetter,
    bool IsImprovement,
    bool IsRegression,
    bool IsMeaningful)
{
    public string Direction => ChangePercent switch
    {
        > 0 => "up",
        < 0 => "down",
        _ => "flat"
    };

    public string Summary
        => $"{Label}: {Before:0.###} → {After:0.###} ({ChangePercent:+0.##;-0.##;0}%)"
           + (IsMeaningful ? IsImprovement ? " better" : " worse" : " within noise");
}

/// <summary>
/// The measured outcome of one recorded change. This is the only place the product states whether
/// a tweak actually did anything on this machine, and it says so from two captures rather than
/// from a claim about the setting.
/// </summary>
public sealed record TweakVerification(
    Guid? TransactionId,
    string TweakId,
    string BeforeCapture,
    string AfterCapture,
    IReadOnlyList<MetricDelta> Deltas,
    VerificationVerdict Verdict,
    string Finding,
    string Recommendation)
{
    /// <summary>True when the evidence says to put the machine back the way it was.</summary>
    public bool ShouldRevert
        => Verdict is VerificationVerdict.Regressed or VerificationVerdict.NoMeasuredChange;
}

public sealed record CaptureAnalysisOptions(
    double? FrameBudgetMs = null,
    long MaximumFileBytes = 268_435_456,
    int MaximumRows = 2_000_000,
    int MaximumColumns = 256,
    int MaximumCellCharacters = 4096);

public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    string Kind,
    string Title,
    string Outcome,
    string SourceFileName,
    string SourceSha256,
    IReadOnlyDictionary<string, double> NumericSummary);

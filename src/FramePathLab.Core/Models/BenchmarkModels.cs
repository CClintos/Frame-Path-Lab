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

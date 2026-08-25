using System.Globalization;
using System.Text;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Reporting;

public sealed class MarkdownReportWriter
{
    public string Build(ScanReport? scan, CaptureAnalysis? analysis)
    {
        if (scan is null && analysis is null)
        {
            throw new InvalidOperationException("A scan or capture analysis is required.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("# FramePath Lab local report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine("> Observational diagnostic report. It is not a guarantee of lower input latency, competitive advantage, or anti-cheat compatibility.");
        builder.AppendLine();

        if (scan is not null)
        {
            AppendScan(builder, scan);
        }

        if (analysis is not null)
        {
            AppendAnalysis(builder, analysis);
        }

        builder.AppendLine("## Safety boundary");
        builder.AppendLine();
        builder.AppendLine("Creating this report does not change Windows, driver, monitor, Steam, or CS2 settings. The desktop app exposes one separately approved, bounded power-plan experiment; this report does not claim that it helped. The app does not inject, inspect game memory, automate input, or inspect/manipulate packets.");
        return builder.ToString();
    }

    public async Task WriteAsync(
        string path,
        ScanReport? scan,
        CaptureAnalysis? analysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = Build(scan, analysis);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Report destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void AppendScan(StringBuilder builder, ScanReport scan)
    {
        var snapshot = scan.Snapshot;
        builder.AppendLine("## Environment scan");
        builder.AppendLine();
        builder.AppendLine($"- Capability: **{snapshot.DecisionGradeCapability}**");
        builder.AppendLine($"- OS: {Escape(snapshot.OsDescription)} ({Escape(snapshot.OsVersion)})");
        builder.AppendLine($"- Session: {(snapshot.IsRemoteSession ? "Remote" : "Local")}");
        builder.AppendLine($"- CS2: {Escape(snapshot.SteamGame.InstallState)}; build {Escape(snapshot.SteamGame.BuildId)}; running {(snapshot.SteamGame.Cs2Running ? "yes" : "no")}");
        builder.AppendLine($"- Power: {Escape(snapshot.Power.Status)}");
        builder.AppendLine();

        builder.AppendLine("### Displays");
        builder.AppendLine();
        builder.AppendLine("| Display | Adapter | Mode | Primary |");
        builder.AppendLine("|---|---|---:|---:|");
        foreach (var display in snapshot.Displays)
        {
            builder.AppendLine($"| {Escape(display.MonitorDescription)} | {Escape(display.AdapterDescription)} | {display.Width}×{display.Height} @ {display.CurrentRefreshHz:0.###} Hz | {(display.IsPrimary ? "Yes" : "No")} |");
        }

        builder.AppendLine();
        builder.AppendLine("### Findings");
        builder.AppendLine();
        foreach (var finding in scan.Findings)
        {
            builder.AppendLine($"#### {Escape(finding.Title)} — {finding.Disposition}");
            builder.AppendLine();
            builder.AppendLine(Escape(finding.Summary));
            builder.AppendLine();
            builder.AppendLine($"Observed state: {Escape(finding.ObservedState)}  ");
            builder.AppendLine($"Provenance: {finding.Provenance}; evidence: {finding.EvidenceQuality}; causal decision enabled: {(finding.CanProduceCausalDecision ? "yes" : "no")}");
            builder.AppendLine();
        }
    }

    private static void AppendAnalysis(StringBuilder builder, CaptureAnalysis analysis)
    {
        builder.AppendLine("## Imported capture analysis");
        builder.AppendLine();
        builder.AppendLine($"- Source filename: `{EscapeCode(analysis.SourceFileName)}`");
        builder.AppendLine($"- SHA-256: `{analysis.SourceSha256}`");
        builder.AppendLine($"- Parser schema: `{analysis.ParserSchemaVersion}`");
        builder.AppendLine($"- Target: `{EscapeCode(analysis.SelectedApplication)}`");
        builder.AppendLine($"- Outcome: **{analysis.Outcome}**");
        builder.AppendLine($"- Accepted/rejected rows: {analysis.AcceptedRows:N0}/{analysis.RejectedRows:N0}");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value | Availability | Formula |");
        builder.AppendLine("|---|---:|---|---|");
        foreach (var metric in analysis.Metrics)
        {
            var value = metric.Value.HasValue
                ? metric.Value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + metric.Unit
                : "N/A";
            builder.AppendLine($"| {Escape(metric.Label)} | {value} | {Escape(metric.Availability)} | {Escape(metric.Formula)} |");
        }

        if (analysis.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Limitations and warnings");
            builder.AppendLine();
            foreach (var warning in analysis.Warnings)
            {
                builder.AppendLine($"- {Escape(warning)}");
            }
        }

        builder.AppendLine();
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeCode(string value)
        => value.Replace("`", "'", StringComparison.Ordinal);
}

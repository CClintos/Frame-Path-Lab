using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

public sealed class ScanCoordinator(IEnvironmentScanner scanner, IEvidenceCatalog evidenceCatalog)
{
    public async Task<ScanReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        var findings = evidenceCatalog.Evaluate(snapshot);
        var actionCount = findings.Count(finding => finding.Disposition is FindingDisposition.Measure or FindingDisposition.GuidedExperiment);
        var summary = snapshot.DecisionGradeCapability == CapabilityState.Supported
            ? $"Read-only scan completed. {actionCount} item(s) may be worth measuring; no settings were changed."
            : "Read-only scan completed with platform limitations. Inventory is available, but decision-grade claims are blocked.";
        return new ScanReport(snapshot, findings, summary, "scan-v1");
    }
}

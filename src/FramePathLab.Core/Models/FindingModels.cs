namespace FramePathLab.Core.Models;

public sealed record EvidenceSource(string Title, Uri Url);

public sealed record FindingCard(
    string Id,
    string Title,
    string Summary,
    string ObservedState,
    ObservationProvenance Provenance,
    EvidenceQuality EvidenceQuality,
    FindingKind Kind,
    FindingDisposition Disposition,
    CapabilityState Capability,
    bool CanProduceCausalDecision,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<EvidenceSource> Sources);

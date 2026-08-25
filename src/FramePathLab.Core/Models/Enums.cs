namespace FramePathLab.Core.Models;

public enum CapabilityState
{
    Supported,
    Provisional,
    Unsupported,
    Unknown
}

public enum ObservationProvenance
{
    WindowsApi,
    SteamManifest,
    ProcessObservation,
    ImportedCapture,
    UserAttested,
    Unknown
}

public enum FindingKind
{
    ConfigurationMismatch,
    DiagnosticHypothesis,
    GuidedCorrection,
    CausalExperiment,
    PromotedRecommendation,
    Exclusion
}

public enum FindingDisposition
{
    NoAction,
    ExplainOnly,
    Measure,
    GuidedExperiment,
    Unsupported,
    Excluded
}

public enum EvidenceQuality
{
    Strong,
    Moderate,
    Weak,
    Insufficient,
    Disproven
}

public enum ResultOutcome
{
    BaselineOnly,
    MeasurementReady,
    Observational,
    Inconclusive,
    Invalid
}

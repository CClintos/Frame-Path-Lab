namespace FramePathLab.Core.Models;

public enum PowerSessionState
{
    Prepared,
    AppliedVerified,
    RevertedVerified,
    ApplyFailed,
    VerificationFailed,
    ExternalChange,
    RecoveryFailed
}

public sealed record PowerSchemeDescriptor(Guid Id, string Name);

public sealed record PowerSessionRecord(
    Guid SessionId,
    string Operation,
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int OwnerProcessId,
    long OwnerProcessStartTimeUtcTicks,
    Guid OriginalSchemeId,
    string OriginalSchemeName,
    Guid TargetSchemeId,
    string TargetSchemeName,
    PowerSessionState State,
    Guid GuardianNonce,
    string LastObservation,
    string? Failure);

public sealed record PowerSessionOverview(
    Guid ActiveSchemeId,
    string ActiveSchemeName,
    IReadOnlyList<PowerSchemeDescriptor> AvailableSchemes,
    PowerSessionRecord? Journal,
    bool HighPerformanceAvailable,
    bool HighPerformancePolicyAllowed,
    string PolicyStatus,
    bool HasUnresolvedSession,
    string Status);

public sealed record PowerSessionTransition(
    PowerSessionRecord Record,
    Guid ObservedSchemeId,
    string Message);

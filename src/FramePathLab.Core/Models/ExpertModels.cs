namespace FramePathLab.Core.Models;

/// <summary>How much a tweak can cost the user if it is wrong for their machine.</summary>
public enum TweakRisk
{
    /// <summary>Reversible in place, no reboot, no security or stability surface.</summary>
    Low,

    /// <summary>Reversible, but the effect is workload-dependent and can regress a specific system.</summary>
    Moderate,

    /// <summary>Requires a reboot, touches driver/kernel behaviour, or can destabilise a marginal system.</summary>
    High,

    /// <summary>Measurably faster, but reduces an operating-system security guarantee.</summary>
    SecurityTradeOff
}

/// <summary>Where the change physically lives, which determines how it is reverted.</summary>
public enum TweakScope
{
    CurrentUser,
    Machine,
    RunningProcess,
    Firmware,
    VendorControlPanel
}

/// <summary>Result of reading a tweak's live state. Never inferred; unknown stays unknown.</summary>
public enum TweakState
{
    /// <summary>Already at the value this catalogue would apply.</summary>
    Optimal,

    /// <summary>Read successfully and differs from the recommended value.</summary>
    Suboptimal,

    /// <summary>Could not be read on this system. No write is offered.</summary>
    Unknown,

    /// <summary>Hardware or OS does not have this surface at all.</summary>
    NotApplicable,

    /// <summary>Readable and suboptimal, but a precondition blocks the write.</summary>
    Blocked
}

/// <summary>
/// Product decision for a candidate tweak. Evidence and risk determine whether FramePath Lab may
/// write it; detecting a non-default value is never, by itself, permission to call it suboptimal.
/// </summary>
public enum TweakDisposition
{
    /// <summary>Strong evidence, bounded downside and a supported reversible write.</summary>
    RecommendDefault,

    /// <summary>Workload-dependent; only offer inside a controlled before/after experiment.</summary>
    OptInExperiment,

    /// <summary>Use the vendor or Windows supported UI and rescan; FramePath Lab does not write it.</summary>
    GuidedAction,

    /// <summary>Useful context for diagnosis, but not a performance recommendation.</summary>
    DiagnosticOnly,

    /// <summary>Unsafe, unsupported, disproven or outside the product's integrity boundary.</summary>
    Excluded
}

/// <summary>The kinds of atomic mutation the executor knows how to capture, apply and revert.</summary>
public enum MutationKind
{
    RegistryValue,
    SystemParameter,
    PowerSchemeValue,
    PowerOverlayScheme,
    ProcessAffinity,
    ProcessPriority,
    ProcessPowerThrottling,
    BootConfigurationValue
}

/// <summary>
/// One atomic, serializable change. The journal stores these as data rather than as code so a
/// revert never depends on the catalogue entry that produced it still existing or still agreeing.
/// </summary>
public sealed record MutationPlan(
    string MutationId,
    MutationKind Kind,
    string Target,
    string ValueName,
    string DesiredValue,
    string ValueType,
    string Description);

/// <summary>Before-state plus verified after-state for a single applied mutation.</summary>
/// <param name="AttemptedWrite">
/// False for a before-state that was captured but never written, which happens when an earlier
/// mutation in the same tweak failed. Such a record has nothing to undo, so a revert must skip it
/// rather than count it as a failed restore.
/// </param>
public sealed record MutationRecord(
    string MutationId,
    MutationKind Kind,
    string Target,
    string ValueName,
    string ValueType,
    string Description,
    bool ExistedBefore,
    string? BeforeValue,
    string DesiredValue,
    string? ObservedAfterValue,
    bool VerifiedAfterWrite,
    string Observation,
    bool AttemptedWrite = true);

/// <summary>A live reading of one tweak against the machine.</summary>
public sealed record TweakReading(
    TweakState State,
    string CurrentValue,
    string RecommendedValue,
    string Detail);

/// <summary>Static description of an expert tweak, independent of any particular machine.</summary>
public sealed record ExpertTweakDefinition(
    string Id,
    string Category,
    string Title,
    string Mechanism,
    string Rationale,
    string Tradeoff,
    TweakRisk Risk,
    TweakScope Scope,
    EvidenceQuality Evidence,
    bool RequiresElevation,
    bool RequiresReboot,
    bool RequiresGameRestart,
    IReadOnlyList<EvidenceSource> Sources)
{
    public TweakDisposition Disposition { get; init; } = TweakDisposition.OptInExperiment;

    public string DispositionReason { get; init; } =
        "This candidate is workload-dependent and requires a controlled before/after benchmark.";
}

/// <summary>Definition plus this machine's reading plus the exact writes that would be made.</summary>
public sealed record ExpertTweakCard(
    ExpertTweakDefinition Definition,
    TweakReading Reading,
    IReadOnlyList<MutationPlan> Plan,
    string? BlockedReason)
{
    public bool CanApply
        => Reading.State == TweakState.Suboptimal
           && Plan.Count > 0
           && BlockedReason is null;
}

/// <summary>A completed apply, retained so it can be reverted independently later.</summary>
public sealed record TweakTransaction(
    Guid TransactionId,
    string TweakId,
    string TweakTitle,
    DateTimeOffset AppliedAtUtc,
    DateTimeOffset? RevertedAtUtc,
    bool RequiresReboot,
    IReadOnlyList<MutationRecord> Mutations,
    string State,
    string LastObservation)
{
    public const string StateApplied = "Applied";
    public const string StateReverted = "Reverted";
    public const string StatePartiallyApplied = "PartiallyApplied";
    public const string StateRevertFailed = "RevertFailed";

    public bool IsOutstanding
        => string.Equals(State, StateApplied, StringComparison.Ordinal)
           || string.Equals(State, StatePartiallyApplied, StringComparison.Ordinal)
           || string.Equals(State, StateRevertFailed, StringComparison.Ordinal);
}

/// <summary>Everything the catalogue needs to evaluate tweaks against one machine.</summary>
public sealed record ExpertScanContext(
    EnvironmentSnapshot Environment,
    CpuTopology Cpu,
    IReadOnlyList<GpuTelemetry> Gpus,
    DisplayTiming? PrimaryTiming,
    InputChainReport? Input,
    SystemLatencyReport? Latency,
    IReadOnlyList<NetworkAdapterState> NetworkAdapters,
    int? GameProcessId,
    string GameExecutableName,
    MemoryConfiguration Memory,
    SteamActivity Steam,
    bool? ForcedPlatformClock,
    long PerformanceCounterFrequency,
    bool? GpuMessageSignalledInterrupts,
    string? GpuInterruptRegistryPath,
    string GpuInterruptObservation);

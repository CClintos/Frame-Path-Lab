namespace FramePathLab.Core.Models;

/// <summary>
/// One answer the catalogue got when it read a machine, kept so the identical question can be
/// answered again later somewhere else.
///
/// The catalogue reads live state through <c>ITweakStateReader</c> rather than from the scan
/// context, so a context alone is not enough to evaluate a machine offline. Recording the reads
/// during collection and replaying them during review keeps the two paths identical without the
/// catalogue needing to know that offline review exists — and means a card added later is carried
/// by the next collection automatically rather than being silently missing from snapshots.
/// </summary>
public sealed record RecordedRead(string Key, bool Exists, string? Value);

/// <summary>
/// Enough of a machine to tell it apart from a different one.
///
/// This is a mistake guard, not a security boundary: it exists so a plan built against a desktop
/// cannot be applied to a laptop by accident. What actually constrains writes is the allowlist,
/// which is compiled in and checked on the target regardless of what any file claims.
/// </summary>
public sealed record MachineIdentity(
    string MachineName,
    string Fingerprint,
    string ProcessorBrand,
    int PhysicalCoreCount,
    int LogicalProcessorCount,
    ulong TotalPhysicalMemoryBytes,
    string OsVersion,
    string PrimaryGpu)
{
    public string Describe()
        => $"{MachineName} — {ProcessorBrand}, {PhysicalCoreCount}C/{LogicalProcessorCount}T, "
           + $"{TotalPhysicalMemoryBytes / 1024d / 1024 / 1024:0.#} GB"
           + (string.IsNullOrWhiteSpace(PrimaryGpu) ? string.Empty : $", {PrimaryGpu}");
}

/// <summary>
/// A complete reading of one machine, portable to another for review.
///
/// A snapshot carries observations only. It holds no instruction to change anything, which is why
/// opening one from an untrusted source cannot do anything worse than describe a machine that does
/// not exist.
/// </summary>
public sealed record MachineSnapshot(
    int FormatVersion,
    DateTimeOffset CapturedUtc,
    string CollectorVersion,
    bool CollectedElevated,
    MachineIdentity Identity,
    ExpertScanContext Context,
    IReadOnlyList<RecordedRead> Reads)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// A set of tweaks chosen on one machine, to be carried back to the machine they were chosen for.
///
/// <para>
/// It carries tweak <em>identifiers</em> and nothing else. It deliberately does not carry the
/// registry paths, values or device nodes those tweaks write. That is the whole security design of
/// this format: the target re-derives every mutation from its own compiled-in catalogue, against
/// its own freshly scanned state, and passes each one through the same allowlist as any other
/// write. An edited plan file can therefore only ever select from what the target machine was
/// already willing to do — it cannot introduce a new write, retarget an existing one, or smuggle a
/// value past the guard. A plan file that carried mutations directly would be a command channel
/// into an elevated process, which is exactly the hole the allowlist was added to close.
/// </para>
/// </summary>
public sealed record TweakPlanFile(
    int FormatVersion,
    DateTimeOffset CreatedUtc,
    MachineIdentity Target,
    IReadOnlyList<string> TweakIds,
    string Note)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>Outcome of applying one tweak from a plan file, written back for the operator to read.</summary>
public sealed record PlanApplicationResult(
    string TweakId,
    string Title,
    bool Applied,
    string Observation);

public sealed record PlanApplicationReport(
    DateTimeOffset CompletedUtc,
    MachineIdentity AppliedOn,
    IReadOnlyList<PlanApplicationResult> Results,
    bool RebootRequired,
    string Summary);

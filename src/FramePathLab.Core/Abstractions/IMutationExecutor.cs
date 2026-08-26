using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

/// <summary>
/// Read side of the mutation surface. The catalogue depends only on this, so evaluating what a
/// machine needs can never accidentally change it.
/// </summary>
public interface ITweakStateReader
{
    /// <summary>Reads the current value without changing anything.</summary>
    string? Read(MutationPlan plan, out bool exists);
}

/// <summary>
/// Applies and reverses one atomic system change.
///
/// Every apply is preceded by a capture of the exact prior value, and every apply is followed by a
/// read-back. A mutation whose read-back does not match the requested value is reported as
/// unverified rather than as success, so a silently-ignored write never counts as an applied tweak.
/// </summary>
public interface IMutationExecutor : ITweakStateReader
{
    /// <summary>
    /// Captures a serializable before-state without writing. Failure to distinguish an absent value
    /// from an unreadable value must throw so the caller fails closed.
    /// </summary>
    MutationRecord Capture(MutationPlan plan);

    /// <summary>
    /// Captures the before-state, writes the desired value, then reads back what the system
    /// actually holds. Throws only when the before-state could not be captured, because applying
    /// without a recorded prior value would create an unrevertible change.
    /// </summary>
    MutationRecord Apply(MutationPlan plan);

    /// <summary>
    /// Applies from an already journalled capture. The executor must compare the live value with
    /// the capture immediately before writing and refuse if it drifted after user approval.
    /// </summary>
    MutationRecord Apply(MutationPlan plan, MutationRecord captured);

    /// <summary>
    /// Restores the captured before-state. Compare-before-write: when the live value no longer
    /// matches what this transaction wrote, a later external change is preserved instead of being
    /// overwritten.
    /// </summary>
    MutationRecord Revert(MutationRecord record);

    /// <summary>Whether this mutation needs an elevated process to succeed.</summary>
    bool RequiresElevation(MutationPlan plan);
}

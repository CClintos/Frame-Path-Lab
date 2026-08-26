using FramePathLab.Core.Evidence;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

/// <summary>
/// Evaluates a machine that is not the one running this code.
///
/// <para>
/// The point of separating collection from review is that the two useful machines are rarely the
/// same one. The computer being tuned is the one that has to stay clean and stay playing; the
/// computer being read from is whichever one is to hand. So collection is a single non-interactive
/// pass that writes a file, and everything else — reading the catalogue, deciding what to change,
/// arguing with it — happens wherever is convenient.
/// </para>
/// <para>
/// Review reaches the same verdicts the target machine would, because it runs the same catalogue
/// against the same context and the same recorded reads, and applies the same allowlist. What it
/// cannot do is write, and it does not pretend otherwise: the output of review is a chosen set,
/// not a change.
/// </para>
/// </summary>
public static class RemoteMachineReview
{
    public sealed record Result(
        MachineSnapshot Snapshot,
        IReadOnlyList<ExpertTweakCard> Cards,
        string Summary);

    public static Result Review(MachineSnapshot snapshot, IMutationGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var effectiveGuard = guard ?? AllowlistMutationGuard.Instance;
        var reader = new ReplayStateReader(snapshot.Reads);
        var cards = ExpertTweakCatalog.Evaluate(snapshot.Context, reader)
            .Select(card => Gate(card, snapshot, effectiveGuard))
            .ToArray();

        var available = cards.Count(card => card.CanApply);
        var age = DateTimeOffset.UtcNow - snapshot.CapturedUtc;
        var ageText = age < TimeSpan.FromMinutes(2)
            ? "just now"
            : age < TimeSpan.FromHours(1)
                ? $"{age.TotalMinutes:0} minutes ago"
                : age < TimeSpan.FromDays(1)
                    ? $"{age.TotalHours:0} hours ago"
                    : $"{age.TotalDays:0} days ago";

        return new Result(
            snapshot,
            cards,
            $"{snapshot.Identity.Describe()} · collected {ageText} · "
            + $"{available} change{(available == 1 ? string.Empty : "s")} available to select · "
            + $"{cards.Length} checked. Nothing here writes to this computer.");
    }

    /// <summary>
    /// Reproduces the gates the target machine will apply, so what review shows as available is
    /// what will actually be offered when the plan gets there.
    ///
    /// Getting this wrong in the permissive direction would be the worst outcome of the whole
    /// feature: choosing eighteen changes on a laptop and finding out on the gaming machine that
    /// eleven were never eligible. The allowlist is compiled in and identical on both sides, and
    /// whether the collection ran elevated is recorded in the snapshot, so both can be checked here.
    /// </summary>
    private static ExpertTweakCard Gate(ExpertTweakCard card, MachineSnapshot snapshot, IMutationGuard guard)
    {
        if (card.Plan.Count == 0 || card.BlockedReason is not null)
        {
            return card;
        }

        var violation = card.Plan
            .Select(guard.FindViolation)
            .FirstOrDefault(problem => problem is not null);
        if (violation is not null)
        {
            return card with { BlockedReason = $"Refused by the write allowlist: {violation}" };
        }

        if (!snapshot.CollectedElevated && card.Definition.RequiresElevation)
        {
            return card with
            {
                BlockedReason = "This was collected without administrator rights, so the machine-scope "
                                + "state could not be read reliably. Re-collect elevated on the target."
            };
        }

        return card;
    }

    /// <summary>
    /// Builds the file that carries a decision back to the machine it was made for.
    ///
    /// Only identifiers travel. See <see cref="TweakPlanFile"/> for why that is the security design
    /// rather than an economy.
    /// </summary>
    public static TweakPlanFile BuildPlan(
        MachineSnapshot snapshot,
        IEnumerable<ExpertTweakCard> chosen,
        string note)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(chosen);

        var ids = chosen
            .Where(card => card.CanApply)
            .Select(card => card.Definition.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new TweakPlanFile(
            TweakPlanFile.CurrentFormatVersion,
            DateTimeOffset.UtcNow,
            snapshot.Identity,
            ids,
            string.IsNullOrWhiteSpace(note) ? "Selected from a snapshot review." : note.Trim());
    }

    /// <summary>
    /// Whether a plan belongs on the machine about to run it.
    ///
    /// Returns the reason to refuse, or null to proceed. The fingerprint is the primary check; the
    /// machine name is reported alongside it because a fingerprint tells a person nothing.
    /// </summary>
    public static string? FindTargetMismatch(TweakPlanFile plan, MachineIdentity here)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(here);

        if (string.Equals(plan.Target.Fingerprint, here.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"This plan was built for {plan.Target.Describe()}, but this machine is "
               + $"{here.Describe()}. Nothing was applied. Collect a snapshot on this machine and "
               + "choose changes against that instead.";
    }
}

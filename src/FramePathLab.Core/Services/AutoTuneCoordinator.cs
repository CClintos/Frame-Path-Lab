using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

/// <summary>How much of the catalogue an automatic run is allowed to touch.</summary>
public enum AutoTuneLevel
{
    /// <summary>Recommended defaults only: documented, per-user, instantly reversible.</summary>
    Conservative,

    /// <summary>Defaults plus the experiments whose downside is bounded.</summary>
    Balanced,

    /// <summary>Everything the policy permits writing, including reboot-gated changes.</summary>
    Aggressive
}

/// <summary>Whether changes are measured together or one at a time.</summary>
public enum AutoTuneMode
{
    /// <summary>
    /// Apply everything, measure once. Fast, and answers "was this set worth it" — but cannot say
    /// which member of the set did the work.
    /// </summary>
    Bundle,

    /// <summary>
    /// One change per measurement pair. Slower by a factor of the number of candidates, and the
    /// only way to attribute a result to a specific change.
    /// </summary>
    Isolate
}

public sealed record AutoTuneStep(
    string TweakId,
    string TweakTitle,
    Guid? TransactionId,
    TweakVerification? Verification,
    bool Kept,
    string Outcome);

public sealed record AutoTuneReport(
    AutoTuneLevel Level,
    AutoTuneMode Mode,
    int CandidatesConsidered,
    int Applied,
    int Kept,
    int Reverted,
    IReadOnlyList<AutoTuneStep> Steps,
    bool RestartRequired,
    string Summary);

/// <summary>
/// Runs the whole loop: measure, change, measure again, keep what earned its place and reverse what
/// did not.
///
/// The design decision worth stating is that nothing is kept on the strength of the catalogue's own
/// opinion. A candidate is applied because policy permits writing it, and retained only because the
/// measurement afterwards supports it. That inverts how a tweak list works — a list is a set of
/// claims applied and left alone, and the reason they accumulate folklore is that nothing ever
/// removes an entry that stopped being true.
/// </summary>
public sealed class AutoTuneCoordinator
{
    private readonly ExpertTweakEngine _engine;
    private readonly IBenchmarkRunner _benchmark;

    public AutoTuneCoordinator(ExpertTweakEngine engine, IBenchmarkRunner benchmark)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _benchmark = benchmark ?? throw new ArgumentNullException(nameof(benchmark));
    }

    /// <summary>Candidates a level is allowed to write, in a deliberate order.</summary>
    public static IReadOnlyList<ExpertTweakCard> SelectCandidates(
        IReadOnlyList<ExpertTweakCard> cards,
        AutoTuneLevel level)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var permitted = cards.Where(card => card.CanApply).Where(card => level switch
        {
            AutoTuneLevel.Conservative =>
                card.Definition.Disposition == TweakDisposition.RecommendDefault,

            AutoTuneLevel.Balanced =>
                card.Definition.Disposition == TweakDisposition.RecommendDefault
                || (card.Definition.Disposition == TweakDisposition.OptInExperiment
                    && card.Definition.Risk != TweakRisk.High
                    && card.Definition.Risk != TweakRisk.SecurityTradeOff),

            AutoTuneLevel.Aggressive =>
                card.Definition.Disposition is TweakDisposition.RecommendDefault
                    or TweakDisposition.OptInExperiment,

            _ => false
        });

        // Cheapest and most certain first. A change needing a restart cannot be measured in the
        // same session, so it is ordered last and reported rather than verified.
        return permitted
            .OrderBy(card => card.Definition.RequiresReboot)
            .ThenBy(card => card.Definition.Disposition == TweakDisposition.RecommendDefault ? 0 : 1)
            .ThenBy(card => card.Definition.Risk)
            .ThenBy(card => card.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AutoTuneReport Run(
        IReadOnlyList<ExpertTweakCard> cards,
        AutoTuneLevel level,
        AutoTuneMode mode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = SelectCandidates(cards, level);
        if (candidates.Count == 0)
        {
            return new AutoTuneReport(level, mode, 0, 0, 0, 0, [], false,
                "Nothing to do: every candidate at this level is already set, or is blocked.");
        }

        progress?.Report($"Measuring a baseline before changing anything ({candidates.Count} candidate(s)).");
        var baseline = _benchmark.Run(cancellationToken);
        if (baseline.Outcome == ResultOutcome.Invalid)
        {
            return new AutoTuneReport(level, mode, candidates.Count, 0, 0, 0, [], false,
                $"The baseline measurement failed, so nothing was changed. {FirstWarning(baseline)}");
        }

        return mode == AutoTuneMode.Isolate
            ? RunIsolated(candidates, baseline, level, progress, cancellationToken)
            : RunBundled(candidates, baseline, level, progress, cancellationToken);
    }

    private AutoTuneReport RunIsolated(
        IReadOnlyList<ExpertTweakCard> candidates,
        CaptureAnalysis baseline,
        AutoTuneLevel level,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var steps = new List<AutoTuneStep>();
        var current = baseline;
        var restartRequired = false;

        foreach (var card in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Applying {card.Definition.Title}.");

            TweakTransaction transaction;
            try
            {
                transaction = _engine.Apply(card);
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
            {
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title, null, null, false,
                    $"Not applied: {exception.Message}"));
                continue;
            }

            // A change that only takes effect after a restart cannot be measured now. Keeping it
            // and saying so is more honest than measuring an unchanged machine and calling the
            // result evidence either way.
            if (card.Definition.RequiresReboot)
            {
                restartRequired = true;
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, null, true,
                    "Applied and kept unmeasured: it takes effect after a restart. Re-run to measure it."));
                continue;
            }

            progress?.Report($"Measuring {card.Definition.Title}.");
            var after = _benchmark.Run(cancellationToken);
            var verification = TweakVerifier.Compare(current, after, transaction);

            // An unmeasurable pair is not a pass. Keeping a change because the measurement failed
            // would put the tool right back where a tweak list is: applying things and calling it
            // evidence. The change is reversed and the run stops, because the cause is the
            // measurement setup rather than this particular candidate.
            if (verification.Verdict == VerificationVerdict.NotComparable)
            {
                _engine.Revert(transaction.TransactionId, "autotune: measurement not comparable");
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, verification, false,
                    $"Reverted, not measured — {verification.Finding} Nothing further was applied."));
                break;
            }

            if (verification.ShouldRevert)
            {
                var reverted = _engine.Revert(transaction.TransactionId, $"autotune: {verification.Verdict}");
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, verification, false,
                    $"Reverted — {verification.Finding} "
                    + (reverted.State == TweakTransaction.StateReverted
                        ? "Every value was restored."
                        : "Review the restore detail.")));
                continue;
            }

            // Only a kept change moves the baseline forward, so each subsequent comparison is
            // against the machine as it now stands rather than against where it started.
            current = after;
            steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                transaction.TransactionId, verification, true,
                $"Kept — {verification.Finding}"));
        }

        return Summarize(level, AutoTuneMode.Isolate, candidates.Count, steps, restartRequired);
    }

    private AutoTuneReport RunBundled(
        IReadOnlyList<ExpertTweakCard> candidates,
        CaptureAnalysis baseline,
        AutoTuneLevel level,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var steps = new List<AutoTuneStep>();
        var applied = new List<TweakTransaction>();
        var restartRequired = false;

        foreach (var card in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var transaction = _engine.Apply(card);
                applied.Add(transaction);
                restartRequired |= card.Definition.RequiresReboot;
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, null, true, "Applied."));
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
            {
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title, null, null, false,
                    $"Not applied: {exception.Message}"));
            }
        }

        if (applied.Count == 0)
        {
            return Summarize(level, AutoTuneMode.Bundle, candidates.Count, steps, restartRequired);
        }

        progress?.Report($"Measuring {applied.Count} applied change(s) together.");
        var after = _benchmark.Run(cancellationToken);
        var verification = TweakVerifier.Compare(baseline, after);

        // Same rule as isolate mode: an unmeasurable pair reverses the set rather than keeping it.
        if (verification.Verdict != VerificationVerdict.NotComparable && !verification.ShouldRevert)
        {
            var kept = steps.Select(step => step.Kept
                ? step with
                {
                    Verification = verification,
                    Outcome = $"Kept as part of the set — {verification.Finding}"
                }
                : step).ToList();
            return Summarize(level, AutoTuneMode.Bundle, candidates.Count, kept, restartRequired);
        }

        // The set did not earn its place. Reversing all of it is the only defensible response,
        // because a bundled measurement cannot say which member was responsible.
        progress?.Report(verification.Verdict == VerificationVerdict.NotComparable
            ? "The measurement could not be compared. Reverting all of it."
            : "The set did not improve the measurement. Reverting all of it.");
        var updated = new List<AutoTuneStep>();
        foreach (var step in steps)
        {
            if (step.TransactionId is null)
            {
                updated.Add(step);
                continue;
            }

            _engine.Revert(step.TransactionId.Value, $"autotune bundle: {verification.Verdict}");
            updated.Add(step with
            {
                Kept = false,
                Verification = verification,
                Outcome = $"Reverted with the set — {verification.Finding} "
                          + "A bundled run cannot attribute this to one change; use isolate mode for that."
            });
        }

        return Summarize(level, AutoTuneMode.Bundle, candidates.Count, updated, restartRequired);
    }

    private static AutoTuneReport Summarize(
        AutoTuneLevel level,
        AutoTuneMode mode,
        int considered,
        IReadOnlyList<AutoTuneStep> steps,
        bool restartRequired)
    {
        var applied = steps.Count(step => step.TransactionId is not null);
        var kept = steps.Count(step => step.Kept);
        var reverted = applied - kept;

        var summary = applied == 0
            ? "No change could be applied."
            : $"{kept} of {applied} change(s) kept on the measurement; {reverted} reverted."
              + (mode == AutoTuneMode.Bundle && kept > 0
                  ? " Measured as a set, so the result belongs to the set rather than to any one change."
                  : string.Empty)
              + (restartRequired ? " A restart is needed before some changes take effect." : string.Empty);

        return new AutoTuneReport(level, mode, considered, applied, kept, reverted, steps, restartRequired, summary);
    }

    private static string FirstWarning(CaptureAnalysis analysis)
        => analysis.Warnings.Count > 0 ? analysis.Warnings[0] : string.Empty;
}

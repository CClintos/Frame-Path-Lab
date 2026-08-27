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

        // The baseline handed in was measured before the first candidate. It is only valid as a
        // before-state until the machine changes or enough time passes for it to drift, so it is
        // consumed once and then re-measured per candidate.
        CaptureAnalysis? current = baseline;
        var restartRequired = false;

        foreach (var card in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var measurable = !card.Definition.RequiresReboot;

            // Measure the before-state immediately before the change, not several candidates ago.
            // Repeated runs on an untouched machine trend upward as it warms, so a baseline carried
            // forward puts all the accumulated drift inside the last comparison and biases late
            // candidates toward a regression they did not cause. The cost is one extra benchmark
            // run per candidate; the alternative is a verdict that depends on queue position.
            if (measurable && current is null)
            {
                progress?.Report($"Re-measuring the baseline before {card.Definition.Title}.");
                current = _benchmark.Run(cancellationToken);
                if (current.Outcome == ResultOutcome.Invalid)
                {
                    steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title, null, null, false,
                        $"Not applied: the baseline re-measurement failed. {FirstWarning(current)}"));
                    break;
                }
            }

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
                current = null;
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, null, true,
                    "Applied and kept unmeasured: it takes effect after a restart. Re-run to measure it."));
                continue;
            }

            progress?.Report($"Measuring {card.Definition.Title}.");
            var after = _benchmark.Run(cancellationToken);
            var verification = TweakVerifier.Compare(current!, after, transaction);

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

            // A recommended default that measured as no change is kept, not reverted.
            //
            // These are not frame-rate claims, so "the benchmark could not see it" is not evidence
            // against them. Pointer acceleration is the case that makes it obvious: the benchmark
            // never moves the mouse, so acceleration can only ever measure as no change — and
            // reverting on that verdict turns acceleration back on, which is the opposite of what
            // the policy says and worse for aim. Capture, telemetry and desktop transparency are
            // the same shape. A default that actually *regressed* the tails is still reverted
            // below, because that is a measurement the workload genuinely made.
            if (verification.Verdict == VerificationVerdict.NoMeasuredChange
                && card.Definition.Disposition == TweakDisposition.RecommendDefault)
            {
                current = null;
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, verification, true,
                    "Kept — no measured frame-time effect, which is expected: this is a recommended default "
                    + "held on its documented behaviour rather than on a frame-rate claim."));
                continue;
            }

            if (verification.ShouldRevert)
            {
                var reverted = _engine.Revert(transaction.TransactionId, $"autotune: {verification.Verdict}");

                // The revert puts the configuration back, but two benchmark runs have passed since
                // this before-state was captured. Re-measure rather than reuse it.
                current = null;
                steps.Add(new AutoTuneStep(card.Definition.Id, card.Definition.Title,
                    transaction.TransactionId, verification, false,
                    $"Reverted — {verification.Finding} "
                    + (reverted.State == TweakTransaction.StateReverted
                        ? "Every value was restored."
                        : "Review the restore detail.")));
                continue;
            }

            // The machine now differs from the state this pair measured, and the next candidate
            // gets its own adjacent before-state rather than inheriting this one.
            current = null;
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

        // The experiments did not earn their place. Reversing all of them is the only defensible
        // response, because a bundled measurement cannot say which member was responsible.
        progress?.Report(verification.Verdict == VerificationVerdict.NotComparable
            ? "The measurement could not be compared. Reverting the experiments."
            : "The set did not improve the measurement. Reverting the experiments.");

        // Recommended defaults are exempt for the same reason they are exempt in isolate mode: they
        // are not frame-rate claims, so "the benchmark could not see it" is not evidence against
        // them. Rolling pointer acceleration back on because a benchmark that never moves the mouse
        // measured no change would be the tool actively making the machine worse.
        //
        // The exemption covers that verdict only. If the set actually regressed the tails, or the
        // captures could not be compared at all, everything goes back — a bundled run cannot say
        // which member was responsible, so no member gets the benefit of the doubt.
        var defaults = verification.Verdict == VerificationVerdict.NoMeasuredChange
            ? candidates
                .Where(card => card.Definition.Disposition == TweakDisposition.RecommendDefault)
                .Select(card => card.Definition.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var updated = new List<AutoTuneStep>();
        foreach (var step in steps)
        {
            if (step.TransactionId is null)
            {
                updated.Add(step);
                continue;
            }

            if (defaults.Contains(step.TweakId))
            {
                updated.Add(step with
                {
                    Verification = verification,
                    Outcome = "Kept: a recommended default, held on its documented behaviour rather than "
                              + "judged by a frame-time measurement it does not claim to move."
                });
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

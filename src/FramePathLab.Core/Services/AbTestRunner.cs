using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

public sealed record AbTestReport(
    string TweakId,
    string TweakTitle,
    int PairsRun,
    IReadOnlyList<AbTestResult> Results,
    bool Recommended,
    string Verdict,
    string Detail);

/// <summary>
/// Drives a properly paired comparison of one change: apply, measure, revert, measure, repeated in
/// a balanced order until the answer settles or the pair budget runs out.
///
/// This is the difference between "we changed something and the number moved" and knowing whether
/// the change caused the movement. It costs one benchmark run per measurement — several minutes for
/// a single tweak — which is why it is a deliberate mode rather than the default. What it buys is a
/// result that survives being questioned.
/// </summary>
public sealed class AbTestRunner
{
    private readonly ExpertTweakEngine _engine;
    private readonly IBenchmarkRunner _benchmark;

    /// <summary>
    /// The metrics a verdict is drawn from, tails first. Mean frame rate is measured and reported
    /// but never decides the outcome — a change that lifts the average while widening the tail is
    /// worse in a match, and judging on the average is how that gets shipped as an improvement.
    /// </summary>
    private static readonly (string Id, string Label, bool LowerIsBetter)[] Metrics =
    [
        ("p99_frame_ms", "P99 frame time", true),
        ("p95_frame_ms", "P95 frame time", true),
        ("frame_stddev", "Frame-time consistency", true),
        ("median_frame_ms", "Median frame time", true),
        ("mean_fps", "Mean frame rate", false)
    ];

    public AbTestRunner(ExpertTweakEngine engine, IBenchmarkRunner benchmark)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _benchmark = benchmark ?? throw new ArgumentNullException(nameof(benchmark));
    }

    public AbTestReport Run(
        ExpertTweakCard card,
        int maximumPairs = 5,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!card.CanApply)
        {
            return new AbTestReport(card.Definition.Id, card.Definition.Title, 0, [], false,
                "Not testable", card.BlockedReason ?? "This change is already at its recommended value.");
        }

        if (card.Definition.RequiresReboot)
        {
            return new AbTestReport(card.Definition.Id, card.Definition.Title, 0, [], false,
                "Not testable in one session",
                "This change only takes effect after a restart, so it cannot be toggled between "
                + "measurements. Apply it, restart, and compare against a baseline captured beforehand.");
        }

        var withoutChange = new List<CaptureAnalysis>();
        var withChange = new List<CaptureAnalysis>();
        var applied = false;
        Guid? transactionId = null;

        try
        {
            for (var pair = 0; pair < maximumPairs; pair++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var wantChangeApplied in PairSchedule(pair))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (wantChangeApplied && !applied)
                    {
                        transactionId = _engine.Apply(card).TransactionId;
                        applied = true;
                    }
                    else if (!wantChangeApplied && applied && transactionId is not null)
                    {
                        _engine.Revert(transactionId.Value, "A/B test: measuring without the change");
                        applied = false;
                        transactionId = null;
                    }

                    progress?.Report(
                        $"Pair {pair + 1}/{maximumPairs}: measuring "
                        + (wantChangeApplied ? "with the change." : "without the change."));

                    var measurement = _benchmark.Run(cancellationToken);
                    if (measurement.Outcome == ResultOutcome.Invalid)
                    {
                        return Abort(card, withoutChange.Count,
                            "A measurement failed, so the comparison was abandoned and the change reversed.");
                    }

                    (wantChangeApplied ? withChange : withoutChange).Add(measurement);
                }

                var interim = Evaluate(withoutChange, withChange);
                var leading = interim.FirstOrDefault();
                if (leading is not null
                    && !PairedAbTest.ShouldContinue(leading, maximumPairs)
                    && pair + 1 >= 3)
                {
                    progress?.Report($"Settled after {pair + 1} pairs.");
                    break;
                }
            }

            var results = Evaluate(withoutChange, withChange);
            return Decide(card, results, withoutChange.Count, ref applied, ref transactionId);
        }
        finally
        {
            // Whatever happened, the machine must not be left holding a change the caller did not
            // ask to keep.
            if (applied && transactionId is not null)
            {
                _engine.Revert(transactionId.Value, "A/B test: restoring after the comparison");
            }
        }
    }

    /// <summary>Balanced within each pair, and reversed on alternate pairs so time cancels.</summary>
    private static IReadOnlyList<bool> PairSchedule(int pairIndex)
        => pairIndex % 2 == 0 ? [false, true] : [true, false];

    private static IReadOnlyList<AbTestResult> Evaluate(
        IReadOnlyList<CaptureAnalysis> withoutChange,
        IReadOnlyList<CaptureAnalysis> withChange)
    {
        var count = Math.Min(withoutChange.Count, withChange.Count);
        var results = new List<AbTestResult>();

        foreach (var (id, label, lowerIsBetter) in Metrics)
        {
            var pairs = new List<AbPair>(count);
            for (var index = 0; index < count; index++)
            {
                var before = Find(withoutChange[index], id);
                var after = Find(withChange[index], id);
                if (before is > 0 && after is > 0)
                {
                    pairs.Add(new AbPair(index, before.Value, after.Value));
                }
            }

            if (pairs.Count > 0)
            {
                results.Add(PairedAbTest.Evaluate(id, label, lowerIsBetter, pairs));
            }
        }

        return results;
    }

    private AbTestReport Decide(
        ExpertTweakCard card,
        IReadOnlyList<AbTestResult> results,
        int pairs,
        ref bool applied,
        ref Guid? transactionId)
    {
        var tails = results.Where(result =>
            result.MetricId is "p99_frame_ms" or "p95_frame_ms" or "frame_stddev").ToArray();

        var improvements = tails.Count(result => result.Conclusive && result.IsImprovement);
        var regressions = tails.Count(result => result.Conclusive && !result.IsImprovement);

        var detail = string.Join(" ", results.Select(result => result.Finding));

        if (regressions > improvements)
        {
            return new AbTestReport(card.Definition.Id, card.Definition.Title, pairs, results, false,
                "Regression", detail);
        }

        if (improvements > 0)
        {
            // Worth keeping, so it is left applied rather than reverted on the way out.
            if (!applied)
            {
                transactionId = _engine.Apply(card).TransactionId;
                applied = true;
            }

            var keptTransaction = transactionId;
            applied = false;
            transactionId = null;

            return new AbTestReport(card.Definition.Id, card.Definition.Title, pairs, results, true,
                "Improvement",
                detail + $" Kept as transaction {keptTransaction:D}.");
        }

        return new AbTestReport(card.Definition.Id, card.Definition.Title, pairs, results, false,
            "No measurable effect",
            detail + " Nothing was kept: a change that cannot be measured is not worth the configuration drift.");
    }

    private AbTestReport Abort(ExpertTweakCard card, int pairs, string reason)
        => new(card.Definition.Id, card.Definition.Title, pairs, [], false, "Abandoned", reason);

    private static double? Find(CaptureAnalysis analysis, string metricId)
        => analysis.Metrics.FirstOrDefault(metric => metric.Id == metricId)?.Value;
}

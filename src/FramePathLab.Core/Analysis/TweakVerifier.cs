using FramePathLab.Core.Models;

namespace FramePathLab.Core.Analysis;

/// <summary>
/// Compares a capture taken before a change against one taken after it.
///
/// This is the part a tutorial cannot do. Anyone can list registry values; nobody watching a video
/// can tell whether the values did anything on <em>their</em> machine, so the honest answer to
/// "did that help?" has always been a shrug and a placebo. Measuring the same scenario either side
/// of one recorded change replaces the shrug with a number.
///
/// What it deliberately does not do is claim causation from a single pair. Two runs differ for
/// reasons that have nothing to do with the change, so a difference inside the noise band is
/// reported as inconclusive rather than as a result, and the verdict names how confident it is.
/// </summary>
public static class TweakVerifier
{
    /// <summary>
    /// Run-to-run variation on an identical configuration routinely reaches a couple of percent.
    /// Movement smaller than this is not evidence of anything.
    /// </summary>
    private const double NoiseBandPercent = 2.0;

    /// <summary>Below this many frames a percentile is too unstable to compare.</summary>
    private const long MinimumFramesForComparison = 3000;

    public static TweakVerification Compare(
        CaptureAnalysis before,
        CaptureAnalysis after,
        TweakTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var blocker = FindComparabilityProblem(before, after);
        if (blocker is not null)
        {
            return new TweakVerification(
                transaction?.TransactionId,
                transaction?.TweakId ?? "unattributed",
                before.SourceFileName,
                after.SourceFileName,
                [],
                VerificationVerdict.NotComparable,
                blocker,
                "Re-capture both runs under the same conditions before drawing any conclusion.");
        }

        var deltas = new List<MetricDelta>();
        foreach (var (id, label, lowerIsBetter) in ComparedMetrics)
        {
            var beforeValue = Find(before, id);
            var afterValue = Find(after, id);
            if (beforeValue is null || afterValue is null || beforeValue.Value == 0)
            {
                continue;
            }

            var changePercent = 100d * (afterValue.Value - beforeValue.Value) / Math.Abs(beforeValue.Value);
            var improved = lowerIsBetter ? changePercent < 0 : changePercent > 0;

            // A metric sitting near zero on both sides has not moved in any way worth acting on,
            // however large the ratio between the two looks.
            var belowFloor = NearZeroFloors.TryGetValue(id, out var floor)
                             && Math.Abs(beforeValue.Value) < floor
                             && Math.Abs(afterValue.Value) < floor;
            var meaningful = !belowFloor && Math.Abs(changePercent) >= NoiseBandPercent;

            deltas.Add(new MetricDelta(
                id,
                label,
                beforeValue.Value,
                afterValue.Value,
                changePercent,
                lowerIsBetter,
                meaningful && improved,
                meaningful && !improved,
                meaningful));
        }

        if (deltas.Count == 0)
        {
            return new TweakVerification(
                transaction?.TransactionId,
                transaction?.TweakId ?? "unattributed",
                before.SourceFileName,
                after.SourceFileName,
                [],
                VerificationVerdict.NotComparable,
                "Neither capture carried a metric that both runs share.",
                "Capture both runs with the same collector and column set.");
        }

        return BuildVerdict(before, after, deltas, transaction);
    }

    private static TweakVerification BuildVerdict(
        CaptureAnalysis before,
        CaptureAnalysis after,
        List<MetricDelta> deltas,
        TweakTransaction? transaction)
    {
        // The tails carry the weight. A change that lifts the average while making the worst
        // frames worse is a change a competitive player should reject, so the decision is driven
        // by the percentile and consistency metrics rather than by mean frame rate.
        //
        // The tail metrics are weighed by how far they moved, not counted. Counting them let two
        // marginal 2.1% improvements outvote a 20% P99 regression and return "improved" — the exact
        // reading this tool exists to refuse. P99 carries the most weight because it is the frame
        // a player actually feels in a duel.
        var tails = deltas.Where(delta => TailWeights.ContainsKey(delta.MetricId)).ToArray();

        var improvedWeight = tails.Where(delta => delta.IsImprovement)
            .Sum(delta => TailWeights[delta.MetricId] * Math.Abs(delta.ChangePercent));
        var regressedWeight = tails.Where(delta => delta.IsRegression)
            .Sum(delta => TailWeights[delta.MetricId] * Math.Abs(delta.ChangePercent));

        var improvedTails = tails.Count(delta => delta.IsImprovement);
        var regressedTails = tails.Count(delta => delta.IsRegression);
        var anyMeaningful = deltas.Any(delta => delta.IsMeaningful);

        // A weighted lead of less than this much is treated as a tie rather than a verdict.
        const double DecisiveWeightRatio = 1.25;

        if (!anyMeaningful)
        {
            return new TweakVerification(
                transaction?.TransactionId,
                transaction?.TweakId ?? "unattributed",
                before.SourceFileName,
                after.SourceFileName,
                deltas,
                VerificationVerdict.NoMeasuredChange,
                $"Every compared metric moved by less than {NoiseBandPercent:0.#}%, which is inside normal "
                + "run-to-run variation.",
                "This change did nothing measurable on this machine in this scenario. Revert it and spend the "
                + "attention elsewhere, or repeat the pair if you believe the scenario was not representative.");
        }

        if (regressedWeight > improvedWeight * DecisiveWeightRatio)
        {
            return new TweakVerification(
                transaction?.TransactionId,
                transaction?.TweakId ?? "unattributed",
                before.SourceFileName,
                after.SourceFileName,
                deltas,
                VerificationVerdict.Regressed,
                $"{regressedTails} frame-time consistency metric(s) got worse against {improvedTails} improved.",
                transaction is not null
                    ? "Revert this transaction. A change that worsens the tails is worse in a match even if the "
                      + "average frame rate looks better."
                    : "Undo the change. A change that worsens the tails is worse in a match even if the average "
                      + "frame rate looks better.");
        }

        if (improvedWeight > regressedWeight * DecisiveWeightRatio)
        {
            return new TweakVerification(
                transaction?.TransactionId,
                transaction?.TweakId ?? "unattributed",
                before.SourceFileName,
                after.SourceFileName,
                deltas,
                VerificationVerdict.Improved,
                $"{improvedTails} frame-time consistency metric(s) improved against {regressedTails} worse.",
                "Worth keeping, but one pair of runs is not proof. Repeat the pair at least twice more before "
                + "treating this as settled, and keep the scenario identical each time.");
        }

        return new TweakVerification(
            transaction?.TransactionId,
            transaction?.TweakId ?? "unattributed",
            before.SourceFileName,
            after.SourceFileName,
            deltas,
            VerificationVerdict.Mixed,
            "Some metrics improved and others regressed by a similar amount.",
            "Inconclusive. Repeat the pair, and if it stays mixed, revert — an ambiguous change is not worth "
            + "the configuration drift it costs.");
    }

    /// <summary>
    /// Two captures are only comparable when they describe the same thing. Different applications,
    /// a rejected capture or too few frames make any delta meaningless, so they are refused rather
    /// than reported with a caveat nobody reads.
    /// </summary>
    private static string? FindComparabilityProblem(CaptureAnalysis before, CaptureAnalysis after)
    {
        if (before.Outcome == ResultOutcome.Invalid || after.Outcome == ResultOutcome.Invalid)
        {
            return "At least one capture was rejected by the parser, so there is nothing valid to compare.";
        }

        if (!string.Equals(before.SelectedApplication, after.SelectedApplication, StringComparison.OrdinalIgnoreCase))
        {
            return $"The captures target different applications ({before.SelectedApplication} against "
                   + $"{after.SelectedApplication}).";
        }

        if (before.AcceptedRows < MinimumFramesForComparison || after.AcceptedRows < MinimumFramesForComparison)
        {
            return $"A comparison needs at least {MinimumFramesForComparison:N0} frames on each side; this pair "
                   + $"has {before.AcceptedRows:N0} and {after.AcceptedRows:N0}.";
        }

        if (string.Equals(before.SourceSha256, after.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            return "Both sides are the same file, so the comparison would be against itself.";
        }

        return null;
    }

    /// <summary>
    /// How much each tail metric counts toward the verdict. P99 is what a player feels in a duel;
    /// P99.9 is rarer and noisier; the consistency and over-budget figures are supporting evidence
    /// that largely restates what the percentiles already said.
    /// </summary>
    private static readonly Dictionary<string, double> TailWeights = new(StringComparer.Ordinal)
    {
        ["p99_frame_ms"] = 3.0,
        ["p999_frame_ms"] = 1.5,
        ["frame_stddev"] = 1.0,
        ["over_budget_pct"] = 1.0
    };

    /// <summary>
    /// Metrics whose baseline can sit near zero, with the floor below which a relative change says
    /// nothing.
    ///
    /// Every metric here is judged as a percentage of its own before-value. That is meaningless
    /// when the before-value is already almost nothing: on a machine holding its frame budget,
    /// 0.05% of frames over budget becoming 0.06% is a 20% "regression" and a 0.01 percentage
    /// point change. Left unguarded it outvotes real movement in the percentiles.
    /// </summary>
    private static readonly Dictionary<string, double> NearZeroFloors = new(StringComparer.Ordinal)
    {
        ["over_budget_pct"] = 0.5
    };

    private static readonly (string Id, string Label, bool LowerIsBetter)[] ComparedMetrics =
    [
        ("median_frame_ms", "Median frame time", true),
        ("p95_frame_ms", "P95 frame time", true),
        ("p99_frame_ms", "P99 frame time", true),
        ("p999_frame_ms", "P99.9 frame time", true),
        ("frame_stddev", "Frame-time consistency", true),
        ("over_budget_pct", "Frames over budget", true),
        ("mean_fps", "Mean frame rate", false)
    ];

    private static double? Find(CaptureAnalysis analysis, string metricId)
        => analysis.Metrics.FirstOrDefault(metric => metric.Id == metricId)?.Value;
}

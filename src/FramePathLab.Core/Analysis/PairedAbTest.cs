using FramePathLab.Core.Models;

namespace FramePathLab.Core.Analysis;

/// <summary>One measured pair: the same metric with the change off and on.</summary>
public sealed record AbPair(int Index, double WithoutChange, double WithChange)
{
    public double Difference => WithChange - WithoutChange;

    public double PercentChange => WithoutChange == 0 ? 0 : 100d * Difference / Math.Abs(WithoutChange);
}

public sealed record AbTestResult(
    string MetricId,
    string MetricLabel,
    bool LowerIsBetter,
    IReadOnlyList<AbPair> Pairs,
    double MeanPercentChange,
    double ConfidenceLowPercent,
    double ConfidenceHighPercent,
    bool Conclusive,
    bool IsImprovement,
    string Finding)
{
    public int PairCount => Pairs.Count;
}

/// <summary>
/// Compares a change against its absence using interleaved, paired measurements.
///
/// Two problems make the obvious approach — measure, change, measure — unreliable, and both were
/// visible in this project's own benchmark before this existed.
///
/// The first is drift. Repeated runs on an untouched machine trend rather than scatter: clocks
/// settle, caches warm, temperature rises, background work comes and goes. Measuring every "before"
/// first and every "after" second puts the entire drift inside the comparison, where it is
/// indistinguishable from the change. Interleaving in a balanced order — off, on, on, off — gives
/// both conditions the same average position in time, so a linear trend cancels instead of being
/// attributed to the change.
///
/// The second is that a single pair has no error bar. One difference cannot say whether it exceeds
/// what two identical runs would have produced anyway. Several pairs give a distribution of
/// differences, and the interval around their mean is what separates a real effect from a run of
/// luck. The comparison is paired rather than pooled, because each pair shares its own conditions:
/// what matters is the difference within a pair, not the spread across all runs.
/// </summary>
public static class PairedAbTest
{
    /// <summary>
    /// A difference smaller than this is not worth acting on even when it is statistically real.
    /// Measured run-to-run spread on an idle machine sits near two percent, so this is the floor
    /// below which the result is indistinguishable from the machine's own variation.
    /// </summary>
    public const double PracticalThresholdPercent = 2.0;

    /// <summary>
    /// Two-sided ninety-five percent critical values by degrees of freedom. Small-sample work
    /// needs the t distribution rather than a normal approximation: at three pairs the difference
    /// between the two is large enough to turn an inconclusive result into a false positive.
    /// </summary>
    private static readonly double[] CriticalValues =
    [
        0, 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228,
        2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086
    ];

    private static double CriticalValue(int degreesOfFreedom)
        => degreesOfFreedom <= 0
            ? 0
            : degreesOfFreedom < CriticalValues.Length
                ? CriticalValues[degreesOfFreedom]
                : 1.96;

    /// <summary>
    /// The order measurements should be taken in, so a linear trend over the session cancels.
    /// Alternating on and off leaves the "on" condition systematically later; a balanced pattern
    /// does not.
    /// </summary>
    public static IReadOnlyList<bool> BuildSchedule(int pairs)
    {
        var schedule = new List<bool>(pairs * 2);
        for (var pair = 0; pair < pairs; pair++)
        {
            // Off, on, on, off — reversing every other pair balances position in time.
            if (pair % 2 == 0)
            {
                schedule.Add(false);
                schedule.Add(true);
            }
            else
            {
                schedule.Add(true);
                schedule.Add(false);
            }
        }

        return schedule;
    }

    public static AbTestResult Evaluate(
        string metricId,
        string metricLabel,
        bool lowerIsBetter,
        IReadOnlyList<AbPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count < 2)
        {
            return new AbTestResult(metricId, metricLabel, lowerIsBetter, pairs, 0, 0, 0, false, false,
                $"{pairs.Count} pair(s) measured; at least two are needed before a difference has an error bar.");
        }

        var differences = pairs.Select(pair => pair.PercentChange).ToArray();
        var mean = differences.Average();

        var sumSquares = differences.Sum(value => (value - mean) * (value - mean));
        var standardDeviation = Math.Sqrt(sumSquares / (differences.Length - 1));
        var standardError = standardDeviation / Math.Sqrt(differences.Length);
        var margin = CriticalValue(differences.Length - 1) * standardError;

        var low = mean - margin;
        var high = mean + margin;

        // Conclusive means the whole interval sits on one side of the practical threshold. An
        // interval straddling it says the measurement cannot tell, which is a real answer.
        //
        // The weaker rule this replaced — interval excludes zero, point estimate past the threshold
        // — called a mean of 2.1% with an interval of 0.3% to 3.9% conclusive, even though most of
        // that interval lies below the threshold the tool says is worth acting on. Judging the
        // interval rather than the point estimate is what the comment always claimed.
        var improvedDirection = lowerIsBetter ? mean < 0 : mean > 0;
        var intervalExcludesZero = low > 0 || high < 0;
        var conclusive = low > PracticalThresholdPercent || high < -PracticalThresholdPercent;

        var finding = conclusive
            ? $"{metricLabel} moved {mean:+0.##;-0.##}% across {pairs.Count} pairs "
              + $"(95% interval {low:+0.##;-0.##}% to {high:+0.##;-0.##}%), which is "
              + (improvedDirection ? "an improvement." : "a regression.")
            : intervalExcludesZero
                ? $"{metricLabel} moved {mean:+0.##;-0.##}% — consistent, but below the "
                  + $"{PracticalThresholdPercent:0.#}% worth acting on."
                : $"{metricLabel} moved {mean:+0.##;-0.##}% across {pairs.Count} pairs, but the 95% interval "
                  + $"({low:+0.##;-0.##}% to {high:+0.##;-0.##}%) includes zero. The measurement cannot "
                  + "distinguish this from run-to-run variation.";

        return new AbTestResult(
            metricId, metricLabel, lowerIsBetter, pairs, mean, low, high,
            conclusive, conclusive && improvedDirection, finding);
    }

    /// <summary>
    /// The fewest pairs that may end a run on a positive verdict.
    ///
    /// Stopping as soon as the interval looks convincing is optional stopping: the interval is
    /// re-examined after every pair, and each extra look is another chance for noise alone to
    /// produce one that clears the bar. The nominal 95% is not preserved under repeated peeking,
    /// so the real false-positive rate sits above the stated one. Two things hold that down here —
    /// the conclusive rule requires the whole interval to clear the practical threshold rather
    /// than merely exclude zero, and a positive verdict may not end the run before this many
    /// pairs. Ruling an effect *out* early is not restricted, because stopping early there costs
    /// a false negative rather than a false claim.
    /// </summary>
    private const int MinimumPairsForEarlyStop = 4;

    /// <summary>
    /// Whether more pairs would plausibly settle the question, used to stop early rather than
    /// always paying for the maximum. Stopping once the answer is clear is not a shortcut: extra
    /// pairs on a settled result cost time and add thermal drift.
    /// </summary>
    public static bool ShouldContinue(AbTestResult result, int maximumPairs)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.PairCount >= maximumPairs)
        {
            return false;
        }

        if (result.PairCount < 3)
        {
            return true;
        }

        // Settled in either direction: a clear effect, or an interval tight enough around zero that
        // a meaningful effect has been ruled out.
        var ruledOut = Math.Abs(result.ConfidenceLowPercent) < PracticalThresholdPercent
                       && Math.Abs(result.ConfidenceHighPercent) < PracticalThresholdPercent;
        if (ruledOut)
        {
            return false;
        }

        // A positive verdict has to survive one more pair than the bare minimum before it may end
        // the run, so a single lucky interval at three pairs cannot close the question.
        return !(result.Conclusive && result.PairCount >= MinimumPairsForEarlyStop);
    }
}

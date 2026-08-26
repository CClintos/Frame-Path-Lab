using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;

namespace FramePathLab.Core.Analysis;

/// <summary>
/// Turns a capture into statements about the presentation path.
///
/// This is the only place in FramePath Lab that can say what actually happened rather than what a
/// setting claims. A settings read says vertical sync is off; the sync interval in the capture says
/// whether it was. A checklist says to use exclusive fullscreen; the present mode says whether the
/// frames reached an independent flip.
/// </summary>
public static class FrameDeliveryAnalyzer
{
    // Present modes that reach the display without going through desktop composition.
    private static readonly string[] IndependentFlipModes =
    [
        "Hardware: Independent Flip",
        "Hardware Composed: Independent Flip",
        "Hardware: Legacy Flip"
    ];

    public static IReadOnlyList<FrameDeliveryFinding> Analyze(
        IReadOnlyDictionary<string, long> presentModeCounts,
        IReadOnlyList<double> frameTimes,
        IReadOnlyList<double> cpuBusy,
        IReadOnlyList<double> gpuBusy,
        IReadOnlyList<double> syncIntervals,
        IReadOnlyList<double> untilDisplayed,
        IReadOnlyList<double> renderPresentLatency,
        long droppedFrames)
    {
        ArgumentNullException.ThrowIfNull(presentModeCounts);
        ArgumentNullException.ThrowIfNull(frameTimes);

        var findings = new List<FrameDeliveryFinding>();
        AddPresentPath(findings, presentModeCounts);
        AddSyncInterval(findings, syncIntervals);
        AddBoundClassification(findings, cpuBusy, gpuBusy);
        AddPacing(findings, frameTimes);
        AddLatencyChain(findings, untilDisplayed, renderPresentLatency);
        AddDropped(findings, droppedFrames, frameTimes.Count);
        return findings;
    }

    private static void AddPresentPath(List<FrameDeliveryFinding> findings, IReadOnlyDictionary<string, long> counts)
    {
        var total = counts.Values.Sum();
        if (total == 0)
        {
            return;
        }

        var independent = counts
            .Where(entry => IndependentFlipModes.Any(mode =>
                entry.Key.Contains(mode, StringComparison.OrdinalIgnoreCase)))
            .Sum(entry => entry.Value);
        var share = 100d * independent / total;
        var dominant = counts.MaxBy(entry => entry.Value).Key;

        if (share >= 95)
        {
            findings.Add(new FrameDeliveryFinding(
                "PRESENT-PATH",
                "Presentation path",
                DeliverySeverity.Good,
                $"{share:0.#}% of frames on an independent flip path ({dominant}).",
                "Frames are reaching the display without passing through desktop composition, which is the "
                + "lowest-latency path available.",
                "No change needed. Re-check this after any display, overlay or HDR change."));
            return;
        }

        findings.Add(new FrameDeliveryFinding(
            "PRESENT-PATH",
            "Presentation path",
            DeliverySeverity.Costly,
            $"Only {share:0.#}% of frames reached an independent flip; the dominant mode was {dominant}.",
            "Composed present modes route frames through the desktop window manager, which typically adds "
            + "about one refresh interval of latency versus an independent flip. This is usually worth more "
            + "than any registry-level tweak on the expert list.",
            "Work through the usual causes one at a time: an overlay or capture tool attached to the game, "
            + "HDR enabled on the output, a second display with a mismatched refresh rate, or windowed "
            + "presentation without the windowed-optimisation path. Re-capture after each change."));
    }

    private static void AddSyncInterval(List<FrameDeliveryFinding> findings, IReadOnlyList<double> syncIntervals)
    {
        if (syncIntervals.Count == 0)
        {
            return;
        }

        var synced = syncIntervals.Count(value => value >= 1);
        var share = 100d * synced / syncIntervals.Count;
        var on = share >= 50;

        findings.Add(new FrameDeliveryFinding(
            "SYNC-INTERVAL",
            "Vertical sync, measured",
            on ? DeliverySeverity.Advisory : DeliverySeverity.Good,
            on
                ? $"{share:0.#}% of presents used a non-zero sync interval."
                : $"{100 - share:0.#}% of presents used a sync interval of zero.",
            on
                ? "Vertical sync was engaged during this capture. Combined with a frame rate at the refresh "
                  + "ceiling this queues frames and adds latency; combined with variable refresh and a cap "
                  + "below the ceiling it does not."
                : "Presents were unsynchronised, which is the lowest-latency configuration and permits tearing.",
            on
                ? "Confirm a frame cap sits below the refresh ceiling, or turn vertical sync off if tearing is acceptable."
                : "No change needed. This is measured from the capture, not read from game configuration."));
    }

    private static void AddBoundClassification(
        List<FrameDeliveryFinding> findings,
        IReadOnlyList<double> cpuBusy,
        IReadOnlyList<double> gpuBusy)
    {
        if (cpuBusy.Count == 0 || gpuBusy.Count == 0)
        {
            return;
        }

        var cpu = DescriptiveStatistics.QuantileR7(cpuBusy.Order().ToArray(), 0.5);
        var gpu = DescriptiveStatistics.QuantileR7(gpuBusy.Order().ToArray(), 0.5);
        if (cpu <= 0 && gpu <= 0)
        {
            return;
        }

        // A 15% margin keeps a genuinely balanced workload out of both buckets rather than forcing
        // it into whichever side happens to be marginally higher.
        const double margin = 1.15;
        var (bound, meaning, next) = cpu > gpu * margin
            ? ("CPU-bound",
                "The processor is the limiting stage for most frames.",
                "Prioritise the CPU entries: core placement, power policy floors, scheduler punctuality. "
                + "Lowering graphics settings will not raise frame rate in this state.")
            : gpu > cpu * margin
                ? ("GPU-bound",
                    "The graphics processor is the limiting stage for most frames.",
                    "Prioritise resolution, graphics settings and the GPU clock-limiter entries. CPU affinity "
                    + "and scheduler tweaks have little headroom to recover here.")
                : ("Balanced",
                    "Neither stage dominates; the workload alternates between them.",
                    "Change one variable at a time and re-capture, because either side can become the limit.");

        findings.Add(new FrameDeliveryFinding(
            "BOUND-CLASS",
            "Limiting stage",
            DeliverySeverity.Advisory,
            $"{bound}: median CPU busy {cpu:0.###} ms against median GPU busy {gpu:0.###} ms.",
            meaning,
            next));
    }

    private static void AddPacing(List<FrameDeliveryFinding> findings, IReadOnlyList<double> frameTimes)
    {
        if (frameTimes.Count < 100)
        {
            return;
        }

        var sorted = frameTimes.Order().ToArray();
        var median = DescriptiveStatistics.QuantileR7(sorted, 0.5);
        if (median <= 0)
        {
            return;
        }

        // Percentiles of frame time hide cadence: a run can post an excellent P99 and still feel
        // broken if consecutive frames alternate long and short. The delta series exposes that.
        var deltas = new double[frameTimes.Count - 1];
        for (var index = 1; index < frameTimes.Count; index++)
        {
            deltas[index - 1] = Math.Abs(frameTimes[index] - frameTimes[index - 1]);
        }

        Array.Sort(deltas);
        var deltaP99 = DescriptiveStatistics.QuantileR7(deltas, 0.99);
        var spikes = sorted.Count(value => value > median * 2);
        var spikeShare = 100d * spikes / sorted.Length;

        var poor = spikeShare > 1.0 || deltaP99 > median;
        findings.Add(new FrameDeliveryFinding(
            "PACING",
            "Frame pacing",
            poor ? DeliverySeverity.Costly : DeliverySeverity.Good,
            $"{spikeShare:0.##}% of frames exceeded twice the median; P99 frame-to-frame change {deltaP99:0.###} ms "
            + $"against a {median:0.###} ms median.",
            poor
                ? "The cadence is uneven. This is what players report as stutter even when average frame rate "
                  + "and percentile frame times look healthy."
                : "Frame delivery is evenly paced.",
            poor
                ? "Check thread wake-up punctuality and the presentation path before adjusting game settings."
                : "No change needed."));
    }

    private static void AddLatencyChain(
        List<FrameDeliveryFinding> findings,
        IReadOnlyList<double> untilDisplayed,
        IReadOnlyList<double> renderPresentLatency)
    {
        if (untilDisplayed.Count == 0 && renderPresentLatency.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        double? displayed = null;
        if (untilDisplayed.Count > 0)
        {
            displayed = DescriptiveStatistics.QuantileR7(untilDisplayed.Order().ToArray(), 0.5);
            parts.Add($"median present-to-display {displayed:0.###} ms");
        }

        if (renderPresentLatency.Count > 0)
        {
            var render = DescriptiveStatistics.QuantileR7(renderPresentLatency.Order().ToArray(), 0.5);
            parts.Add($"median render-to-present {render:0.###} ms");
        }

        findings.Add(new FrameDeliveryFinding(
            "LATENCY-CHAIN",
            "Presentation latency chain",
            DeliverySeverity.Advisory,
            string.Join("; ", parts) + ".",
            "These are the collector's software timing stages. They measure the render and present path only, "
            + "and are not a mouse-to-photon measurement.",
            displayed is > 0
                ? "Compare this figure between two configurations rather than reading it as an absolute latency."
                : "Capture with a collector that emits display timing to compare configurations."));
    }

    private static void AddDropped(List<FrameDeliveryFinding> findings, long dropped, int accepted)
    {
        if (dropped <= 0 || accepted <= 0)
        {
            return;
        }

        var share = 100d * dropped / (dropped + accepted);
        findings.Add(new FrameDeliveryFinding(
            "DROPPED",
            "Dropped presents",
            share > 0.5 ? DeliverySeverity.Costly : DeliverySeverity.Advisory,
            $"{dropped:N0} presents were dropped ({share:0.##}% of the capture).",
            "A dropped present is rendered work that never reached the display. It costs latency without "
            + "producing a visible frame.",
            "Dropped presents commonly accompany a composed present path or a frame rate above the refresh "
            + "ceiling with sync engaged. Resolve the present path first."));
    }
}

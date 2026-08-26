using System.Security.Cryptography;
using System.Text;
using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;

namespace FramePathLab.Windows.Benchmark;

/// <summary>
/// Turns a benchmark run into the same shape an imported capture produces, so a self-run result and
/// a real capture go through one comparison path rather than two.
/// </summary>
public static class BenchmarkAnalysis
{
    public const string SourceApplication = "framepathlab-benchmark";

    public static CaptureAnalysis ToAnalysis(BenchmarkResult result, string label)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (!result.Succeeded || result.FrameTimes.Count == 0)
        {
            return new CaptureAnalysis(
                DateTimeOffset.UtcNow, label, string.Empty, 0, "benchmark-v1",
                SourceApplication, "FrameTime", 0, 0, 0, ResultOutcome.Invalid,
                [], new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
                [result.Observation]);
        }

        var sorted = result.FrameTimes.Order().ToArray();
        var mean = DescriptiveStatistics.Mean(sorted);
        var metrics = new List<MetricSummary>
        {
            new("frames", "Valid frames", sorted.Length, "frames", "Measured frames after warmup.", "Available"),
            new("mean_frame_ms", "Mean frame time", mean, "ms", "Arithmetic mean.", "Available"),
            new("median_frame_ms", "Median frame time", DescriptiveStatistics.QuantileR7(sorted, 0.5), "ms", "R-7 50th percentile.", "Available"),
            new("p95_frame_ms", "P95 frame time", DescriptiveStatistics.QuantileR7(sorted, 0.95), "ms", "R-7 95th percentile.", "Available"),
            new("p99_frame_ms", "P99 frame time", DescriptiveStatistics.QuantileR7(sorted, 0.99), "ms", "R-7 99th percentile.", "Available"),
            new("p999_frame_ms", "P99.9 frame time",
                sorted.Length >= 10_000 ? DescriptiveStatistics.QuantileR7(sorted, 0.999) : null, "ms",
                "R-7 99.9th percentile; suppressed below 10,000 frames.",
                sorted.Length >= 10_000 ? "Available" : "Suppressed: fewer than 10,000 frames"),
            new("mean_fps", "Mean frame-rate equivalent", 1000d / mean, "FPS",
                "1000 divided by mean frame time; not a latency measurement.", "Available"),
            new("frame_stddev", "Frame-time standard deviation",
                DescriptiveStatistics.SampleStandardDeviation(sorted), "ms", "Sample standard deviation.", "Available")
        };

        if (result.CpuTimes.Count > 0)
        {
            var cpu = result.CpuTimes.Order().ToArray();
            metrics.Add(new MetricSummary(
                "cpu_busy_median", "Median CPU busy",
                DescriptiveStatistics.QuantileR7(cpu, 0.5), "ms",
                "Median time spent in the simulated frame workload.", "Available"));
        }

        var warnings = new List<string>
        {
            "Self-run benchmark. It exercises the presentation path, the scheduler and the platform's power "
            + "behaviour, which are shared with any real-time renderer.",
            "It does not reproduce a specific engine's memory access pattern, so a change that works through "
            + "cache or memory latency will under-report here. Confirm those against a real capture."
        };

        if (result.PresentsIssued > 0 && result.PresentsCompleted > 0
            && result.PresentsCompleted < result.PresentsIssued)
        {
            warnings.Add(
                $"{result.PresentsIssued - result.PresentsCompleted:N0} present(s) did not complete to the "
                + "display during the run.");
        }

        // The hash identifies this run so two results are never mistaken for the same one; there is
        // no file to fingerprint, so the run's own shape stands in for it.
        var identity = $"{label}|{sorted.Length}|{mean:R}|{DateTimeOffset.UtcNow.Ticks}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        return new CaptureAnalysis(
            DateTimeOffset.UtcNow,
            label,
            hash,
            sorted.Length,
            "benchmark-v1",
            SourceApplication,
            "FrameTime",
            sorted.Length,
            sorted.Length,
            0,
            ResultOutcome.BaselineOnly,
            metrics,
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [result.TearingAllowed ? "Hardware: Independent Flip" : "Composed: Flip"] = sorted.Length
            },
            warnings);
    }
}

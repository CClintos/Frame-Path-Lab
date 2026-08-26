using System.Diagnostics;
using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;
using FramePathLab.Windows.Interop;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Measures the timing of a managed Thread.Sleep probe plus the queried timer period. It is a
/// coarse environmental observation, not a DPC/ISR measurement and not a substitute for ETW/WPR.
/// </summary>
public static class SystemLatencyProbe
{
    private const string KernelPolicyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";

    /// <summary>Interval short enough to expose scheduling delay without becoming a busy-wait.</summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(1);

    public static SystemLatencyReport Measure(int sampleCount = 400, CancellationToken cancellationToken = default)
    {
        var (current, minimum, maximum) = ReadTimerResolution();
        var samples = MeasureSchedulerJitter(sampleCount, current, cancellationToken);

        var sorted = samples.OrderBy(value => value).ToArray();
        var median = sorted.Length > 0 ? DescriptiveStatistics.QuantileR7(sorted, 0.5) : 0;
        var p99 = sorted.Length > 0 ? DescriptiveStatistics.QuantileR7(sorted, 0.99) : 0;
        var worst = sorted.Length > 0 ? sorted[^1] : 0;

        var honored = ReadGlobalTimerRequests();
        var observation = sorted.Length == 0
            ? "Scheduler jitter could not be sampled."
            : $"Sampled {sorted.Length} wake-ups against a {current:0.###} ms timer tick; "
              + $"median lateness {median:0.###} ms, P99 {p99:0.###} ms, worst {worst:0.###} ms.";

        return new SystemLatencyReport(
            current,
            minimum,
            maximum,
            honored,
            median,
            p99,
            worst,
            sorted.Length,
            observation);
    }

    /// <summary>
    /// Returns how late each wake-up landed relative to the next timer tick it could legally have
    /// woken on.
    ///
    /// Measuring against the requested interval instead would report the timer granularity itself
    /// as jitter: on a machine running the default 15.625 ms tick, a 1 ms sleep genuinely takes
    /// about 15.6 ms, and calling that 14.6 ms of jitter would flag every idle Windows system as
    /// faulty. Rounding the expectation up to the tick isolates the part that is actually
    /// scheduling delay.
    /// </summary>
    private static double[] MeasureSchedulerJitter(
        int sampleCount,
        double timerGranularityMs,
        CancellationToken cancellationToken)
    {
        if (sampleCount <= 0)
        {
            return [];
        }

        var samples = new List<double>(sampleCount);
        var requested = SampleInterval.TotalMilliseconds;
        var granularity = timerGranularityMs > 0 ? timerGranularityMs : requested;
        var expected = Math.Ceiling(requested / granularity) * granularity;
        var previousPriority = Thread.CurrentThread.Priority;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        }
        catch (ThreadStateException)
        {
            // Priority adjustment is advisory; continue at the default priority.
        }

        try
        {
            for (var index = 0; index < sampleCount; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var start = Stopwatch.GetTimestamp();
                Thread.Sleep(SampleInterval);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var overshoot = elapsed - expected;
                samples.Add(overshoot > 0 ? overshoot : 0);
            }
        }
        finally
        {
            try
            {
                Thread.CurrentThread.Priority = previousPriority;
            }
            catch (ThreadStateException)
            {
                // Restoring priority is best-effort and never fails the scan.
            }
        }

        return samples.ToArray();
    }

    private static (double Current, double Minimum, double Maximum) ReadTimerResolution()
    {
        // NtQueryTimerResolution reports in 100 ns units, and reverses the usual naming: the
        // "minimum" value is the coarsest period and the "maximum" value is the finest.
        if (ExpertNativeMethods.NtQueryTimerResolution(out var coarsest, out var finest, out var current) != 0)
        {
            return (0, 0, 0);
        }

        return (current / 10000d, finest / 10000d, coarsest / 10000d);
    }

    /// <summary>
    /// Reports only whether an undocumented legacy policy value is present. Its presence is not a
    /// recommendation and does not prove a game benefits from it.
    /// </summary>
    public static bool ReadGlobalTimerRequests()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KernelPolicyPath, writable: false);
            return key?.GetValue("GlobalTimerResolutionRequests") is int value && value != 0;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using FramePathLab.Core.Models;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Counts machine-check and corrected hardware errors from the system event log.
///
/// This exists because validating a voltage offset by stress test is structurally unreliable. The
/// tests that are easy to run load every core, which drops boost clocks and raises voltage per
/// clock — the safest part of the curve. The region that actually fails is maximum single-core
/// boost, and the region nothing can provoke on demand is idle, where the processor makes brief
/// opportunistic boosts and enters and leaves low-power states thousands of times an hour.
///
/// The platform, however, records it either way. A corrected error is the silicon telling you the
/// margin was not enough and it recovered; an uncorrected machine check is the same message without
/// the recovery. Counting them over real uptime is the one stability measurement that covers the
/// idle case, and it costs nothing to take.
///
/// The log is read through the system's own query tool so no event-log package is needed.
/// </summary>
public static class HardwareErrorScanner
{
    private const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    private const int MaximumEventsReturned = 25;

    /// <summary>
    /// Event 18 is an uncorrected machine check; 17, 19, 46 and 47 are corrected or informational
    /// hardware errors. The distinction matters: one is a survived fault, the other is not.
    /// </summary>
    private static readonly int[] UncorrectedEventIds = [18, 20, 21];

    public static HardwareErrorSummary Scan(TimeSpan window)
    {
        var sinceMilliseconds = (long)window.TotalMilliseconds;
        var query =
            $"*[System[Provider[@Name='{WheaProvider}'] and TimeCreated[timediff(@SystemTime) <= {sinceMilliseconds}]]]";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "wevtutil.exe",
                ArgumentList = { "qe", "System", $"/q:{query}", "/f:text", $"/c:{MaximumEventsReturned}", "/rd:true" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return HardwareErrorSummary.Unreadable("The event log reader could not be started.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                TryKill(process);
                return HardwareErrorSummary.Unreadable("The event log query did not complete.");
            }

            if (process.ExitCode != 0)
            {
                return HardwareErrorSummary.Unreadable(
                    string.IsNullOrWhiteSpace(error)
                        ? "The system event log could not be queried."
                        : $"The system event log could not be queried: {error.Trim()}");
            }

            return Summarize(output, window);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException
                                             or UnauthorizedAccessException or InvalidOperationException)
        {
            return HardwareErrorSummary.Unreadable($"Hardware error history could not be read: {exception.Message}");
        }
    }

    private static HardwareErrorSummary Summarize(string output, TimeSpan window)
    {
        var events = new List<HardwareErrorEvent>();
        var uncorrected = 0;
        var corrected = 0;

        // wevtutil's text output writes one block per event; the fields needed here are the event
        // identifier and the creation timestamp, both on their own lines.
        foreach (var block in output.Split("Event[", StringSplitOptions.RemoveEmptyEntries))
        {
            var eventId = ReadInt(block, "Event ID:");
            if (eventId is null)
            {
                continue;
            }

            var timestamp = ReadTimestamp(block);
            var isUncorrected = UncorrectedEventIds.Contains(eventId.Value);
            if (isUncorrected)
            {
                uncorrected++;
            }
            else
            {
                corrected++;
            }

            events.Add(new HardwareErrorEvent(
                timestamp ?? DateTimeOffset.MinValue,
                eventId.Value,
                WheaProvider,
                isUncorrected
                    ? $"Uncorrected machine check (event {eventId})"
                    : $"Corrected hardware error (event {eventId})"));
        }

        if (events.Count == 0)
        {
            return new HardwareErrorSummary(
                true, 0, 0, 0, null, window, [],
                $"No hardware errors were logged in the last {DescribeWindow(window)}.");
        }

        var mostRecent = events
            .Where(entry => entry.TimestampUtc > DateTimeOffset.MinValue)
            .Select(entry => (DateTimeOffset?)entry.TimestampUtc)
            .DefaultIfEmpty(null)
            .Max();

        return new HardwareErrorSummary(
            true,
            events.Count,
            uncorrected,
            corrected,
            mostRecent,
            window,
            events.OrderByDescending(entry => entry.TimestampUtc).Take(MaximumEventsReturned).ToArray(),
            $"{events.Count} hardware error(s) in the last {DescribeWindow(window)}: "
            + $"{uncorrected} uncorrected, {corrected} corrected.");
    }

    private static string DescribeWindow(TimeSpan window)
        => window.TotalDays >= 1
            ? $"{window.TotalDays:0.#} day(s)"
            : $"{window.TotalHours:0.#} hour(s)";

    private static int? ReadInt(string block, string label)
    {
        var match = Regex.Match(
            block, Regex.Escape(label) + @"\s*(\d+)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    private static DateTimeOffset? ReadTimestamp(string block)
    {
        var match = Regex.Match(
            block, @"Date:\s*(\S+)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success
               && DateTimeOffset.TryParse(
                   match.Groups[1].Value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }
}

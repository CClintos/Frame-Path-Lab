using System.Diagnostics;
using System.Runtime.InteropServices;
using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Input;

/// <summary>
/// Measures the mouse report stream as this PC actually delivers it.
///
/// A device advertising 1000 Hz that only sustains 500 Hz, or that delivers 1000 Hz with heavy
/// interval scatter, produces aim that feels inconsistent while every frame-time metric stays
/// clean. That failure is invisible to frame capture because it happens before the engine samples
/// input. This probe times WM_INPUT arrivals, so it measures delivery to a user-mode process
/// rather than the device's electrical report rate; USB scheduling and driver batching are inside
/// the number, and it is reported as such.
/// </summary>
public sealed class InputChainProbe
{
    private const string WindowClassName = "FramePathLabRawInputSink";
    private const int MinimumUsefulSamples = 64;

    private static readonly int[] StandardPollingRates = [125, 250, 500, 1000, 2000, 4000, 8000];

    public static InputChainReport Measure(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var (accelerationEnabled, pointerSpeed) = ReadPointerBehaviour();
        var intervals = new List<double>(4096);
        string failure;

        try
        {
            failure = CollectIntervals(duration, intervals, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException)
        {
            failure = $"Raw input sink could not be created: {exception.Message}";
        }

        if (intervals.Count < MinimumUsefulSamples)
        {
            return new InputChainReport(
                false,
                intervals.Count,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                accelerationEnabled,
                pointerSpeed,
                "Not measured",
                string.IsNullOrEmpty(failure)
                    ? $"Only {intervals.Count} mouse reports arrived. Move the mouse continuously for the whole measurement window."
                    : failure);
        }

        return Summarize(intervals, accelerationEnabled, pointerSpeed);
    }

    private static InputChainReport Summarize(List<double> intervals, bool accelerationEnabled, int pointerSpeed)
    {
        var sorted = intervals.OrderBy(value => value).ToArray();
        var median = DescriptiveStatistics.QuantileR7(sorted, 0.5);
        var p99 = DescriptiveStatistics.QuantileR7(sorted, 0.99);
        var stdDev = DescriptiveStatistics.SampleStandardDeviation(sorted);
        var worst = sorted[^1];

        // The floor of the distribution shows what the device is capable of when nothing gets in
        // the way; the median shows what it sustained. Comparing the two separates "this mouse is
        // configured at 500 Hz" from "this mouse is set to 1000 Hz and is missing half its reports".
        var fastestSustained = DescriptiveStatistics.QuantileR7(sorted, 0.05);
        var capabilityHz = fastestSustained > 0 ? 1000d / fastestSustained : 0;
        var nominalHz = NearestStandardRate(capabilityHz);
        var measuredHz = median > 0 ? 1000d / median : 0;

        var missed = 0;
        if (median > 0)
        {
            foreach (var interval in sorted)
            {
                if (interval > median * 1.8)
                {
                    missed += (int)Math.Round(interval / median) - 1;
                }
            }
        }

        var observation =
            $"{sorted.Length} reports; sustained {measuredHz:0} Hz against a {nominalHz:0} Hz capability floor; "
            + $"median interval {median:0.###} ms, P99 {p99:0.###} ms, worst {worst:0.###} ms.";

        return new InputChainReport(
            true,
            sorted.Length,
            measuredHz,
            nominalHz,
            median,
            stdDev,
            p99,
            worst,
            missed,
            accelerationEnabled,
            pointerSpeed,
            "System mouse stream",
            observation);
    }

    private static double NearestStandardRate(double observedHz)
    {
        if (observedHz <= 0)
        {
            return 0;
        }

        var nearest = StandardPollingRates[0];
        var bestDistance = double.MaxValue;
        foreach (var rate in StandardPollingRates)
        {
            var distance = Math.Abs(rate - observedHz);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = rate;
            }
        }

        // Refuse to snap a wildly out-of-range measurement onto a standard rate.
        return bestDistance > nearest * 0.35 ? observedHz : nearest;
    }

    public static (bool AccelerationEnabled, int PointerSpeed) ReadPointerBehaviour()
    {
        var mouse = new int[3];
        var accelerationEnabled = ExpertNativeMethods.SystemParametersInfo(
                                      ExpertNativeMethods.SpiGetMouse, 0, mouse, 0)
                                  && mouse[2] != 0;

        var speed = 0;
        ExpertNativeMethods.SystemParametersInfo(ExpertNativeMethods.SpiGetMouseSpeed, 0, ref speed, 0);
        return (accelerationEnabled, speed);
    }

    private static string CollectIntervals(
        TimeSpan duration,
        List<double> intervals,
        CancellationToken cancellationToken)
    {
        string failure = string.Empty;
        var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            nint window = 0;
            ushort classAtom = 0;
            WndProc? procedure = null;
            try
            {
                var instance = RawInputInterop.GetModuleHandle(null);
                long lastTimestamp = 0;

                procedure = (hwnd, message, wParam, lParam) =>
                {
                    if (message == ExpertNativeMethods.WmInput)
                    {
                        var now = Stopwatch.GetTimestamp();
                        if (lastTimestamp != 0)
                        {
                            var elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalMilliseconds;
                            if (elapsed is > 0 and < 200)
                            {
                                intervals.Add(elapsed);
                            }
                        }

                        lastTimestamp = now;
                    }

                    return RawInputInterop.DefWindowProc(hwnd, message, wParam, lParam);
                };

                var windowClass = new WndClassEx
                {
                    Size = (uint)Marshal.SizeOf<WndClassEx>(),
                    WndProc = Marshal.GetFunctionPointerForDelegate(procedure),
                    Instance = instance,
                    ClassName = WindowClassName
                };

                classAtom = RawInputInterop.RegisterClassEx(ref windowClass);
                if (classAtom == 0)
                {
                    failure = $"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).";
                    return;
                }

                window = RawInputInterop.CreateWindowEx(
                    0, WindowClassName, string.Empty, 0, 0, 0, 0, 0,
                    RawInputInterop.HwndMessage, 0, instance, 0);
                if (window == 0)
                {
                    failure = $"Message-only window could not be created ({Marshal.GetLastWin32Error()}).";
                    return;
                }

                var devices = new[]
                {
                    new RawInputDevice
                    {
                        UsagePage = ExpertNativeMethods.HidUsagePageGeneric,
                        Usage = ExpertNativeMethods.HidUsageGenericMouse,
                        Flags = ExpertNativeMethods.RidevInputSink,
                        Target = window
                    }
                };

                if (!ExpertNativeMethods.RegisterRawInputDevices(
                        devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
                {
                    failure = $"RegisterRawInputDevices failed ({Marshal.GetLastWin32Error()}).";
                    return;
                }

                ready.Set();
                PumpUntil(duration, window, cancellationToken);
            }
            finally
            {
                if (window != 0)
                {
                    RawInputInterop.DestroyWindow(window);
                }

                if (classAtom != 0)
                {
                    RawInputInterop.UnregisterClass(WindowClassName, RawInputInterop.GetModuleHandle(null));
                }

                GC.KeepAlive(procedure);
                ready.Set();
            }
        })
        {
            IsBackground = true,
            Name = "FramePathLab raw input probe"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
        thread.Join(duration + TimeSpan.FromSeconds(5));
        return failure;
    }

    private static void PumpUntil(TimeSpan duration, nint window, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp();
        // A timer message guarantees the loop wakes even when the mouse is completely still, so a
        // motionless measurement ends on schedule instead of blocking on GetMessage.
        RawInputInterop.SetTimer(window, 1, 100, 0);
        while (Stopwatch.GetElapsedTime(deadline) < duration && !cancellationToken.IsCancellationRequested)
        {
            if (!RawInputInterop.PeekMessage(out var message, 0, 0, 0, RawInputInterop.PmRemove))
            {
                Thread.Sleep(1);
                continue;
            }

            RawInputInterop.TranslateMessage(ref message);
            RawInputInterop.DispatchMessage(ref message);
        }

        RawInputInterop.KillTimer(window, 1);
    }
}

internal delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WndClassEx
{
    public uint Size;
    public uint Style;
    public nint WndProc;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? MenuName;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string ClassName;

    public nint IconSmall;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public int PointX;
    public int PointY;
}

internal static class RawInputInterop
{
    internal static readonly nint HwndMessage = -3;
    internal const uint PmRemove = 0x0001;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    internal static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    internal static extern bool PeekMessage(
        out NativeMessage message,
        nint window,
        uint filterMin,
        uint filterMax,
        uint removeMessage);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nuint SetTimer(nint window, nuint eventId, uint elapseMilliseconds, nint callback);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool KillTimer(nint window, nuint eventId);
}

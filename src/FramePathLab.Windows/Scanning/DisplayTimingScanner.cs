using System.Runtime.InteropServices;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads exact display timing through QueryDisplayConfig. EnumDisplaySettings truncates the
/// refresh rate to an integer, which is why 59.94 Hz has to be guessed at from a 59-versus-60
/// mismatch; the rational numerator/denominator here removes the guess entirely and gives the
/// frame-cap calculator a real number to work from.
/// </summary>
public static class DisplayTimingScanner
{
    public static IReadOnlyList<DisplayTiming> Scan()
    {
        if (ExpertNativeMethods.GetDisplayConfigBufferSizes(
                ExpertNativeMethods.QdcOnlyActivePaths,
                out var pathCount,
                out var modeCount) != 0)
        {
            return [];
        }

        if (pathCount == 0)
        {
            return [];
        }

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        if (ExpertNativeMethods.QueryDisplayConfig(
                ExpertNativeMethods.QdcOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                nint.Zero) != 0)
        {
            return [];
        }

        var results = new List<DisplayTiming>();
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            var gdiName = ReadSourceName(path);
            if (string.IsNullOrWhiteSpace(gdiName))
            {
                continue;
            }

            var (width, height) = ReadSourceMode(modes, modeCount, path);
            var (numerator, denominator) = ResolveRefresh(modes, modeCount, path);
            var (advancedSupported, advancedEnabled) = ReadAdvancedColor(path);

            results.Add(new DisplayTiming(
                gdiName,
                ReadTargetName(path),
                numerator,
                denominator,
                width,
                height,
                results.Count == 0,
                advancedSupported,
                advancedEnabled));
        }

        return results;
    }

    private static (uint Numerator, uint Denominator) ResolveRefresh(
        DisplayConfigModeInfo[] modes,
        uint modeCount,
        DisplayConfigPathInfo path)
    {
        // The target mode carries the timing the display is actually being driven at. The path's
        // own refresh fields are a fallback for drivers that omit a target mode entry.
        var modeIndex = path.TargetInfo.ModeInfoIdx;
        if (modeIndex < modeCount && modes[modeIndex].InfoType == 2)
        {
            var signal = modes[modeIndex].Mode.TargetMode.TargetVideoSignalInfo;
            if (signal.VSyncFreqDenominator != 0)
            {
                return (signal.VSyncFreqNumerator, signal.VSyncFreqDenominator);
            }
        }

        return path.TargetInfo.RefreshRateDenominator != 0
            ? (path.TargetInfo.RefreshRateNumerator, path.TargetInfo.RefreshRateDenominator)
            : (0u, 1u);
    }

    private static (int Width, int Height) ReadSourceMode(
        DisplayConfigModeInfo[] modes,
        uint modeCount,
        DisplayConfigPathInfo path)
    {
        var modeIndex = path.SourceInfo.ModeInfoIdx;
        if (modeIndex < modeCount && modes[modeIndex].InfoType == 1)
        {
            var source = modes[modeIndex].Mode.SourceMode;
            return ((int)source.Width, (int)source.Height);
        }

        return (0, 0);
    }

    private static string ReadSourceName(DisplayConfigPathInfo path)
    {
        var request = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = ExpertNativeMethods.DeviceInfoGetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = path.SourceInfo.AdapterId,
                Id = path.SourceInfo.Id
            },
            ViewGdiDeviceName = string.Empty
        };

        return ExpertNativeMethods.DisplayConfigGetDeviceInfo(ref request) == 0
            ? request.ViewGdiDeviceName
            : string.Empty;
    }

    private static string ReadTargetName(DisplayConfigPathInfo path)
    {
        var request = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = ExpertNativeMethods.DeviceInfoGetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };

        return ExpertNativeMethods.DisplayConfigGetDeviceInfo(ref request) == 0
               && !string.IsNullOrWhiteSpace(request.MonitorFriendlyDeviceName)
            ? request.MonitorFriendlyDeviceName
            : "Attached monitor";
    }

    private static (bool Supported, bool Enabled) ReadAdvancedColor(DisplayConfigPathInfo path)
    {
        var request = new DisplayConfigAdvancedColorInfo
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = ExpertNativeMethods.DeviceInfoGetAdvancedColorInfo,
                Size = (uint)Marshal.SizeOf<DisplayConfigAdvancedColorInfo>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id
            }
        };

        return ExpertNativeMethods.DisplayConfigGetDeviceInfo(ref request) == 0
            ? (request.AdvancedColorSupported, request.AdvancedColorEnabled)
            : (false, false);
    }
}

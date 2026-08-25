using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

public sealed partial class WindowsEnvironmentScanner : IEnvironmentScanner
{
    private static readonly (string ProcessName, string DisplayName)[] OptionalApplicationNames =
    [
        ("RTSS", "RivaTuner Statistics Server"),
        ("obs64", "OBS Studio"),
        ("GameBar", "Xbox Game Bar"),
        ("GameBarFTServer", "Xbox Game Bar capture"),
        ("Discord", "Discord"),
        ("Overwolf", "Overwolf"),
        ("Medal", "Medal")
    ];

    public Task<EnvironmentSnapshot> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private static EnvironmentSnapshot Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var displays = EnumerateDisplays(cancellationToken);
        var game = ReadSteamGameState();
        var power = ReadPowerState();
        var optionalApplications = ObserveOptionalApplications(cancellationToken);
        var remote = NativeMethods.GetSystemMetrics(NativeMethods.SmRemoteSession) != 0;
        var memory = MemoryStatusEx.Create();
        var totalPhysical = NativeMethods.GlobalMemoryStatusEx(ref memory) ? memory.TotalPhysical : 0UL;

        var limitations = new List<string>();
        if (remote)
        {
            limitations.Add("Remote Desktop session detected");
        }

        if (displays.Count == 0)
        {
            limitations.Add("No attached physical display mode was resolved");
        }
        else if (displays.Count != 1)
        {
            limitations.Add($"{displays.Count} active display paths detected; initial decision-grade tier requires exactly one");
        }

        if (!game.Cs2Installed)
        {
            limitations.Add("Supported CS2 Steam installation/build was not resolved");
        }

        var capability = remote || displays.Count == 0
            ? CapabilityState.Unsupported
            : displays.Count == 1 && game.Cs2Installed
                ? CapabilityState.Supported
                : CapabilityState.Provisional;

        return new EnvironmentSnapshot(
            DateTimeOffset.UtcNow,
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Version.ToString(),
            Environment.Is64BitOperatingSystem,
            IsElevated(),
            remote,
            Environment.ProcessorCount,
            totalPhysical,
            displays,
            game,
            power,
            optionalApplications,
            capability,
            limitations);
    }

    private static IReadOnlyList<DisplaySnapshot> EnumerateDisplays(CancellationToken cancellationToken)
    {
        var displays = new List<DisplaySnapshot>();
        for (uint adapterIndex = 0; adapterIndex < 32; adapterIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
            {
                break;
            }

            if (!adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop)
                || adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.MirroringDriver)
                || adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.Remote)
                || adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.Disconnect))
            {
                continue;
            }

            var current = DevMode.Create();
            if (!NativeMethods.EnumDisplaySettingsEx(
                    adapter.DeviceName,
                    NativeMethods.EnumCurrentSettings,
                    ref current,
                    0))
            {
                continue;
            }

            var refreshRates = EnumerateRefreshRates(adapter.DeviceName, current.PelsWidth, current.PelsHeight);
            var monitorDescription = ReadMonitorDescription(adapter.DeviceName);
            displays.Add(new DisplaySnapshot(
                adapter.DeviceName,
                Clean(adapter.DeviceString, "Unknown display adapter"),
                monitorDescription,
                adapter.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice),
                true,
                current.PelsWidth,
                current.PelsHeight,
                current.BitsPerPel,
                NormalizeRefresh(current.DisplayFrequency),
                refreshRates.Count > 0 ? refreshRates.Max() : NormalizeRefresh(current.DisplayFrequency),
                refreshRates));
        }

        return displays;
    }

    private static IReadOnlyList<double> EnumerateRefreshRates(string deviceName, int width, int height)
    {
        var rates = new SortedSet<double>();
        for (var modeIndex = 0; modeIndex < 4096; modeIndex++)
        {
            var mode = DevMode.Create();
            if (!NativeMethods.EnumDisplaySettingsEx(deviceName, modeIndex, ref mode, 0))
            {
                break;
            }

            if (mode.PelsWidth == width && mode.PelsHeight == height)
            {
                var rate = NormalizeRefresh(mode.DisplayFrequency);
                if (rate is > 20 and < 1000)
                {
                    rates.Add(rate);
                }
            }
        }

        return rates.ToArray();
    }

    private static string ReadMonitorDescription(string adapterDeviceName)
    {
        var monitor = DisplayDevice.Create();
        return NativeMethods.EnumDisplayDevices(adapterDeviceName, 0, ref monitor, 0)
            ? Clean(monitor.DeviceString, "Attached monitor")
            : "Attached monitor";
    }

    private static SteamGameSnapshot ReadSteamGameState()
    {
        var running = IsProcessRunning("cs2");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
            var steamPath = key?.GetValue("SteamPath") as string;
            if (string.IsNullOrWhiteSpace(steamPath))
            {
                return new SteamGameSnapshot(false, false, running, "unknown", "Steam path not resolved");
            }

            var normalizedSteamPath = steamPath.Replace('/', Path.DirectorySeparatorChar);
            var manifestPath = EnumerateSteamLibraries(normalizedSteamPath)
                .Select(library => Path.Combine(library, "steamapps", "appmanifest_730.acf"))
                .FirstOrDefault(File.Exists);
            if (manifestPath is null)
            {
                return new SteamGameSnapshot(true, false, running, "unknown", "CS2 manifest not found in resolved Steam libraries");
            }

            var manifest = File.ReadAllText(manifestPath);
            var buildId = BuildIdRegex().Match(manifest).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(buildId))
            {
                buildId = "unknown";
            }

            return new SteamGameSnapshot(true, true, running, buildId, "CS2 Steam app manifest resolved");
        }
        catch (UnauthorizedAccessException)
        {
            return new SteamGameSnapshot(false, false, running, "unknown", "Steam state unavailable: access denied");
        }
        catch (IOException)
        {
            return new SteamGameSnapshot(false, false, running, "unknown", "Steam state unavailable: I/O error");
        }
    }

    private static IReadOnlyList<string> EnumerateSteamLibraries(string normalizedSteamPath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(normalizedSteamPath)
        };
        var libraryFile = Path.Combine(normalizedSteamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            return libraries.ToArray();
        }

        try
        {
            var content = File.ReadAllText(libraryFile);
            foreach (Match match in LibraryPathRegex().Matches(content))
            {
                if (libraries.Count >= 64)
                {
                    break;
                }

                var value = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value))
                {
                    libraries.Add(Path.GetFullPath(value));
                }
            }
        }
        catch (IOException)
        {
            // The primary Steam path remains usable when the optional library list is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The primary Steam path remains usable when the optional library list is unavailable.
        }

        return libraries.ToArray();
    }

    private static PowerSnapshot ReadPowerState()
    {
        var hasPowerStatus = NativeMethods.GetSystemPowerStatus(out var status);
        var isOnAc = hasPowerStatus && status.AcLineStatus == 1;
        var batteryPercent = hasPowerStatus && status.BatteryLifePercent <= 100
            ? status.BatteryLifePercent
            : -1;
        var schemeId = ReadActivePowerScheme();
        var summary = hasPowerStatus
            ? $"{(isOnAc ? "AC power" : "Battery or unknown AC state")}; battery {(batteryPercent >= 0 ? $"{batteryPercent}%" : "not reported")}; active scheme {schemeId}"
            : $"Power status unavailable; active scheme {schemeId}";
        return new PowerSnapshot(isOnAc, batteryPercent, schemeId, summary);
    }

    private static string ReadActivePowerScheme()
    {
        var result = NativeMethods.PowerGetActiveScheme(0, out var pointer);
        if (result != 0 || pointer == 0)
        {
            return "unknown";
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer).ToString("D");
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    private static IReadOnlyList<string> ObserveOptionalApplications(CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        foreach (var (processName, displayName) in OptionalApplicationNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsProcessRunning(processName))
            {
                observed.Add(displayName);
            }
        }

        return observed;
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static double NormalizeRefresh(int value)
        => value is <= 1 or > 1000 ? 0 : value;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    [GeneratedRegex("\\\"buildid\\\"\\s+\\\"(\\d+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex BuildIdRegex();

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}

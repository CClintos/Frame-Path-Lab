using System.Diagnostics;
using System.Text.RegularExpressions;
using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Platform-level readings that do not belong to the CPU, GPU, display or input scanners: whether
/// timer-counter context, whether the GPU is using message-signalled interrupts, and
/// whether Steam is currently moving bytes.
/// </summary>
public static partial class PlatformStateScanner
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string PciEnumPath = @"SYSTEM\CurrentControlSet\Enum\PCI";

    /// <summary>
    /// QPC frequency is useful capture provenance, but Microsoft does not document it as an
    /// authoritative read of the BCD useplatformclock flag. The app therefore reports the flag as
    /// unknown instead of converting one observed frequency into a boot-configuration claim.
    /// </summary>
    public static (bool? ForcedPlatformClock, long Frequency) ReadPlatformTimer()
    {
        var frequency = Stopwatch.Frequency;
        return (null, frequency);
    }

    /// <summary>
    /// Reads whether the display adapter is configured for message-signalled interrupts. An absent
    /// value means the driver default applies rather than meaning the feature is off, so it is
    /// reported as unset rather than as a problem to fix.
    /// </summary>
    public static (bool? MsiEnabled, string? RegistryPath, string Observation) ReadGpuInterruptMode()
    {
        try
        {
            using var pci = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: false);
            if (pci is null)
            {
                return (null, null, "PCI device enumeration could not be read.");
            }

            foreach (var deviceName in pci.GetSubKeyNames())
            {
                using var device = pci.OpenSubKey(deviceName, writable: false);
                if (device is null)
                {
                    continue;
                }

                foreach (var instanceName in device.GetSubKeyNames())
                {
                    using var instance = device.OpenSubKey(instanceName, writable: false);
                    if (instance?.GetValue("ClassGUID") as string is not { } classGuid
                        || !classGuid.Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var description = instance.GetValue("DeviceDesc") as string ?? deviceName;
                    var msiPath = $@"{PciEnumPath}\{deviceName}\{instanceName}"
                                  + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
                    using var msi = Registry.LocalMachine.OpenSubKey(msiPath, writable: false);
                    var supported = msi?.GetValue("MSISupported");

                    return supported is int value
                        ? (value != 0, $@"HKLM\{msiPath}",
                            $"{Trim(description)}: MSISupported is explicitly {value}.")
                        : (null, $@"HKLM\{msiPath}",
                            $"{Trim(description)}: no explicit interrupt-mode value; the driver default applies.");
                }
            }

            return (null, null, "No display adapter was found under PCI enumeration.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (null, null, $"Interrupt mode could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Detects an in-flight Steam transfer by comparing downloaded against total bytes in every
    /// app manifest across every resolved library.
    /// </summary>
    public static SteamActivity ReadSteamActivity()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
            if (key?.GetValue("SteamPath") as string is not { Length: > 0 } steamPath)
            {
                return new SteamActivity(false, [], "Steam location was not resolved.");
            }

            var active = new List<string>();
            foreach (var library in EnumerateLibraries(steamPath.Replace('/', Path.DirectorySeparatorChar)))
            {
                var steamApps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamApps))
                {
                    continue;
                }

                foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    var content = ReadTextSafely(manifest);
                    if (content is null)
                    {
                        continue;
                    }

                    var toDownload = ReadLong(content, "BytesToDownload");
                    var downloaded = ReadLong(content, "BytesDownloaded");
                    if (toDownload > 0 && downloaded >= 0 && downloaded < toDownload)
                    {
                        var name = NameRegex().Match(content).Groups[1].Value;
                        var remaining = (toDownload - downloaded) / 1024d / 1024d;
                        active.Add($"{(string.IsNullOrWhiteSpace(name) ? Path.GetFileName(manifest) : name)} "
                                   + $"({remaining:N0} MiB remaining)");
                    }
                }
            }

            return active.Count == 0
                ? new SteamActivity(false, [], "No Steam transfer is in progress.")
                : new SteamActivity(true, active, $"Steam is transferring: {string.Join("; ", active)}.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new SteamActivity(false, [], $"Steam activity could not be read: {exception.Message}");
        }
    }

    private static IEnumerable<string> EnumerateLibraries(string steamPath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        var content = File.Exists(libraryFile) ? ReadTextSafely(libraryFile) : null;
        if (content is not null)
        {
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

        return libraries;
    }

    private static string? ReadTextSafely(string path)
    {
        try
        {
            // App manifests are small; a bound keeps a corrupt or hostile file from being read whole.
            var info = new FileInfo(path);
            return info.Length > 1024 * 1024 ? null : File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long ReadLong(string content, string key)
    {
        var match = Regex.Match(
            content,
            "\"" + Regex.Escape(key) + "\"\\s+\"(\\d+)\"",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success && long.TryParse(match.Groups[1].Value, out var value) ? value : -1;
    }

    private static string Trim(string value) => value.Contains(';') ? value[(value.IndexOf(';') + 1)..] : value;

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\\\"name\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}

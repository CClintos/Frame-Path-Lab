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

    private const string NetworkClassGuid = "{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string UsbClassGuid = "{36fc9e60-c465-11cf-8056-444553540000}";

    /// <summary>
    /// Reads the reserved processor set: the cores the scheduler has been told to keep general work
    /// and device interrupts off.
    ///
    /// This is the inverse of pinning a process, and the difference matters. Pinning the game means
    /// opening a handle to it with rights to change its execution, which an anti-cheat cannot
    /// distinguish from hostile behaviour. Reserving cores moves everything else instead, so the
    /// game lands on the free cores without ever being touched.
    /// </summary>
    public static (ulong? ReservedMask, string Observation) ReadReservedCpuSets()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel", writable: false);
            var raw = key?.GetValue("ReservedCpuSets");

            if (raw is not byte[] bytes || bytes.Length == 0)
            {
                return (null, "No processor reservation is configured; the scheduler may use every core.");
            }

            // The value is a little-endian bitmask; only the first 64 processors are modelled here,
            // which covers every desktop part this catalogue targets.
            ulong mask = 0;
            for (var index = 0; index < Math.Min(bytes.Length, 8); index++)
            {
                mask |= (ulong)bytes[index] << (index * 8);
            }

            return mask == 0
                ? (null, "A reservation value exists but reserves no processors.")
                : (mask,
                    $"Processors 0x{mask:X} are reserved from general scheduling "
                    + $"({System.Numerics.BitOperations.PopCount(mask)} of them).");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (null, $"Processor reservation could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the interrupt moderation interval on the USB host controllers.
    ///
    /// The controller batches interrupts so the processor is disturbed less often. That batching
    /// delays delivery of every report inside the batch, which only becomes visible above the
    /// ordinary polling rate — at which point it is delaying exactly the reports a high-rate mouse
    /// exists to produce.
    /// </summary>
    public static (bool Readable, int Controllers, int ModeratedControllers, string Observation)
        ReadUsbInterruptModeration()
    {
        try
        {
            using var pci = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: false);
            if (pci is null)
            {
                return (false, 0, 0, "PCI device enumeration could not be read.");
            }

            var total = 0;
            var moderated = 0;
            foreach (var deviceName in pci.GetSubKeyNames())
            {
                using var device = pci.OpenSubKey(deviceName, writable: false);
                foreach (var instanceName in device?.GetSubKeyNames() ?? [])
                {
                    using var instance = device!.OpenSubKey(instanceName, writable: false);
                    if (instance?.GetValue("ClassGUID") as string is not { } classGuid
                        || !classGuid.Equals(UsbClassGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    total++;
                    using var parameters = instance.OpenSubKey("Device Parameters", writable: false);

                    // Absent means the controller's own default applies, which is a moderated
                    // interval on every current Windows build.
                    if (parameters?.GetValue("IdleInWorkingState") is null
                        || parameters.GetValue("IdleInWorkingState") is int idle && idle != 0)
                    {
                        moderated++;
                    }
                }
            }

            return total == 0
                ? (false, 0, 0, "No USB host controller was found under PCI enumeration.")
                : (true, total, moderated,
                    $"{total} USB host controller(s); {moderated} using a moderated or default interrupt interval.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (false, 0, 0, $"USB controller state could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the interrupt mode of the network adapter carrying live traffic. Same reasoning as the
    /// display adapter: an absent value means the driver default applies, not that the feature is
    /// off, so it is reported as unset rather than as something to fix.
    /// </summary>
    public static (bool? MsiEnabled, string Observation) ReadNetworkInterruptMode()
    {
        try
        {
            using var pci = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: false);
            if (pci is null)
            {
                return (null, "PCI device enumeration could not be read.");
            }

            foreach (var deviceName in pci.GetSubKeyNames())
            {
                using var device = pci.OpenSubKey(deviceName, writable: false);
                foreach (var instanceName in device?.GetSubKeyNames() ?? [])
                {
                    using var instance = device!.OpenSubKey(instanceName, writable: false);
                    if (instance?.GetValue("ClassGUID") as string is not { } classGuid
                        || !classGuid.Equals(NetworkClassGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var description = instance.GetValue("DeviceDesc") as string ?? deviceName;
                    using var msi = Registry.LocalMachine.OpenSubKey(
                        $@"{PciEnumPath}\{deviceName}\{instanceName}"
                        + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties",
                        writable: false);

                    return msi?.GetValue("MSISupported") is int value
                        ? (value != 0, $"{Trim(description)}: MSISupported is explicitly {value}.")
                        : (null, $"{Trim(description)}: no explicit interrupt-mode value; the driver default applies.");
                }
            }

            return (null, "No wired network adapter was found under PCI enumeration.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (null, $"Network interrupt mode could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the boot options that affect timing directly, rather than inferring them from the
    /// performance-counter frequency. This needs elevation, so a failure is reported as unread
    /// rather than as "nothing is set" — those are very different claims.
    /// </summary>
    public static BootTimingState ReadBootTiming()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "bcdedit.exe",
                Arguments = "/enum {current}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return BootTimingState.Unreadable("The boot configuration reader could not be started.");
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone; nothing to clean up.
                }

                return BootTimingState.Unreadable("The boot configuration reader did not complete.");
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return BootTimingState.Unreadable(
                    "Boot configuration requires an elevated session; it was not read.");
            }

            // bcdedit only prints a boot option that has been explicitly set, so an absent boolean
            // means the option is not configured — not that its state is unknown. Reporting absence
            // as unknown left every correctly configured machine looking unreadable, which held the
            // platform-timer gate shut on exactly the machines that should have passed it. The
            // enumeration succeeding is what makes absence meaningful, so it is only read that way
            // here, inside the success path.
            return new BootTimingState(
                true,
                ReadFlag(output, "useplatformclock") ?? false,
                ReadFlag(output, "useplatformtick") ?? false,
                ReadFlag(output, "disabledynamictick") ?? false,
                ReadWord(output, "tscsyncpolicy"),
                "Boot configuration read.",
                ReadWord(output, "hypervisorlaunchtype"));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException
                                             or UnauthorizedAccessException or InvalidOperationException)
        {
            return BootTimingState.Unreadable($"Boot configuration could not be read: {exception.Message}");
        }
    }

    private static bool? ReadFlag(string output, string option)
    {
        var match = Regex.Match(
            output, $@"^\s*{Regex.Escape(option)}\s+(Yes|No)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value.Equals("Yes", StringComparison.OrdinalIgnoreCase) : null;
    }

    private static string? ReadWord(string output, string option)
    {
        var match = Regex.Match(
            output, $@"^\s*{Regex.Escape(option)}\s+(\w+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads whether the speculative-execution mitigations have been overridden. Reported only:
    /// this is a processor-level security guarantee, and turning it off is not a change this
    /// application makes on someone's behalf.
    /// </summary>
    public static (bool? Overridden, string Observation) ReadSpeculativeMitigations()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", writable: false);
            var setting = key?.GetValue("FeatureSettingsOverride") as int?;
            var mask = key?.GetValue("FeatureSettingsOverrideMask") as int?;

            if (setting is null && mask is null)
            {
                return (false, "No mitigation override is set; the processor mitigations are active.");
            }

            // The commonly published override sets both to 3, which disables the branch-target and
            // kernel-page mitigations together.
            var disabled = setting == 3 && mask == 3;
            return (disabled,
                disabled
                    ? "Speculative-execution mitigations are overridden off."
                    : $"A partial override is present (setting {setting?.ToString() ?? "unset"}, "
                      + $"mask {mask?.ToString() ?? "unset"}).");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (null, $"Mitigation state could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Fast startup hibernates the kernel session instead of shutting it down, so a "shut down"
    /// followed by a power-on resumes the previous kernel state and its driver state with it. That
    /// is why a problem survives a shutdown but disappears after a restart.
    /// </summary>
    public static bool? ReadFastStartup()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", writable: false);
            return key?.GetValue("HiberbootEnabled") is int value ? value != 0 : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads whether an interrupt affinity policy has been pinned onto the display adapter. An
    /// explicit policy here steers device interrupts onto chosen processors; it is a real lever and
    /// a real way to make a machine worse, so it is only ever reported, never written.
    /// </summary>
    public static (bool HasPolicy, string Observation) ReadInterruptAffinityPolicy()
    {
        try
        {
            using var pci = Registry.LocalMachine.OpenSubKey(PciEnumPath, writable: false);
            if (pci is null)
            {
                return (false, "PCI device enumeration could not be read.");
            }

            foreach (var deviceName in pci.GetSubKeyNames())
            {
                using var device = pci.OpenSubKey(deviceName, writable: false);
                foreach (var instanceName in device?.GetSubKeyNames() ?? [])
                {
                    using var instance = device!.OpenSubKey(instanceName, writable: false);
                    if (instance?.GetValue("ClassGUID") as string is not { } classGuid
                        || !classGuid.Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var policy = Registry.LocalMachine.OpenSubKey(
                        $@"{PciEnumPath}\{deviceName}\{instanceName}\Device Parameters\Interrupt Management\Affinity Policy",
                        writable: false);
                    if (policy is null)
                    {
                        return (false, "No interrupt affinity policy is set on the display adapter.");
                    }

                    var devicePolicy = policy.GetValue("DevicePolicy");
                    var assignment = policy.GetValue("AssignmentSetOverride");
                    return devicePolicy is null && assignment is null
                        ? (false, "An affinity policy key exists but carries no policy values.")
                        : (true,
                            $"An interrupt affinity policy is set (DevicePolicy {devicePolicy ?? "unset"}, "
                            + $"AssignmentSetOverride {(assignment is byte[] bytes ? Convert.ToHexString(bytes) : assignment ?? "unset")}).");
                }
            }

            return (false, "No display adapter was found under PCI enumeration.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (false, $"Interrupt affinity policy could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the real-time scanning exclusion list. Whether an exclusion belongs here is the
    /// user's call; this only reports what is already configured.
    /// </summary>
    public static (bool Readable, IReadOnlyList<string> Paths, string Observation) ReadDefenderExclusions()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", writable: false);
            if (key is null)
            {
                return (false, [], "The exclusion list is not readable without elevation on this system.");
            }

            var paths = key.GetValueNames();
            return (true, paths,
                paths.Length == 0
                    ? "No real-time scanning path exclusions are configured."
                    : $"{paths.Length} path exclusion(s) configured.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return (false, [], "The exclusion list is not readable without elevation on this system.");
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

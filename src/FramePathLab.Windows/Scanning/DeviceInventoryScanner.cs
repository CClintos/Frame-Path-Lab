using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Enumerates present devices and works out which are genuinely idle.
///
/// Class membership alone is not enough to decide whether something is a candidate. A network
/// adapter is offerable as a class, but the adapter carrying the current connection obviously is
/// not, and the same reasoning applies to the audio device sound is coming out of. So the scan
/// cross-references the live routing and audio state and marks anything in use, which is what
/// stops the tool from politely offering to disconnect the machine.
/// </summary>
public static class DeviceInventoryScanner
{
    /// <summary>
    /// Instance-identifier prefixes that indicate real hardware sitting on a bus.
    ///
    /// Everything else enumerated under these classes is a software construct — miniports, virtual
    /// adapters, proxies. They have no hardware behind them and therefore raise no interrupts, so
    /// disabling one removes nothing at all. Offering them would pad the list with candidates that
    /// cannot possibly help while diluting the few that can.
    /// </summary>
    private static readonly string[] HardwarePrefixes =
    [
        "PCI\\", "USB\\", "HDAUDIO\\", "BTH\\", "BTHENUM\\", "ACPI\\", "HID\\", "SCSI\\"
    ];

    public static DeviceInventory Scan(AudioState audio, IReadOnlyList<NetworkAdapterState> adapters)
    {
        var handle = DeviceInterop.SetupDiGetClassDevs(
            0, 0, 0, DeviceInterop.DigcfPresent | DeviceInterop.DigcfAllClasses);

        if (handle == -1 || handle == 0)
        {
            return DeviceInventory.Unavailable("Device enumeration could not be opened.");
        }

        try
        {
            var inUse = BuildInUseSet(audio, adapters);
            var devices = new List<DeviceEntry>();

            // Kept so the System class can report what it refused and why, rather than the devices
            // people expect to see simply not appearing.
            var systemSeen = 0;
            var systemRefused = new List<string>();

            for (uint index = 0; ; index++)
            {
                var info = SpDevInfoData.Create();
                if (!DeviceInterop.SetupDiEnumDeviceInfo(handle, index, ref info))
                {
                    break;
                }

                var deviceClass = ReadProperty(handle, ref info, DeviceInterop.SpdrpClass);
                if (string.IsNullOrWhiteSpace(deviceClass))
                {
                    continue;
                }

                var name = ReadProperty(handle, ref info, DeviceInterop.SpdrpFriendlyName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = ReadProperty(handle, ref info, DeviceInterop.SpdrpDeviceDesc);
                }

                var instanceId = ReadInstanceId(handle, ref info);
                if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var isSystemClass = deviceClass.Equals("System", StringComparison.OrdinalIgnoreCase);
                if (isSystemClass)
                {
                    systemSeen++;
                }

                // Software-enumerated nodes raise no interrupts, so disabling one cannot help. This
                // is what removes most of the System class before policy sees it: the bulk of what
                // circulates as System-device tweaking is enumerated under ROOT and has no hardware.
                if (!HardwarePrefixes.Any(prefix =>
                        instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Policy is applied per device, not per class, because the System class contains
                // both things that are free to disable and things that stop the machine booting.
                if (DeviceClassPolicy.FindDeviceViolation(deviceClass, instanceId) is not null)
                {
                    if (isSystemClass && systemRefused.Count < 12)
                    {
                        systemRefused.Add(name.Trim());
                    }

                    continue;
                }

                var disabled = IsDisabled(instanceId);
                var used = inUse.Any(marker =>
                    name.Contains(marker, StringComparison.OrdinalIgnoreCase)
                    || marker.Contains(name, StringComparison.OrdinalIgnoreCase));

                // Mapping an audio endpoint back to the device behind it is not reliable by name:
                // an endpoint called "Speakers" is backed by a codec called something else
                // entirely. Where only one render endpoint exists, any audio device is a candidate
                // for backing it, so all of them are treated as in use. Being wrong here in the
                // permissive direction means offering to disable the sound card in use, which is
                // not a trade-off worth leaving to a string match.
                if (deviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)
                    && audio.Available
                    && audio.Endpoints.Count <= 1)
                {
                    used = true;
                }

                devices.Add(new DeviceEntry(instanceId, name.Trim(), deviceClass, disabled, used));
            }

            return new DeviceInventory(
                true,
                devices,
                $"{devices.Count} device(s) in offerable classes; "
                + $"{devices.Count(device => device.InUse)} currently in use, "
                + $"{devices.Count(device => device.Disabled)} already disabled.",
                systemSeen,
                systemRefused);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return DeviceInventory.Unavailable($"Device enumeration is unavailable: {exception.Message}");
        }
        finally
        {
            DeviceInterop.SetupDiDestroyDeviceInfoList(handle);
        }
    }

    /// <summary>
    /// Names of things demonstrably carrying live work right now. Matching on name is imprecise by
    /// nature, so it is used only to <em>exclude</em> candidates — a false match costs an offer,
    /// which is the harmless direction to be wrong in.
    /// </summary>
    private static List<string> BuildInUseSet(AudioState audio, IReadOnlyList<NetworkAdapterState> adapters)
    {
        var markers = new List<string>();

        foreach (var adapter in adapters.Where(entry => entry.IsActiveRoute))
        {
            markers.Add(adapter.InterfaceDescription);
            markers.Add(adapter.Name);
        }

        try
        {
            foreach (var live in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(entry => entry.OperationalStatus == OperationalStatus.Up
                                         && entry.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                markers.Add(live.Description);
            }
        }
        catch (NetworkInformationException)
        {
            // The adapter list already covers the active route; this is a refinement.
        }

        if (audio.Default is { } endpoint)
        {
            markers.Add(endpoint.FriendlyName);
        }

        return markers.Where(marker => !string.IsNullOrWhiteSpace(marker)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsDisabled(string instanceId)
    {
        if (DeviceInterop.CM_Locate_DevNodeW(out var devInst, instanceId, 0) != DeviceInterop.CrSuccess)
        {
            return false;
        }

        return DeviceInterop.CM_Get_DevNode_Status(out var status, out var problem, devInst, 0)
                   == DeviceInterop.CrSuccess
               && (status & DeviceInterop.DnHasProblem) != 0
               && problem == DeviceInterop.ProblemDisabled;
    }

    private static string ReadProperty(nint handle, ref SpDevInfoData info, uint property)
    {
        DeviceInterop.SetupDiGetDeviceRegistryProperty(
            handle, ref info, property, out _, null, 0, out var required);

        if (required == 0 || required > 8192)
        {
            return string.Empty;
        }

        var buffer = new byte[required];
        return DeviceInterop.SetupDiGetDeviceRegistryProperty(
            handle, ref info, property, out _, buffer, required, out _)
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : string.Empty;
    }

    private static string ReadInstanceId(nint handle, ref SpDevInfoData info)
    {
        var builder = new StringBuilder(512);
        return DeviceInterop.SetupDiGetDeviceInstanceId(
            handle, ref info, builder, (uint)builder.Capacity, out _)
            ? builder.ToString()
            : string.Empty;
    }
}

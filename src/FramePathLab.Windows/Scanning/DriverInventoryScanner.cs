using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reports which driver is actually bound to each device that matters for latency.
///
/// <para>
/// "Vendor driver or the one Windows installed" is one of the few tuning questions with a real
/// answer per subsystem rather than a universal one, and it is normally argued about rather than
/// checked. It does not need to be: Windows records the provider, version, date and binding service
/// for every device, so the machine can be asked instead of the internet.
/// </para>
/// <para>
/// Nothing here is written. Installing a driver is an installer's job, and a class-key write is not
/// a supported way to change one. The value is in knowing what is bound before deciding.
/// </para>
/// </summary>
public static class DriverInventoryScanner
{
    private const string ClassRootPath = @"SYSTEM\CurrentControlSet\Control\Class";

    /// <summary>
    /// The classes where the choice of driver changes latency, and where a generic and a vendor
    /// driver both realistically exist. Deliberately excludes input and USB controllers, where
    /// Windows' own driver is the only sensible option and reporting it would be noise.
    /// </summary>
    private static readonly string[] InterestingClasses =
        ["MEDIA", "Net", "Display", "HDC", "SCSIAdapter"];

    public static DriverInventory Scan()
    {
        var handle = DeviceInterop.SetupDiGetClassDevs(
            0, 0, 0, DeviceInterop.DigcfPresent | DeviceInterop.DigcfAllClasses);

        if (handle == -1 || handle == 0)
        {
            return DriverInventory.Unavailable("Driver enumeration could not be opened.");
        }

        try
        {
            var drivers = new List<InstalledDriver>();

            for (uint index = 0; ; index++)
            {
                var info = SpDevInfoData.Create();
                if (!DeviceInterop.SetupDiEnumDeviceInfo(handle, index, ref info))
                {
                    break;
                }

                var deviceClass = ReadProperty(handle, ref info, DeviceInterop.SpdrpClass);
                if (!InterestingClasses.Contains(deviceClass, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The network class is mostly software: miniports, virtual adapters, debug
                // shims. They carry driver bindings like real devices, so without this the audit
                // reports a dozen Microsoft-provided drivers that were never a choice.
                if (!HardwareEnumerator.IsRealHardware(ReadInstanceId(handle, ref info)))
                {
                    continue;
                }

                var name = ReadProperty(handle, ref info, DeviceInterop.SpdrpFriendlyName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = ReadProperty(handle, ref info, DeviceInterop.SpdrpDeviceDesc);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // SPDRP_DRIVER is the device's class-key subpath, which is where Windows records
                // what it bound and where the provider and version live.
                var driverKeyPath = ReadProperty(handle, ref info, DeviceInterop.SpdrpDriver);
                var service = ReadProperty(handle, ref info, DeviceInterop.SpdrpService);
                var (provider, version, date, inf) = ReadDriverKey(driverKeyPath);

                drivers.Add(new InstalledDriver(
                    name.Trim(),
                    deviceClass,
                    provider,
                    version,
                    date,
                    inf,
                    service));
            }

            return new DriverInventory(
                true,
                drivers,
                $"{drivers.Count} driver binding(s) read across "
                + $"{drivers.Select(driver => driver.DeviceClass).Distinct(StringComparer.OrdinalIgnoreCase).Count()} "
                + "latency-relevant device classes.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return DriverInventory.Unavailable($"Driver enumeration is unavailable: {exception.Message}");
        }
        finally
        {
            DeviceInterop.SetupDiDestroyDeviceInfoList(handle);
        }
    }

    private static (string Provider, string Version, string Date, string Inf) ReadDriverKey(string driverKeyPath)
    {
        if (string.IsNullOrWhiteSpace(driverKeyPath))
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"{ClassRootPath}\{driverKeyPath}", writable: false);
            if (key is null)
            {
                return (string.Empty, string.Empty, string.Empty, string.Empty);
            }

            return (
                key.GetValue("ProviderName") as string ?? string.Empty,
                key.GetValue("DriverVersion") as string ?? string.Empty,
                NormaliseDate(key.GetValue("DriverDate") as string),
                key.GetValue("InfPath") as string ?? string.Empty);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Windows records driver dates as M-D-YYYY in the installing locale's ordering. Parsing to a
    /// sortable form is what lets a card say how old a driver is rather than only when it was made.
    /// </summary>
    private static string NormaliseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : raw.Trim();
    }

    private static string ReadInstanceId(nint handle, ref SpDevInfoData info)
    {
        var builder = new StringBuilder(512);
        return DeviceInterop.SetupDiGetDeviceInstanceId(
            handle, ref info, builder, (uint)builder.Capacity, out _)
            ? builder.ToString()
            : string.Empty;
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
}

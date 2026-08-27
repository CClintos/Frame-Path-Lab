namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Whether a device instance sits on a real bus.
///
/// Windows enumerates a great deal that is not hardware: miniports, virtual adapters, service
/// proxies, kernel debug shims. They appear in Device Manager beside real devices and carry driver
/// bindings like real devices, which makes them indistinguishable by name — and they dominate the
/// network class by count. Nothing sensible can be said about them: there is no interrupt to remove
/// and no vendor driver to prefer, so both the device list and the driver inventory filter on the
/// enumerator rather than trying to recognise them individually.
/// </summary>
internal static class HardwareEnumerator
{
    private static readonly string[] Prefixes =
    [
        "PCI\\", "USB\\", "HDAUDIO\\", "BTH\\", "BTHENUM\\", "ACPI\\", "HID\\", "SCSI\\",
        "INTELAUDIO\\", "NVME\\", "IDE\\"
    ];

    public static bool IsRealHardware(string instanceId)
        => !string.IsNullOrWhiteSpace(instanceId)
           && Prefixes.Any(prefix => instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

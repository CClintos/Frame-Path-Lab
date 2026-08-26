namespace FramePathLab.Core.Evidence;

/// <summary>
/// Which device classes may be offered for disabling, and — more importantly — which may not.
///
/// The honest position on this technique first, because the community is split and both halves are
/// right about different things. Disabling a device does remove its interrupt and deferred-call
/// activity entirely; that part is mechanical and not in dispute. What is disputed is whether it
/// matters, and for most devices it does not, because most idle devices generate almost nothing to
/// remove. The devices where it does matter are the ones running a real driver that is doing real
/// periodic work: a second network adapter, a Bluetooth radio, an onboard audio codec left enabled
/// while sound goes out over a separate interface, vendor lighting controllers.
///
/// So this is offered as something to measure rather than something to apply. The catalogue has no
/// way to know whether a given driver on a given machine is badly behaved, and neither does a guide.
/// The paired comparison does.
///
/// The class list is a hard boundary rather than advice. Disabling the wrong device does not
/// degrade a machine, it removes the ability to use it — the controller carrying the keyboard, the
/// disk holding Windows — and no measurement is worth that risk.
/// </summary>
public static class DeviceClassPolicy
{
    /// <summary>
    /// Classes that may be offered, each with what stops working. Everything absent from this list
    /// is refused, so the default for an unrecognised class is always no.
    /// </summary>
    public static readonly Dictionary<string, string> OfferableClasses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bluetooth"] =
                "All Bluetooth stops working: audio, controllers, peripherals. The radio runs a driver "
                + "that stays active scanning even with nothing paired, which is why it is worth testing.",

            ["Net"] =
                "This adapter stops carrying traffic. Only adapters not holding the active route are "
                + "offered, so the connection in use is never a candidate. Network drivers are among "
                + "the most frequently identified sources of deferred-call time, which makes a second "
                + "idle adapter one of the better things on this list to test.",

            ["MEDIA"] =
                "This audio device stops working. Only devices that are not the default output are "
                + "offered. An onboard codec left enabled while sound goes out over a headset "
                + "interface is doing periodic work for nobody.",

            ["Image"] =
                "The camera or scanner stops working, including in applications that would otherwise "
                + "wake it — which for a webcam means streaming software and voice chat video too.",

            ["Biometric"] =
                "Fingerprint and face sign-in stop working, and so does anything that authenticates "
                + "through them. Password and PIN sign-in are unaffected, so this locks nobody out.",

            ["SmartCardReader"] =
                "This reader stops working, and any sign-in, VPN or certificate that depends on a "
                + "card in it stops with it. Common on a corporate build and almost never present "
                + "on a machine built for playing games.",

            ["SmartCardFilter"] =
                "The filter sitting above a smart card reader stops loading, which stops the reader "
                + "it belongs to from being usable even where the reader itself is still enabled.",

            ["Sensor"] =
                "Ambient light, orientation and related sensors stop reporting. Adaptive brightness "
                + "and rotation stop working with them.",

            ["Printer"] =
                "This printer stops being available to everything on the machine. The print queue "
                + "service is offered separately, under Services, and is the broader of the two.",

            ["MTD"] =
                "This memory technology device stops being available, along with anything stored on "
                + "it. Almost always a firmware or embedded flash part rather than something in use.",

            ["Modem"] =
                "This modem stops working, including any dial-up or cellular connection through it. "
                + "Rarely present on a desktop, and rarer still to be carrying anything."
        };

    /// <summary>
    /// Classes that are never offered whatever else is true. This is not a list of things that
    /// would merely be inconvenient — these are the ones where a wrong disable costs the use of
    /// the machine, or removes a protection, rather than costing frame time.
    /// </summary>
    public static readonly string[] NeverOfferedClasses =
    [
        // Losing these loses control of the machine.
        "HIDClass", "Keyboard", "Mouse", "USB", "System", "Computer", "Processor",

        // Losing these loses the machine outright.
        "DiskDrive", "Volume", "VolumeSnapshot", "SCSIAdapter", "hdc", "SDHost",

        // The display path, the firmware surface and security devices.
        "Display", "Monitor", "SecurityDevices", "SecurityAccelerator",
        "Firmware", "SoftwareDevice", "SoftwareComponent",

        // Buses and the plumbing beneath everything above.
        "PCMCIA", "1394", "Ports", "MultifunctionAdapter", "Battery"
    ];

    /// <summary>Returns null when the class may be offered, or the reason it may not.</summary>
    public static string? FindClassViolation(string deviceClass)
    {
        if (string.IsNullOrWhiteSpace(deviceClass))
        {
            return "A device with no class is never offered.";
        }

        if (NeverOfferedClasses.Contains(deviceClass, StringComparer.OrdinalIgnoreCase))
        {
            return $"The '{deviceClass}' class is never offered: a wrong disable there costs the use of "
                   + "the machine rather than costing frame time.";
        }

        return OfferableClasses.ContainsKey(deviceClass)
            ? null
            : $"The '{deviceClass}' class is not one this application offers to disable.";
    }
}

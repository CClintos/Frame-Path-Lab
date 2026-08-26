namespace FramePathLab.Core.Evidence;

/// <summary>What stops working, so the trade is stated rather than implied.</summary>
public enum ServiceLoss
{
    /// <summary>Nothing a desktop uses. Vestigial or superseded.</summary>
    None,

    /// <summary>A feature disappears, and it is obvious which one.</summary>
    Feature,

    /// <summary>Convenience degrades: something gets slower or more manual, not impossible.</summary>
    Convenience,

    /// <summary>Reduces a diagnostic or recovery capability you would want after a fault.</summary>
    Diagnostics
}

public sealed record ServiceCandidate(
    string ServiceName,
    string DisplayName,
    string WhatItDoes,
    string WhatYouLose,
    ServiceLoss Loss,
    string OnlyIf,
    bool SafeByDefault);

/// <summary>
/// Services that can be switched off deliberately, each with the functionality it costs.
///
/// This is not a debloat list, and the distinction is the entire point. A debloat script flips
/// dozens of settings at once: nothing is attributable, nothing is individually reversible, and
/// several entries are load-bearing for something the author did not think about. That remains
/// excluded.
///
/// What is offered here is one service at a time, each stating what stops working, each recording
/// its exact prior start type, and each reversible on its own. Every candidate is checked against
/// the live dependency graph before it is offered, because the way this actually breaks a machine
/// is disabling something another service quietly requires.
///
/// On what it buys: mostly not frame rate. Services cost background wakeups, disk activity and
/// memory, and a few — content indexing especially — produce real disk and processor spikes that
/// land inside frames. Several of these will measure as doing nothing at all, which is a fine
/// outcome and exactly why the paired comparison exists. Do not take this list on faith; measure it.
/// </summary>
public static class ServiceCatalog
{
    /// <summary>
    /// Services this application will never offer to touch, because something load-bearing depends
    /// on them or because switching them off removes a protection rather than a convenience.
    ///
    /// The event log deserves a specific mention: a great many services depend on it, and this
    /// application's own hardware-error reading — the only stability signal that covers idle —
    /// comes from it.
    /// </summary>
    public static readonly string[] NeverOffered =
    [
        "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay", "Power", "Schedule",
        "EventLog", "EventSystem", "UserManager", "ProfSvc", "Themes",
        "Audiosrv", "AudioEndpointBuilder", "CryptSvc", "Dhcp", "Dnscache",
        "BFE", "MpsSvc", "WinDefend", "SecurityHealthService", "wscsvc",
        "nsi", "NlaSvc", "netprofm", "LanmanWorkstation", "gpsvc", "SamSs",
        "StateRepository", "TextInputManagementService", "CoreMessagingRegistrar",
        "SystemEventsBroker", "TimeBrokerSvc", "WpnService"
    ];

    /// <summary>
    /// The curated set, grouped by the question that decides each one. Order is roughly by how
    /// obviously safe the trade is.
    /// </summary>
    public static readonly ServiceCandidate[] Candidates =
    [
        // --- Nothing to lose on a desktop ---------------------------------------------------
        new("Fax", "Fax",
            "Sends and receives faxes through a fax modem.",
            "Nothing. This requires a fax modem, and one is almost certainly not present.",
            ServiceLoss.None, "Always safe on a machine with no fax modem.", true),

        new("RetailDemo", "Retail Demo Service",
            "Runs the in-store demonstration mode used on shop display machines.",
            "Nothing. This should never run on a machine someone owns.",
            ServiceLoss.None, "Always safe outside a retail display.", true),

        new("AJRouter", "AllJoyn Router Service",
            "Routes messages for the AllJoyn internet-of-things protocol.",
            "Nothing, unless AllJoyn smart-home devices are in use, which is rare.",
            ServiceLoss.None, "Safe unless AllJoyn devices are paired to this machine.", true),

        new("wisvc", "Windows Insider Service",
            "Enrols the machine in preview builds and reports back on them.",
            "Nothing unless the machine is on the Insider programme.",
            ServiceLoss.None, "Safe unless this machine runs preview builds.", true),

        new("MapsBroker", "Downloaded Maps Manager",
            "Downloads and updates offline map data in the background.",
            "Offline maps stop updating. The Maps application still works online.",
            ServiceLoss.None, "Safe unless offline maps are used.", true),

        // --- A feature disappears, and you will know which -----------------------------------
        new("Spooler", "Print Spooler",
            "Queues print jobs and manages printer connections.",
            "Printing stops entirely, including printing to a file or to a document writer.",
            ServiceLoss.Feature, "Only if nothing is ever printed from this machine.", false),

        new("PrintNotify", "Printer Extensions and Notifications",
            "Shows printer notifications and vendor-supplied printer dialogs.",
            "Printer notifications stop. Printing itself is unaffected.",
            ServiceLoss.Feature, "Only if nothing is ever printed from this machine.", false),

        new("LanmanServer", "Server",
            "Shares this machine's files and printers with other machines on the network.",
            "Other machines can no longer reach shares hosted here. Reaching shares hosted "
            + "elsewhere is unaffected — that is a different service, and it is never offered.",
            ServiceLoss.Feature, "Only if this machine does not host network shares.", false),

        new("TermService", "Remote Desktop Services",
            "Accepts incoming remote desktop connections.",
            "Nobody can connect to this machine remotely. Connecting out to other machines is "
            + "unaffected.",
            ServiceLoss.Feature, "Only if nothing ever connects to this machine remotely.", false),

        new("SessionEnv", "Remote Desktop Configuration",
            "Configures incoming remote desktop sessions.",
            "Incoming remote desktop stops working.",
            ServiceLoss.Feature, "Only if nothing ever connects to this machine remotely.", false),

        new("RemoteRegistry", "Remote Registry",
            "Lets other machines read and modify this machine's registry over the network.",
            "Nothing locally, and it closes a remote attack surface. Usually already disabled.",
            ServiceLoss.None, "Safe on any machine that is not remotely administered.", true),

        new("bthserv", "Bluetooth Support Service",
            "Discovers and connects Bluetooth devices.",
            "All Bluetooth stops working, including audio, controllers and peripherals.",
            ServiceLoss.Feature, "Only if no Bluetooth device is used — check your headset and mouse.", false),

        new("icssvc", "Windows Mobile Hotspot Service",
            "Shares this machine's connection as a wireless hotspot.",
            "The mobile hotspot feature stops working.",
            ServiceLoss.Feature, "Safe unless this machine is used as a hotspot.", true),

        new("WbioSrvc", "Windows Biometric Service",
            "Drives fingerprint and face sign-in.",
            "Biometric sign-in stops. Password and PIN sign-in are unaffected.",
            ServiceLoss.Feature, "Only if fingerprint or face sign-in is not used.", false),

        new("SCardSvr", "Smart Card",
            "Manages smart card readers.",
            "Smart card sign-in and certificates stop working.",
            ServiceLoss.Feature, "Safe unless a smart card is used to sign in.", true),

        new("ScDeviceEnum", "Smart Card Device Enumeration Service",
            "Enumerates smart card devices for applications.",
            "Smart card devices stop being detected.",
            ServiceLoss.Feature, "Safe unless a smart card is used to sign in.", true),

        new("SEMgrSvc", "Payments and NFC/SE Manager",
            "Manages near-field payment hardware.",
            "Tap-to-pay stops working. Requires hardware most desktops do not have.",
            ServiceLoss.Feature, "Safe on a machine with no near-field payment hardware.", true),

        new("PhoneSvc", "Phone Service",
            "Manages telephony state for phone-linked features.",
            "Phone integration features stop working.",
            ServiceLoss.Feature, "Safe unless a phone is linked to this machine.", true),

        new("TabletInputService", "Touch Keyboard and Handwriting Panel Service",
            "Provides the on-screen keyboard and handwriting input.",
            "The on-screen keyboard and handwriting panel stop working. A physical keyboard is "
            + "unaffected. Note that some applications summon this panel for text entry.",
            ServiceLoss.Feature, "Only on a machine with no touch screen or stylus.", false),

        new("lfsvc", "Geolocation Service",
            "Reports the machine's location to applications that request it.",
            "Location-aware applications lose location. Some weather and time-zone features degrade.",
            ServiceLoss.Feature, "Safe on a desktop that does not move.", true),

        new("FrameServer", "Windows Camera Frame Server",
            "Lets several applications share one camera at the same time.",
            "Camera sharing between applications stops; single-application camera use may still work.",
            ServiceLoss.Feature, "Only if the webcam is unused, or never shared between applications.", false),

        new("WalletService", "WalletService",
            "Stores payment and loyalty cards for store applications.",
            "Wallet features in store applications stop working.",
            ServiceLoss.Feature, "Safe unless store wallet features are used.", true),

        new("WpcMonSvc", "Parental Controls",
            "Enforces family safety restrictions on this machine.",
            "Family safety restrictions stop being enforced.",
            ServiceLoss.Feature, "Safe unless family safety is configured on this machine.", true),

        new("CscService", "Offline Files",
            "Caches network files for use while disconnected.",
            "Offline access to network files stops.",
            ServiceLoss.Feature, "Safe unless offline files are configured against a network share.", true),

        // --- Network discovery, only useful with devices to discover --------------------------
        new("SSDPSRV", "SSDP Discovery",
            "Discovers devices that advertise themselves over the network, such as media players.",
            "Network media devices and some smart-home devices stop being discovered.",
            ServiceLoss.Feature, "Only if no network media or discovery devices are used.", false),

        new("upnphost", "UPnP Device Host",
            "Hosts this machine as a discoverable network device.",
            "This machine stops advertising itself to media players and similar.",
            ServiceLoss.Feature, "Only if this machine does not need to be discovered.", false),

        new("FDResPub", "Function Discovery Resource Publication",
            "Publishes this machine and its resources on the local network.",
            "This machine stops appearing in other machines' network views.",
            ServiceLoss.Feature, "Only if this machine does not need to be visible on the network.", false),

        // --- Background activity: the ones most likely to actually show up --------------------
        new("WSearch", "Windows Search",
            "Continuously indexes file content so search results are instant.",
            "Searching inside files becomes slow, because it falls back to scanning on demand. "
            + "Finding applications from the start menu is unaffected.",
            ServiceLoss.Convenience,
            "Worth measuring: indexing is one of the few here that produces real disk and "
            + "processor activity landing inside frames.", false),

        new("SysMain", "SysMain",
            "Watches usage patterns and preloads what it expects to be needed.",
            "Applications may take marginally longer to start cold.",
            ServiceLoss.Convenience,
            "Designed for mechanical disks. On solid-state storage the benefit is small and the "
            + "background activity is not. Measure it.", false),

        new("DiagTrack", "Connected User Experiences and Telemetry",
            "Collects and uploads diagnostic and usage data.",
            "Diagnostic reporting stops. Some feedback and troubleshooting features degrade.",
            ServiceLoss.Diagnostics, "Safe. Note that the trace session it feeds is separate and "
            + "has its own entry.", true),

        new("dmwappushservice", "Device Management Wireless Application Protocol Push",
            "Routes device-management messages, largely used by enterprise management.",
            "Enterprise device management messaging stops.",
            ServiceLoss.Feature, "Safe on a machine that is not centrally managed.", true),

        new("DPS", "Diagnostic Policy Service",
            "Runs the built-in troubleshooters and diagnoses network and hardware problems.",
            "Windows troubleshooters stop working, including the network diagnostics wizard.",
            ServiceLoss.Diagnostics,
            "Weigh this one: it costs you the tool you would reach for when something breaks.", false),

        new("WerSvc", "Windows Error Reporting",
            "Collects crash data and offers to send it.",
            "Crash reports stop being collected. Local crash dumps are unaffected.",
            ServiceLoss.Diagnostics, "Safe, at the cost of crash telemetry that occasionally "
            + "produces a driver fix.", true),

        new("PcaSvc", "Program Compatibility Assistant",
            "Detects applications with compatibility problems and applies shims.",
            "Automatic compatibility fixes stop being applied to older software.",
            ServiceLoss.Convenience, "Safe for modern software; older titles may rely on a shim.", false),

        new("TrkWks", "Distributed Link Tracking Client",
            "Maintains shortcuts to files that have been moved or renamed.",
            "Shortcuts to moved files stop repairing themselves automatically.",
            ServiceLoss.Convenience, "Safe on a machine that does not rely on link tracking.", true),

        new("seclogon", "Secondary Logon",
            "Runs programs as a different user account.",
            "Run-as-different-user stops working, including some installer prompts.",
            ServiceLoss.Convenience, "Safe unless run-as is used regularly.", false),

        new("SensorService", "Sensor Service",
            "Manages ambient light, orientation and other sensors.",
            "Adaptive brightness and rotation stop working.",
            ServiceLoss.Feature, "Safe on a desktop with no sensors.", true),

        new("SensrSvc", "Sensor Monitoring Service",
            "Monitors sensors and adjusts brightness accordingly.",
            "Adaptive brightness stops working.",
            ServiceLoss.Feature, "Safe on a desktop with no sensors.", true)
    ];
}

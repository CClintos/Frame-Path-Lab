using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

/// <summary>
/// The check applied before every write and restore. Production always uses the compiled-in
/// allowlist; this exists so a test can exercise the engine against a scratch location without
/// that location ever being reachable in a shipped build.
/// </summary>
public interface IMutationGuard
{
    string? FindViolation(MutationPlan plan);

    string? FindViolation(MutationRecord record);
}

/// <summary>The shipping guard: nothing outside <see cref="MutationAllowlist"/> may be written.</summary>
public sealed class AllowlistMutationGuard : IMutationGuard
{
    public static AllowlistMutationGuard Instance { get; } = new();

    public string? FindViolation(MutationPlan plan) => MutationAllowlist.FindViolation(plan);

    public string? FindViolation(MutationRecord record) => MutationAllowlist.FindViolation(record);
}

/// <summary>
/// The complete set of locations this application is permitted to write, checked immediately
/// before every write and every restore.
///
/// The reason this exists rather than trusting the ledger: the ledger describes writes as data, in
/// a file the signed-in user can edit, and a restore replays that data. Without an independent
/// check, anything able to edit that file could choose what an elevated restore writes and where —
/// the ledger would become a command channel rather than a record. Its integrity hash cannot close
/// that, because whoever can rewrite the file can recompute the hash.
///
/// The list is keyed by registry key <em>and</em> value name, never by key alone. Several keys the
/// catalogue legitimately writes also hold values it must never touch: the memory-management key
/// carries the speculative-execution mitigation override, and the graphics-driver key carries the
/// timeout-detection settings. Allowing a key wholesale would hand a tampered ledger exactly the
/// values this product refuses to write.
/// </summary>
public static class MutationAllowlist
{
    /// <summary>Registry key to the exact value names permitted beneath it.</summary>
    private static readonly Dictionary<string, string[]> PermittedRegistryValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [@"HKCU\System\GameConfigStore"] =
                ["GameDVR_Enabled", "GameDVR_FSEBehavior"],
            [@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR"] =
                ["AppCaptureEnabled"],
            [@"HKCU\Software\Microsoft\GameBar"] =
                ["AutoGameModeEnabled"],
            [@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences"] =
                ["DirectXUserGlobalSettings", "*"],
            [@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"] =
                ["*"],
            [@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications"] =
                ["GlobalUserDisabled"],
            [@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"] =
                ["EnableTransparency"],
            [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"] =
                ["HiberbootEnabled"],
            [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel"] =
                ["GlobalTimerResolutionRequests"],

            // This key also holds FeatureSettingsOverride, the speculative-execution mitigation
            // switch. Only the paging value is reachable.
            [@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"] =
                ["DisablePagingExecutive"],

            // Shares a key with SystemResponsiveness; both are permitted, nothing else is.
            [@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"] =
                ["SystemResponsiveness", "NetworkThrottlingIndex"],

            [@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling"] =
                ["PowerThrottlingOff"],
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR"] =
                ["AllowGameDVR"],
            [@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"] =
                ["DODownloadMode"],
            [@"HKLM\SYSTEM\CurrentControlSet\Control\WMI\Autologger\DiagTrack-Listener"] =
                ["Start"]
        };

    /// <summary>
    /// Network adapters live under a per-instance key beneath the network class, so this one is a
    /// prefix. The trailing segment is still constrained: only a numeric instance is accepted.
    /// </summary>
    private const string NetworkClassPrefix =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\";

    /// <summary>Value names permitted under the network adapter instance keys.</summary>
    private static readonly string[] PermittedNetworkValues =
    [
        "*InterruptModeration",
        "*EEE",
        "EnableGreenEthernet",
        "AdvancedEEE",
        "EnableSavePowerNow",
        "*EnergyEfficientEthernet",
        "*FlowControl",
        "*RscIPv4",
        "*RscIPv6",

        // The "allow the computer to turn off this device to save power" checkbox. A DWord rather
        // than an NDIS keyword, and permitted for the same reason as the rest: it decides whether
        // the adapter is allowed to stop being ready.
        "PnPCapabilities"
    ];

    /// <summary>
    /// Service start types are reachable only for the curated candidates, and only the start value
    /// itself. The services root holds every driver and service on the machine, so permitting the
    /// prefix wholesale would hand a tampered ledger the ability to disable anything — including
    /// the security services this catalogue explicitly refuses to touch.
    /// </summary>
    private const string ServicesPrefix = @"HKLM\SYSTEM\CurrentControlSet\Services\";

    private static readonly string[] PermittedSystemParameters =
    [
        "pointer.acceleration",
        "pointer.speed"
    ];

    /// <summary>
    /// The power subgroups this catalogue touches. Everything else under the power API stays
    /// unreachable, including the sleep, battery and display subgroups, so a tampered ledger cannot
    /// reach settings the catalogue never offers.
    /// </summary>
    private static readonly string[] PermittedPowerSubgroups =
    [
        "54533251-82be-4824-96c1-47b60b740d00", // processor
        "501a4d13-42af-4429-9fd1-a8218c268e20", // pcie
        "0012ee47-9041-4b5d-9b77-535fba8b1442"  // disk
    ];

    /// <summary>
    /// Returns null when the target is permitted, or the reason it is refused.
    /// </summary>
    public static string? FindViolation(MutationKind kind, string target, string valueName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(valueName);

        return kind switch
        {
            MutationKind.RegistryValue => CheckRegistry(target, valueName),
            MutationKind.SystemParameter => PermittedSystemParameters.Contains(target, StringComparer.Ordinal)
                ? null
                : $"'{target}' is not a permitted system parameter.",
            MutationKind.PowerSchemeValue => CheckPowerSetting(target),
            MutationKind.PowerOverlayScheme => null,

            // The class travels in the value name so the guard can check it without needing to
            // re-enumerate hardware, which a restore replayed from the ledger cannot rely on doing.
            // Class alone does not decide this. The System class holds both the PCI bus and a
            // virtual drive enumerator, so the check is per device, against the instance identifier
            // the plan targets.
            MutationKind.DeviceState => DeviceClassPolicy.FindDeviceViolation(valueName, target),

            // Nothing reaches into a running process, and boot configuration is never written.
            MutationKind.ProcessAffinity or MutationKind.ProcessPriority
                or MutationKind.ProcessPowerThrottling =>
                "Writes into a running process are outside the product boundary.",
            MutationKind.BootConfigurationValue =>
                "Boot configuration is reported for review only and is never written.",
            _ => $"Mutation kind {kind} is not permitted."
        };
    }

    public static string? FindViolation(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return FindViolation(plan.Kind, plan.Target, plan.ValueName);
    }

    public static string? FindViolation(MutationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return FindViolation(record.Kind, record.Target, record.ValueName);
    }

    private static string? CheckRegistry(string target, string valueName)
    {
        if (PermittedRegistryValues.TryGetValue(target, out var values))
        {
            // A "*" entry means the key is keyed by executable name, so the value name cannot be
            // enumerated in advance. Those keys hold nothing but per-application preferences.
            return values.Contains("*", StringComparer.Ordinal)
                   || values.Contains(valueName, StringComparer.OrdinalIgnoreCase)
                ? null
                : $"'{valueName}' is not a permitted value under '{target}'.";
        }

        if (target.StartsWith(ServicesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var serviceName = target[ServicesPrefix.Length..];

            // A nested key under a service must never match; only the service key itself.
            if (serviceName.Contains('\\'))
            {
                return $"'{target}' is not a permitted service key.";
            }

            if (!string.Equals(valueName, "Start", StringComparison.OrdinalIgnoreCase))
            {
                return $"Only the start type may be written on a service; '{valueName}' may not.";
            }

            if (ServiceCatalog.NeverOffered.Contains(serviceName, StringComparer.OrdinalIgnoreCase))
            {
                return $"'{serviceName}' is on the never-offered list and may not be changed.";
            }

            return ServiceCatalog.Candidates.Any(candidate =>
                candidate.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"'{serviceName}' is not a service this application offers to change.";
        }

        if (target.StartsWith(NetworkClassPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var instance = target[NetworkClassPrefix.Length..];

            // Only a bare four-digit instance may follow. This stops a traversal into any deeper
            // key under the network class, such as a driver's own subkeys.
            if (instance.Length is not 4 || !instance.All(char.IsAsciiDigit))
            {
                return $"'{target}' is not a permitted network adapter instance key.";
            }

            return PermittedNetworkValues.Contains(valueName, StringComparer.OrdinalIgnoreCase)
                ? null
                : $"'{valueName}' is not a permitted network adapter value.";
        }

        return $"'{target}' is not on the list of keys this application may write.";
    }

    private static string? CheckPowerSetting(string target)
    {
        // The capture binds the scheme GUID onto the front as "{scheme}|{subgroup}:{setting}".
        var withoutScheme = target.Contains('|', StringComparison.Ordinal)
            ? target[(target.IndexOf('|', StringComparison.Ordinal) + 1)..]
            : target;

        return PermittedPowerSubgroups.Any(subgroup =>
            withoutScheme.StartsWith(subgroup, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"'{target}' is not in a permitted power subgroup.";
    }
}

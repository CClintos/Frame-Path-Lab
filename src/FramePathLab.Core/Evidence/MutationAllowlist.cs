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
        "*RscIPv6"
    ];

    private static readonly string[] PermittedSystemParameters =
    [
        "pointer.acceleration",
        "pointer.speed"
    ];

    /// <summary>The processor subgroup is the only power subgroup this catalogue touches.</summary>
    private const string ProcessorSubgroup = "54533251-82be-4824-96c1-47b60b740d00";

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

        return withoutScheme.StartsWith(ProcessorSubgroup, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"'{target}' is not a permitted power setting.";
    }
}

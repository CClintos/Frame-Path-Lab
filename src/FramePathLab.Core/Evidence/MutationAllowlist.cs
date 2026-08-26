using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

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
/// So the ledger is treated as untrusted input. It may only ever name a location on this list, and
/// the list is compiled in. Blocking privileged writes outright would close the same hole by making
/// the product unable to do its job; this closes it while leaving the job possible.
/// </summary>
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

public static class MutationAllowlist
{
    /// <summary>Registry keys that may be written, matched case-insensitively as exact keys.</summary>
    private static readonly string[] PermittedRegistryKeys =
    [
        @"HKCU\System\GameConfigStore",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR",
        @"HKCU\Software\Microsoft\GameBar",
        @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
    ];

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
        "*FlowControl"
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
        if (PermittedRegistryKeys.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return null;
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

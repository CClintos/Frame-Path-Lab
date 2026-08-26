using System.Diagnostics;
using System.Globalization;
using System.Security;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;
using Microsoft.Win32;

namespace FramePathLab.Windows.Mutation;

/// <summary>
/// The single place in FramePath Lab that changes system state.
///
/// Every path follows the same contract: capture the exact prior value, write, read back, and
/// report the read-back rather than the request. Reverts compare before writing, so a value that
/// something else changed after this transaction is left alone instead of being clobbered.
/// </summary>
public sealed class WindowsMutationExecutor : IMutationExecutor
{
    public const string MouseAccelerationTarget = "pointer.acceleration";
    public const string MouseSpeedTarget = "pointer.speed";

    private static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");

    public string? Read(MutationPlan plan, out bool exists)
    {
        ArgumentNullException.ThrowIfNull(plan);
        switch (plan.Kind)
        {
            case MutationKind.RegistryValue:
                return ReadRegistry(plan, out exists);
            case MutationKind.SystemParameter:
                exists = true;
                return ReadSystemParameter(plan);
            case MutationKind.PowerSchemeValue:
                return ReadPowerSchemeValue(plan, out exists);
            case MutationKind.PowerOverlayScheme:
                return ReadOverlayScheme(out exists);
            case MutationKind.ProcessAffinity:
                return ReadProcessAffinity(plan, out exists);
            case MutationKind.ProcessPriority:
                return ReadProcessPriority(plan, out exists);
            case MutationKind.ProcessPowerThrottling:
                return ReadProcessPowerThrottling(plan, out exists);
            case MutationKind.BootConfigurationValue:
            default:
                exists = false;
                return null;
        }
    }

    public MutationRecord Apply(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Kind == MutationKind.BootConfigurationValue)
        {
            throw new NotSupportedException(
                "Boot configuration values are reported for review only and are never written by FramePath Lab.");
        }

        var before = Read(plan, out var existedBefore);

        Write(plan, plan.DesiredValue);

        var after = Read(plan, out _);
        var verified = ValuesMatch(plan, after, plan.DesiredValue);

        return new MutationRecord(
            plan.MutationId,
            plan.Kind,
            plan.Target,
            plan.ValueName,
            plan.ValueType,
            plan.Description,
            existedBefore,
            before,
            plan.DesiredValue,
            after,
            verified,
            verified
                ? $"Applied and verified: {Describe(before, existedBefore)} to {after}."
                : $"Write did not verify. Requested {plan.DesiredValue}, system reports {after ?? "no value"}.");
    }

    public MutationRecord Revert(MutationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var plan = ToPlan(record);
        var live = Read(plan, out var liveExists);

        // Compare before write. If the live value is neither what we wrote nor already the prior
        // value, something else owns this setting now and that newer state is preserved.
        if (!ValuesMatch(plan, live, record.DesiredValue)
            && !ValuesMatch(plan, live, record.BeforeValue))
        {
            return record with
            {
                ObservedAfterValue = live,
                Observation =
                    $"Left unchanged: the value is now {live ?? "absent"}, which this transaction did not write. "
                    + "A later external change was preserved."
            };
        }

        if (!record.ExistedBefore && record.Kind == MutationKind.RegistryValue)
        {
            DeleteRegistryValue(plan);
            var afterDelete = Read(plan, out var stillExists);
            return record with
            {
                ObservedAfterValue = afterDelete,
                VerifiedAfterWrite = !stillExists,
                Observation = stillExists
                    ? "Revert incomplete: the value could not be removed."
                    : "Reverted by removing the value FramePath Lab created."
            };
        }

        if (record.BeforeValue is null)
        {
            return record with
            {
                ObservedAfterValue = live,
                VerifiedAfterWrite = false,
                Observation = "No prior value was captured, so no restore was attempted."
            };
        }

        Write(plan, record.BeforeValue);
        var restored = Read(plan, out _);
        var verified = ValuesMatch(plan, restored, record.BeforeValue);
        return record with
        {
            ObservedAfterValue = restored,
            VerifiedAfterWrite = verified,
            Observation = verified
                ? $"Restored to {restored}."
                : $"Restore did not verify. Expected {record.BeforeValue}, system reports {restored ?? "no value"}."
        };
    }

    public bool RequiresElevation(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Kind switch
        {
            MutationKind.RegistryValue => plan.Target.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase),
            MutationKind.BootConfigurationValue => true,
            _ => false
        };
    }

    private void Write(MutationPlan plan, string value)
    {
        switch (plan.Kind)
        {
            case MutationKind.RegistryValue:
                WriteRegistry(plan, value);
                break;
            case MutationKind.SystemParameter:
                WriteSystemParameter(plan, value);
                break;
            case MutationKind.PowerSchemeValue:
                WritePowerSchemeValue(plan, value);
                break;
            case MutationKind.PowerOverlayScheme:
                WriteOverlayScheme(value);
                break;
            case MutationKind.ProcessAffinity:
                WriteProcessAffinity(plan, value);
                break;
            case MutationKind.ProcessPriority:
                WriteProcessPriority(plan, value);
                break;
            case MutationKind.ProcessPowerThrottling:
                WriteProcessPowerThrottling(plan, value);
                break;
            default:
                throw new NotSupportedException($"Mutation kind {plan.Kind} cannot be written.");
        }
    }

    private static MutationPlan ToPlan(MutationRecord record)
        => new(
            record.MutationId,
            record.Kind,
            record.Target,
            record.ValueName,
            record.DesiredValue,
            record.ValueType,
            record.Description);

    private static string Describe(string? value, bool existed)
        => existed ? value ?? "no value" : "absent";

    private static bool ValuesMatch(MutationPlan plan, string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (plan.Kind is MutationKind.PowerOverlayScheme)
        {
            return Guid.TryParse(left, out var leftGuid)
                   && Guid.TryParse(right, out var rightGuid)
                   && leftGuid == rightGuid;
        }

        if (long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber)
            && long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Registry -------------------------------------------------------------------------

    private static (RegistryKey Hive, string Path) SplitRegistryTarget(string target)
    {
        var separator = target.IndexOf('\\', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException($"Registry target '{target}' is not a hive-qualified path.", nameof(target));
        }

        var hiveName = target[..separator];
        var path = target[(separator + 1)..];
        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Unsupported registry hive '{hiveName}'.", nameof(target))
        };

        return (hive, path);
    }

    private static string? ReadRegistry(MutationPlan plan, out bool exists)
    {
        var (hive, path) = SplitRegistryTarget(plan.Target);
        try
        {
            using var key = hive.OpenSubKey(path, writable: false);
            var value = key?.GetValue(plan.ValueName);
            exists = value is not null;
            return value switch
            {
                null => null,
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
        }
        catch (SecurityException)
        {
            exists = false;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            exists = false;
            return null;
        }
    }

    private static void WriteRegistry(MutationPlan plan, string value)
    {
        var (hive, path) = SplitRegistryTarget(plan.Target);
        using var key = hive.CreateSubKey(path, writable: true)
                        ?? throw new InvalidOperationException($"Registry key '{plan.Target}' could not be opened for writing.");

        if (string.Equals(plan.ValueType, "DWord", StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(
                plan.ValueName,
                int.Parse(value, CultureInfo.InvariantCulture),
                RegistryValueKind.DWord);
            return;
        }

        key.SetValue(plan.ValueName, value, RegistryValueKind.String);
    }

    private static void DeleteRegistryValue(MutationPlan plan)
    {
        var (hive, path) = SplitRegistryTarget(plan.Target);
        using var key = hive.OpenSubKey(path, writable: true);
        key?.DeleteValue(plan.ValueName, throwOnMissingValue: false);
    }

    // ---- Pointer behaviour ----------------------------------------------------------------

    private static string ReadSystemParameter(MutationPlan plan)
    {
        if (string.Equals(plan.Target, MouseAccelerationTarget, StringComparison.Ordinal))
        {
            var mouse = new int[3];
            return ExpertNativeMethods.SystemParametersInfo(ExpertNativeMethods.SpiGetMouse, 0, mouse, 0)
                ? (mouse[2] != 0 ? "1" : "0")
                : "0";
        }

        var speed = 0;
        ExpertNativeMethods.SystemParametersInfo(ExpertNativeMethods.SpiGetMouseSpeed, 0, ref speed, 0);
        return speed.ToString(CultureInfo.InvariantCulture);
    }

    private static void WriteSystemParameter(MutationPlan plan, string value)
    {
        const uint flags = ExpertNativeMethods.SpifUpdateIniFile | ExpertNativeMethods.SpifSendChange;
        if (string.Equals(plan.Target, MouseAccelerationTarget, StringComparison.Ordinal))
        {
            var mouse = new int[3];
            if (!ExpertNativeMethods.SystemParametersInfo(ExpertNativeMethods.SpiGetMouse, 0, mouse, 0))
            {
                throw new InvalidOperationException("Current pointer acceleration state could not be read.");
            }

            // Thresholds are retained; only the acceleration flag itself is toggled.
            mouse[2] = int.Parse(value, CultureInfo.InvariantCulture) != 0 ? 1 : 0;
            if (!ExpertNativeMethods.SystemParametersInfo(ExpertNativeMethods.SpiSetMouse, 0, mouse, flags))
            {
                throw new InvalidOperationException("Pointer acceleration could not be written.");
            }

            return;
        }

        var speed = int.Parse(value, CultureInfo.InvariantCulture);
        if (!ExpertNativeMethods.SystemParametersInfoSetValue(
                ExpertNativeMethods.SpiSetMouseSpeed, 0, speed, flags))
        {
            throw new InvalidOperationException("Pointer speed could not be written.");
        }
    }

    // ---- Power ----------------------------------------------------------------------------

    private static Guid ResolveActiveScheme()
    {
        var result = NativeMethods.PowerGetActiveScheme(0, out var pointer);
        if (result != 0 || pointer == 0)
        {
            throw new InvalidOperationException("The active power scheme could not be read.");
        }

        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    private static (Guid Subgroup, Guid Setting) ParsePowerTarget(string target)
    {
        var parts = target.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var subgroup) || !Guid.TryParse(parts[1], out var setting))
        {
            // A bare setting GUID defaults to the processor subgroup, which is where every
            // processor-policy tweak in this catalogue lives.
            return Guid.TryParse(target, out var only)
                ? (ProcessorSubgroup, only)
                : throw new ArgumentException($"Power target '{target}' is not a subgroup:setting pair.", nameof(target));
        }

        return (subgroup, setting);
    }

    private static string? ReadPowerSchemeValue(MutationPlan plan, out bool exists)
    {
        var (subgroup, setting) = ParsePowerTarget(plan.Target);
        var scheme = ResolveActiveScheme();
        var status = ExpertNativeMethods.PowerReadACValueIndex(0, ref scheme, ref subgroup, ref setting, out var value);
        exists = status == 0;
        return exists ? value.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static void WritePowerSchemeValue(MutationPlan plan, string value)
    {
        var (subgroup, setting) = ParsePowerTarget(plan.Target);
        var scheme = ResolveActiveScheme();
        var status = ExpertNativeMethods.PowerWriteACValueIndex(
            0, ref scheme, ref subgroup, ref setting, uint.Parse(value, CultureInfo.InvariantCulture));
        if (status != 0)
        {
            throw new InvalidOperationException($"Power setting write failed with status {status}.");
        }

        // Windows only materialises a written scheme value when the scheme is re-activated.
        ExpertNativeMethods.PowerSetActiveScheme(0, ref scheme);
    }

    private static string? ReadOverlayScheme(out bool exists)
    {
        try
        {
            var status = ExpertNativeMethods.PowerGetEffectiveOverlayScheme(out var overlay);
            exists = status == 0;
            return exists ? overlay.ToString("D") : null;
        }
        catch (EntryPointNotFoundException)
        {
            exists = false;
            return null;
        }
    }

    private static void WriteOverlayScheme(string value)
    {
        var status = ExpertNativeMethods.PowerSetActiveOverlayScheme(Guid.Parse(value));
        if (status != 0)
        {
            throw new InvalidOperationException($"Power mode overlay write failed with status {status}.");
        }
    }

    // ---- Process --------------------------------------------------------------------------

    private static Process? FindProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            return null;
        }

        for (var index = 1; index < processes.Length; index++)
        {
            processes[index].Dispose();
        }

        return processes[0];
    }

    private static string? ReadProcessAffinity(MutationPlan plan, out bool exists)
    {
        using var process = FindProcess(plan.Target);
        if (process is null)
        {
            exists = false;
            return null;
        }

        try
        {
            exists = true;
            return ((ulong)process.ProcessorAffinity).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            exists = false;
            return null;
        }
    }

    private static void WriteProcessAffinity(MutationPlan plan, string value)
    {
        using var process = FindProcess(plan.Target)
                            ?? throw new InvalidOperationException($"Process '{plan.Target}' is not running.");
        process.ProcessorAffinity = (nint)ulong.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string? ReadProcessPriority(MutationPlan plan, out bool exists)
    {
        using var process = FindProcess(plan.Target);
        if (process is null)
        {
            exists = false;
            return null;
        }

        try
        {
            exists = true;
            return process.PriorityClass.ToString();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            exists = false;
            return null;
        }
    }

    private static void WriteProcessPriority(MutationPlan plan, string value)
    {
        using var process = FindProcess(plan.Target)
                            ?? throw new InvalidOperationException($"Process '{plan.Target}' is not running.");
        process.PriorityClass = Enum.Parse<ProcessPriorityClass>(value, ignoreCase: true);
    }

    private static string? ReadProcessPowerThrottling(MutationPlan plan, out bool exists)
    {
        using var process = FindProcess(plan.Target);
        if (process is null)
        {
            exists = false;
            return null;
        }

        var handle = ExpertNativeMethods.OpenProcess(
            ExpertNativeMethods.ProcessQueryInformation, false, (uint)process.Id);
        if (handle == 0)
        {
            exists = false;
            return null;
        }

        try
        {
            var state = new ProcessPowerThrottlingState();
            if (!ExpertNativeMethods.GetProcessInformation(
                    handle,
                    ExpertNativeMethods.ProcessInformationClassPowerThrottling,
                    ref state,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessPowerThrottlingState>()))
            {
                exists = false;
                return null;
            }

            exists = true;
            var throttled = (state.ControlMask & ExpertNativeMethods.ProcessPowerThrottlingExecutionSpeed) != 0
                            && (state.StateMask & ExpertNativeMethods.ProcessPowerThrottlingExecutionSpeed) != 0;
            return throttled ? "1" : "0";
        }
        finally
        {
            ExpertNativeMethods.CloseHandle(handle);
        }
    }

    private static void WriteProcessPowerThrottling(MutationPlan plan, string value)
    {
        using var process = FindProcess(plan.Target)
                            ?? throw new InvalidOperationException($"Process '{plan.Target}' is not running.");
        var handle = ExpertNativeMethods.OpenProcess(
            ExpertNativeMethods.ProcessSetInformation | ExpertNativeMethods.ProcessQueryInformation,
            false,
            (uint)process.Id);
        if (handle == 0)
        {
            throw new InvalidOperationException(
                $"Process '{plan.Target}' could not be opened to change power throttling.");
        }

        try
        {
            var throttle = int.Parse(value, CultureInfo.InvariantCulture) != 0;
            var state = new ProcessPowerThrottlingState
            {
                Version = ExpertNativeMethods.ProcessPowerThrottlingCurrentVersion,
                ControlMask = ExpertNativeMethods.ProcessPowerThrottlingExecutionSpeed,
                StateMask = throttle ? ExpertNativeMethods.ProcessPowerThrottlingExecutionSpeed : 0
            };

            if (!ExpertNativeMethods.SetProcessInformation(
                    handle,
                    ExpertNativeMethods.ProcessInformationClassPowerThrottling,
                    ref state,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessPowerThrottlingState>()))
            {
                throw new InvalidOperationException("Power throttling state could not be written.");
            }
        }
        finally
        {
            ExpertNativeMethods.CloseHandle(handle);
        }
    }
}

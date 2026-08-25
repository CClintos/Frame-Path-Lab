using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Power;

public sealed class WindowsPowerSchemeController : IPowerSchemeController
{
    private const uint AccessScheme = 16;
    private const uint AccessActiveScheme = 19;
    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;

    public IReadOnlyList<PowerSchemeDescriptor> EnumerateSchemes()
    {
        var schemes = new List<PowerSchemeDescriptor>();
        var completed = false;
        for (uint index = 0; index < 256; index++)
        {
            var buffer = new byte[Marshal.SizeOf<Guid>()];
            var size = (uint)buffer.Length;
            var result = NativeMethods.PowerEnumerate(
                0,
                0,
                0,
                AccessScheme,
                index,
                buffer,
                ref size);
            if (result == ErrorNoMoreItems)
            {
                completed = true;
                break;
            }

            ThrowForResult(result, "enumerate Windows power plans");
            if (size != Marshal.SizeOf<Guid>())
            {
                throw new InvalidDataException($"Windows returned an unexpected {size}-byte power-plan identifier.");
            }

            var schemeId = new Guid(buffer);
            schemes.Add(new PowerSchemeDescriptor(schemeId, ReadFriendlyName(schemeId)));
        }

        if (!completed)
        {
            throw new InvalidDataException("Windows returned more than the bounded maximum of 256 power plans.");
        }

        return schemes;
    }

    public Guid GetActiveScheme()
    {
        var result = NativeMethods.PowerGetActiveScheme(0, out var pointer);
        ThrowForResult(result, "read the active Windows power plan");
        if (pointer == 0)
        {
            throw new InvalidOperationException("Windows returned a null active power-plan pointer.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    public void EnsureCanSetActiveScheme(Guid schemeId)
    {
        var activeResult = NativeMethods.PowerSettingAccessCheck(AccessActiveScheme, 0);
        ThrowForResult(activeResult, "change the active power plan under current Group Policy");

        var mutableId = schemeId;
        var schemeResult = NativeMethods.PowerSettingAccessCheck(AccessScheme, ref mutableId);
        ThrowForResult(schemeResult, $"access power plan {schemeId:D} under current Group Policy");
    }

    public void SetActiveScheme(Guid schemeId)
    {
        if (!EnumerateSchemes().Any(scheme => scheme.Id == schemeId))
        {
            throw new InvalidOperationException("The requested power plan is no longer enumerated by Windows.");
        }

        var mutableId = schemeId;
        var result = NativeMethods.PowerSetActiveScheme(0, ref mutableId);
        ThrowForResult(result, $"activate power plan {schemeId:D}");
    }

    private static string ReadFriendlyName(Guid schemeId)
    {
        uint size = 0;
        var mutableId = schemeId;
        var first = NativeMethods.PowerReadFriendlyName(0, ref mutableId, 0, 0, null, ref size);
        if (first != ErrorSuccess && size == 0)
        {
            return schemeId.ToString("D");
        }

        if (size is 0 or > 32_768)
        {
            return schemeId.ToString("D");
        }

        var buffer = new byte[size];
        var result = NativeMethods.PowerReadFriendlyName(0, ref mutableId, 0, 0, buffer, ref size);
        if (result != ErrorSuccess)
        {
            return schemeId.ToString("D");
        }

        if (size > buffer.Length || (size & 1) != 0)
        {
            return schemeId.ToString("D");
        }

        var name = Encoding.Unicode.GetString(buffer, 0, checked((int)size)).TrimEnd('\0').Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return schemeId.ToString("D");
        }

        return name.Length <= 1024 ? name : name[..1024];
    }

    private static void ThrowForResult(uint result, string operation)
    {
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(checked((int)result), $"Could not {operation}.");
        }
    }
}

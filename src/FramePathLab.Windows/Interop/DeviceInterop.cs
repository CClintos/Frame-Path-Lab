using System.Runtime.InteropServices;
using System.Text;

namespace FramePathLab.Windows.Interop;

/// <summary>
/// Device enumeration and enable/disable through the documented configuration manager.
///
/// One property of this surface is worth stating because it is the safety net: a plain disable is
/// not persistent. The device comes back on the next boot unless the persist flag is passed, which
/// this application never does. So the worst outcome of a disable that turns out to be wrong — the
/// controller carrying the keyboard, say — is a restart, not a rebuild. That is a considerably
/// better failure mode than most of the registry work elsewhere in this catalogue.
/// </summary>
internal static class DeviceInterop
{
    internal const uint DigcfPresent = 0x02;
    internal const uint DigcfAllClasses = 0x04;

    internal const uint SpdrpDeviceDesc = 0x00;
    internal const uint SpdrpClass = 0x07;
    internal const uint SpdrpClassGuid = 0x08;
    internal const uint SpdrpFriendlyName = 0x0C;

    /// <summary>The kernel service the device is bound to — "storahci", "rcraid", "RTKVHD64".</summary>
    internal const uint SpdrpService = 0x04;

    /// <summary>The device's subpath under the class root, where the provider and version live.</summary>
    internal const uint SpdrpDriver = 0x09;

    /// <summary>Device node has a problem recorded against it.</summary>
    internal const uint DnHasProblem = 0x00000400;

    /// <summary>The problem code meaning a person or program disabled this device.</summary>
    internal const uint ProblemDisabled = 22;

    internal const int CrSuccess = 0;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetupDiGetClassDevs(
        nint classGuid, nint enumerator, nint parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInfo(
        nint deviceInfoSet, uint memberIndex, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        nint deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        nint deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Locate_DevNodeW(
        out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Get_DevNode_Status(
        out uint status, out uint problemNumber, uint devInst, uint flags);

    /// <summary>
    /// Flags are deliberately always zero at the call sites, which means the disable lasts until
    /// the next boot rather than persisting.
    /// </summary>
    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Enable_DevNode(uint devInst, uint flags);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevInfoData
{
    public uint Size;
    public Guid ClassGuid;
    public uint DevInst;
    public nint Reserved;

    public static SpDevInfoData Create()
        => new() { Size = (uint)Marshal.SizeOf<SpDevInfoData>() };
}

using System.Runtime.InteropServices;

namespace FramePathLab.Windows.Interop;

internal static class NativeMethods
{
    internal const int EnumCurrentSettings = -1;
    internal const int SmRemoteSession = 0x1000;

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string? device,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsEx(
        string? deviceName,
        int modeNumber,
        ref DevMode devMode,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveScheme(nint userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll", EntryPoint = "PowerSettingAccessCheck")]
    internal static extern uint PowerSettingAccessCheck(uint accessFlags, nint powerGuid);

    [DllImport("powrprof.dll", EntryPoint = "PowerSettingAccessCheck")]
    internal static extern uint PowerSettingAccessCheck(uint accessFlags, ref Guid powerGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerEnumerate(
        nint rootPowerKey,
        nint schemeGuid,
        nint subgroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PowerReadFriendlyName(
        nint rootPowerKey,
        ref Guid schemeGuid,
        nint subgroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    internal static extern nint LocalFree(nint memory);
}

[Flags]
internal enum DisplayDeviceStateFlags : uint
{
    AttachedToDesktop = 0x00000001,
    PrimaryDevice = 0x00000004,
    MirroringDriver = 0x00000008,
    Remote = 0x04000000,
    Disconnect = 0x02000000
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayDevice
{
    public int Size;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;

    public DisplayDeviceStateFlags StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;

    public static DisplayDevice Create()
        => new()
        {
            Size = Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty
        };
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DevMode
{
    private const int DeviceNameCharacters = 32;
    private const int FormNameCharacters = 32;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameCharacters)]
    public string DeviceName;

    public short SpecVersion;
    public short DriverVersion;
    public short Size;
    public short DriverExtra;
    public int Fields;
    public int PositionX;
    public int PositionY;
    public int DisplayOrientation;
    public int DisplayFixedOutput;
    public short Color;
    public short Duplex;
    public short YResolution;
    public short TtOption;
    public short Collate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FormNameCharacters)]
    public string FormName;

    public short LogPixels;
    public int BitsPerPel;
    public int PelsWidth;
    public int PelsHeight;
    public int DisplayFlags;
    public int DisplayFrequency;
    public int IcmMethod;
    public int IcmIntent;
    public int MediaType;
    public int DitherType;
    public int Reserved1;
    public int Reserved2;
    public int PanningWidth;
    public int PanningHeight;

    public static DevMode Create()
        => new()
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = (short)Marshal.SizeOf<DevMode>()
        };
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhysical;
    public ulong AvailablePhysical;
    public ulong TotalPageFile;
    public ulong AvailablePageFile;
    public ulong TotalVirtual;
    public ulong AvailableVirtual;
    public ulong AvailableExtendedVirtual;

    public static MemoryStatusEx Create()
        => new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

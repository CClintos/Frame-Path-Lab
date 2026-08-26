using System.Runtime.InteropServices;

namespace FramePathLab.Windows.Interop;

/// <summary>
/// Documented Win32 surfaces used by the expert scanners. Every entry point here is read-first:
/// nothing in this file writes without an explicit caller-supplied value.
/// </summary>
internal static class ExpertNativeMethods
{
    // ---- Logical processor topology -------------------------------------------------------
    internal const int RelationProcessorCore = 0;
    internal const int RelationCache = 2;
    internal const byte LtpPcSmt = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        byte[]? buffer,
        ref uint returnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessAffinityMask(
        nint process,
        out nuint processAffinityMask,
        out nuint systemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessAffinityMask(nint process, nuint processAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessInformation(
        nint process,
        int processInformationClass,
        ref ProcessPowerThrottlingState processInformation,
        uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessInformation(
        nint process,
        int processInformationClass,
        ref ProcessPowerThrottlingState processInformation,
        uint processInformationSize);

    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint ProcessSetInformation = 0x0200;
    internal const int ProcessInformationClassPowerThrottling = 4;
    internal const uint ProcessPowerThrottlingCurrentVersion = 1;
    internal const uint ProcessPowerThrottlingExecutionSpeed = 0x1;

    // ---- Processor power information ------------------------------------------------------
    internal const int ProcessorInformationLevel = 11;

    [DllImport("powrprof.dll")]
    internal static extern uint CallNtPowerInformation(
        int informationLevel,
        nint inputBuffer,
        uint inputBufferLength,
        byte[] outputBuffer,
        uint outputBufferLength);

    // ---- Power scheme sub-values ----------------------------------------------------------
    [DllImport("powrprof.dll")]
    internal static extern uint PowerReadACValueIndex(
        nint rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint acValueIndex);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerWriteACValueIndex(
        nint rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint acValueIndex);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveScheme(nint userRootPowerKey, ref Guid schemeGuid);

    // Windows 11 "Power mode" is an overlay on top of the plan. These exports are stable across
    // Windows 10 1709+ and Windows 11 but are not published on Microsoft Learn, so every caller
    // treats a non-zero return as "unsupported here" rather than as a hard failure.
    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    // ---- Timer resolution -----------------------------------------------------------------
    [DllImport("ntdll.dll")]
    internal static extern int NtQueryTimerResolution(
        out uint minimumResolution,
        out uint maximumResolution,
        out uint currentResolution);

    // ---- Exact display timing -------------------------------------------------------------
    internal const uint QdcOnlyActivePaths = 0x00000002;
    internal const int DeviceInfoGetSourceName = 1;
    internal const int DeviceInfoGetTargetName = 2;
    internal const int DeviceInfoGetAdvancedColorInfo = 9;

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigAdvancedColorInfo requestPacket);

    // ---- Pointer behaviour ----------------------------------------------------------------
    internal const uint SpiGetMouse = 0x0003;
    internal const uint SpiSetMouse = 0x0004;
    internal const uint SpiGetMouseSpeed = 0x0070;
    internal const uint SpiSetMouseSpeed = 0x0071;
    internal const uint SpifUpdateIniFile = 0x01;
    internal const uint SpifSendChange = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint param,
        [In, Out] int[] vParam,
        uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint param,
        ref int vParam,
        uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    internal static extern bool SystemParametersInfoSetValue(
        uint action,
        uint param,
        nint vParam,
        uint winIni);

    // ---- Raw input ------------------------------------------------------------------------
    internal const int WmInput = 0x00FF;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RidInput = 0x10000003;
    internal const ushort HidUsagePageGeneric = 0x01;
    internal const ushort HidUsageGenericMouse = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] rawInputDevices,
        uint numDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint sizeHeader);

    // ---- WDDM capability query ------------------------------------------------------------
    internal const int KmtqaiTypeWddm27Caps = 70;

    [DllImport("gdi32.dll")]
    internal static extern int D3DKMTEnumAdapters2(ref D3dkmtEnumAdapters2 enumAdapters);

    [DllImport("gdi32.dll")]
    internal static extern int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo queryAdapterInfo);

    [DllImport("gdi32.dll")]
    internal static extern int D3DKMTCloseAdapter(ref D3dkmtCloseAdapter closeAdapter);
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessPowerThrottlingState
{
    public uint Version;
    public uint ControlMask;
    public uint StateMask;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessorPowerInformation
{
    public uint Number;
    public uint MaxMhz;
    public uint CurrentMhz;
    public uint MhzLimit;
    public uint MaxIdleState;
    public uint CurrentIdleState;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public uint HSyncFreqNumerator;
    public uint HSyncFreqDenominator;
    public uint VSyncFreqNumerator;
    public uint VSyncFreqDenominator;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetMode
{
    public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    public uint Width;
    public uint Height;
    public uint PixelFormat;
    public PointL Position;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfoUnion
{
    [FieldOffset(0)]
    public DisplayConfigTargetMode TargetMode;

    [FieldOffset(0)]
    public DisplayConfigSourceMode SourceMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    public uint InfoType;
    public uint Id;
    public Luid AdapterId;
    public DisplayConfigModeInfoUnion Mode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public uint Type;
    public uint Size;
    public Luid AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string ViewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public uint OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigAdvancedColorInfo
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Value;
    public uint ColorEncoding;
    public uint BitsPerColorChannel;

    public bool AdvancedColorSupported => (Value & 0x1) != 0;

    public bool AdvancedColorEnabled => (Value & 0x2) != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDevice
{
    public ushort UsagePage;
    public ushort Usage;
    public uint Flags;
    public nint Target;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHeader
{
    public uint Type;
    public uint Size;
    public nint Device;
    public nint WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtAdapterInfo
{
    public uint Adapter;
    public Luid AdapterLuid;
    public uint NumOfSources;
    public int PrecisePresentRegionsPreferred;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtEnumAdapters2
{
    public uint NumAdapters;
    public nint Adapters;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtQueryAdapterInfo
{
    public uint Adapter;
    public int Type;
    public nint PrivateDriverData;
    public uint PrivateDriverDataSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3dkmtCloseAdapter
{
    public uint Adapter;
}

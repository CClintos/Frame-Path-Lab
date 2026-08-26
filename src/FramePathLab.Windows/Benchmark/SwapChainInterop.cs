using System.Runtime.InteropServices;

namespace FramePathLab.Windows.Benchmark;

/// <summary>
/// The minimum Direct3D surface needed to drive a real swap chain and time its presents.
///
/// The point of owning the swap chain rather than observing someone else's is that every number
/// comes from this process: present timing is taken either side of the call, and the runtime's own
/// frame statistics report what actually reached the display. Nothing depends on an external
/// collector, an event-tracing session, or events that can be silently lost under load.
/// </summary>
internal static class SwapChainInterop
{
    internal const uint D3DDriverTypeHardware = 1;
    internal const uint D3DSdkVersion = 7;

    /// <summary>Feature level 11.0; nothing here needs more than that.</summary>
    internal const uint FeatureLevel110 = 0xb000;

    internal const uint FormatB8G8R8A8Unorm = 87;

    /// <summary>The flip model modern games present through, and the one worth measuring.</summary>
    internal const uint SwapEffectFlipDiscard = 4;

    internal const uint UsageRenderTargetOutput = 0x20;
    internal const uint SwapChainFlagAllowTearing = 2048;
    internal const uint PresentAllowTearing = 0x00000200;

    [DllImport("d3d11.dll")]
    internal static extern int D3D11CreateDeviceAndSwapChain(
        nint adapter,
        uint driverType,
        nint software,
        uint flags,
        uint[]? featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        ref SwapChainDescription swapChainDesc,
        out IDxgiSwapChain swapChain,
        out nint device,
        out uint featureLevel,
        out nint immediateContext);
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiRational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiModeDescription
{
    public uint Width;
    public uint Height;
    public DxgiRational RefreshRate;
    public uint Format;
    public uint ScanlineOrdering;
    public uint Scaling;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiSampleDescription
{
    public uint Count;
    public uint Quality;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SwapChainDescription
{
    public DxgiModeDescription BufferDescription;
    public DxgiSampleDescription SampleDescription;
    public uint BufferUsage;
    public uint BufferCount;
    public nint OutputWindow;
    public int Windowed;
    public uint SwapEffect;
    public uint Flags;
}

/// <summary>
/// What the runtime says actually happened, as opposed to what was asked for. The present and
/// refresh counters are what expose a present that never reached the display.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DxgiFrameStatistics
{
    public uint PresentCount;
    public uint PresentRefreshCount;
    public uint SyncRefreshCount;
    public long SyncQpcTime;
    public long SyncGpuTime;
}

/// <summary>
/// Declared in full vtable order because a COM interface cannot be partially described — the
/// inherited members have to be present so the later slots land in the right place.
/// </summary>
[ComImport]
[Guid("310d36a0-d2e7-4c0a-aa04-6a9d23b8886a")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDxgiSwapChain
{
    // IDXGIObject
    [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, nint data);
    [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);
    [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, nint data);
    [PreserveSig] int GetParent(ref Guid riid, out nint parent);

    // IDXGIDeviceSubObject
    [PreserveSig] int GetDevice(ref Guid riid, out nint device);

    // IDXGISwapChain
    [PreserveSig] int Present(uint syncInterval, uint flags);
    [PreserveSig] int GetBuffer(uint buffer, ref Guid riid, out nint surface);
    [PreserveSig] int SetFullscreenState(int fullscreen, nint target);
    [PreserveSig] int GetFullscreenState(out int fullscreen, out nint target);
    [PreserveSig] int GetDesc(out SwapChainDescription description);
    [PreserveSig] int ResizeBuffers(uint bufferCount, uint width, uint height, uint newFormat, uint flags);
    [PreserveSig] int ResizeTarget(ref DxgiModeDescription newTargetParameters);
    [PreserveSig] int GetContainingOutput(out nint output);
    [PreserveSig] int GetFrameStatistics(out DxgiFrameStatistics statistics);
    [PreserveSig] int GetLastPresentCount(out uint lastPresentCount);
}

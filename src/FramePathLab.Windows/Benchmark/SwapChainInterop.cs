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

/// <summary>
/// The device and context members needed to put real work on the graphics processor. Clearing a
/// render target repeatedly is enough: it is genuine fill work and it goes through the driver's
/// command submission path, which is what a game's draw calls actually cost on the processor side.
/// </summary>
[ComImport]
[Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11Device
{
    [PreserveSig] int CreateBuffer(nint desc, nint initialData, out nint buffer);
    [PreserveSig] int CreateTexture1D(nint desc, nint initialData, out nint texture);
    [PreserveSig] int CreateTexture2D(nint desc, nint initialData, out nint texture);
    [PreserveSig] int CreateTexture3D(nint desc, nint initialData, out nint texture);
    [PreserveSig] int CreateShaderResourceView(nint resource, nint desc, out nint view);
    [PreserveSig] int CreateUnorderedAccessView(nint resource, nint desc, out nint view);
    [PreserveSig] int CreateRenderTargetView(nint resource, nint desc, out nint view);
}

[ComImport]
[Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ID3D11DeviceContext
{
    // ID3D11DeviceChild
    [PreserveSig] void GetDevice(out nint device);
    [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, nint data);
    [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, nint data);
    [PreserveSig] int SetPrivateDataInterface(ref Guid name, nint unknown);

    // ID3D11DeviceContext, up to the clear entry point
    [PreserveSig] void VSSetConstantBuffers(uint slot, uint count, nint buffers);
    [PreserveSig] void PSSetShaderResources(uint slot, uint count, nint views);
    [PreserveSig] void PSSetShader(nint shader, nint instances, uint count);
    [PreserveSig] void PSSetSamplers(uint slot, uint count, nint samplers);
    [PreserveSig] void VSSetShader(nint shader, nint instances, uint count);
    [PreserveSig] void DrawIndexed(uint indexCount, uint startIndex, int baseVertex);
    [PreserveSig] void Draw(uint vertexCount, uint startVertex);
    [PreserveSig] int Map(nint resource, uint subresource, uint mapType, uint flags, nint mapped);
    [PreserveSig] void Unmap(nint resource, uint subresource);
    [PreserveSig] void PSSetConstantBuffers(uint slot, uint count, nint buffers);
    [PreserveSig] void IASetInputLayout(nint layout);
    [PreserveSig] void IASetVertexBuffers(uint slot, uint count, nint buffers, nint strides, nint offsets);
    [PreserveSig] void IASetIndexBuffer(nint buffer, uint format, uint offset);
    [PreserveSig] void DrawIndexedInstanced(uint indexCountPerInstance, uint instanceCount, uint startIndex, int baseVertex, uint startInstance);
    [PreserveSig] void DrawInstanced(uint vertexCountPerInstance, uint instanceCount, uint startVertex, uint startInstance);
    [PreserveSig] void GSSetConstantBuffers(uint slot, uint count, nint buffers);
    [PreserveSig] void GSSetShader(nint shader, nint instances, uint count);
    [PreserveSig] void IASetPrimitiveTopology(uint topology);
    [PreserveSig] void VSSetShaderResources(uint slot, uint count, nint views);
    [PreserveSig] void VSSetSamplers(uint slot, uint count, nint samplers);
    [PreserveSig] void Begin(nint async);
    [PreserveSig] void End(nint async);
    [PreserveSig] int GetData(nint async, nint data, uint dataSize, uint flags);
    [PreserveSig] void SetPredication(nint predicate, int predicateValue);
    [PreserveSig] void GSSetShaderResources(uint slot, uint count, nint views);
    [PreserveSig] void GSSetSamplers(uint slot, uint count, nint samplers);
    [PreserveSig] void OMSetRenderTargets(uint count, nint views, nint depthStencil);
    [PreserveSig] void OMSetRenderTargetsAndUnorderedAccessViews(uint count, nint views, nint depthStencil, uint uavStart, uint uavCount, nint uavs, nint counts);
    [PreserveSig] void OMSetBlendState(nint blendState, nint blendFactor, uint sampleMask);
    [PreserveSig] void OMSetDepthStencilState(nint depthStencilState, uint stencilRef);
    [PreserveSig] void SOSetTargets(uint count, nint targets, nint offsets);
    [PreserveSig] void DrawAuto();
    [PreserveSig] void DrawIndexedInstancedIndirect(nint buffer, uint alignedOffset);
    [PreserveSig] void DrawInstancedIndirect(nint buffer, uint alignedOffset);
    [PreserveSig] void Dispatch(uint x, uint y, uint z);
    [PreserveSig] void DispatchIndirect(nint buffer, uint alignedOffset);
    [PreserveSig] void RSSetState(nint state);
    [PreserveSig] void RSSetViewports(uint count, nint viewports);
    [PreserveSig] void RSSetScissorRects(uint count, nint rects);
    [PreserveSig] void CopySubresourceRegion(nint dst, uint dstSub, uint x, uint y, uint z, nint src, uint srcSub, nint box);
    [PreserveSig] void CopyResource(nint dst, nint src);
    [PreserveSig] void UpdateSubresource(nint dst, uint dstSub, nint box, nint data, uint rowPitch, uint depthPitch);
    [PreserveSig] void CopyStructureCount(nint dstBuffer, uint dstOffset, nint srcView);
    [PreserveSig] void ClearRenderTargetView(nint renderTargetView, [In] float[] colorRgba);
}

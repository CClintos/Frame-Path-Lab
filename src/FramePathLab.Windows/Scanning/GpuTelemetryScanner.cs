using System.Runtime.InteropServices;
using System.Text;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the GPU state that decides whether a system holds its clocks under a competitive load:
/// the active performance state, why the driver is currently limiting clocks, and whether the card
/// negotiated its full PCIe link. A GPU silently running at x8 or dropping to a thermal slowdown
/// mid-round produces exactly the 1%-low collapse that gets misattributed to Windows settings.
///
/// NVML ships with the NVIDIA driver and is loaded by name at runtime. No binary is bundled and a
/// missing library degrades to "telemetry unavailable" rather than failing the scan.
/// </summary>
public static class GpuTelemetryScanner
{
    public static IReadOnlyList<GpuTelemetry> Scan(IReadOnlyList<string> adapterDescriptions)
    {
        var hardwareScheduling = ReadHardwareScheduling();
        var nvidia = NvmlSession.TryRead();

        var results = new List<GpuTelemetry>();
        var distinct = adapterDescriptions
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinct.Length == 0 && nvidia.Count == 0)
        {
            return results;
        }

        foreach (var description in distinct)
        {
            var vendor = ResolveVendor(description);
            var match = nvidia.FirstOrDefault(device =>
                description.Contains(device.Name, StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains(TrimVendorPrefix(description), StringComparison.OrdinalIgnoreCase));

            results.Add(BuildCard(description, vendor, match, hardwareScheduling));
        }

        // A discrete NVIDIA card that is not driving any enumerated display still matters, because
        // it may be the render device on a hybrid system.
        foreach (var device in nvidia)
        {
            if (results.Any(card => card.Name.Contains(device.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(BuildCard(device.Name, "NVIDIA", device, hardwareScheduling));
        }

        return results;
    }

    private static GpuTelemetry BuildCard(
        string name,
        string vendor,
        NvmlDeviceReading? reading,
        (bool? Supported, bool? Enabled) hardwareScheduling)
    {
        if (reading is null)
        {
            return new GpuTelemetry(
                name,
                vendor,
                false,
                "none",
                null, null, null, null,
                null,
                [],
                null,
                hardwareScheduling.Supported,
                hardwareScheduling.Enabled,
                vendor == "NVIDIA"
                    ? "NVIDIA adapter detected but NVML did not return a matching device."
                    : $"{vendor} telemetry is not read by this build; frame-capture evidence remains available.");
        }

        return new GpuTelemetry(
            name,
            vendor,
            true,
            "NVML",
            reading.LinkWidth,
            reading.MaxLinkWidth,
            reading.LinkGeneration,
            reading.MaxLinkGeneration,
            reading.PerformanceState,
            reading.ThrottleReasons,
            reading.ResizableBarActive,
            hardwareScheduling.Supported,
            hardwareScheduling.Enabled,
            reading.Observation);
    }

    private static string ResolveVendor(string description)
    {
        if (description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || description.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA";
        }

        if (description.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "AMD";
        }

        return description.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Unknown";
    }

    private static string TrimVendorPrefix(string description)
        => description
            .Replace("NVIDIA", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("AMD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

    /// <summary>
    /// No supported public API currently used by this build proves effective HAGS state. The
    /// D3DKMT_WDDM_2_7_CAPS structure is documented as reserved for system use, so it is deliberately
    /// not queried and the supported Settings UI remains the authority.
    /// </summary>
    public static (bool? Supported, bool? Enabled) ReadHardwareScheduling()
        => (null, null);
}

internal sealed record NvmlDeviceReading(
    string Name,
    int? LinkWidth,
    int? MaxLinkWidth,
    int? LinkGeneration,
    int? MaxLinkGeneration,
    string? PerformanceState,
    IReadOnlyList<string> ThrottleReasons,
    bool? ResizableBarActive,
    string Observation);

/// <summary>Runtime binding to the NVIDIA Management Library that ships with the display driver.</summary>
internal static class NvmlSession
{
    private const int NvmlSuccess = 0;

    private static readonly (ulong Bit, string Reason)[] ThrottleReasonMap =
    [
        (0x0000000000000001UL, "GPU idle"),
        (0x0000000000000002UL, "Applications clocks setting"),
        (0x0000000000000004UL, "Software power cap"),
        (0x0000000000000008UL, "Hardware slowdown"),
        (0x0000000000000010UL, "Sync boost"),
        (0x0000000000000020UL, "Software thermal slowdown"),
        (0x0000000000000040UL, "Hardware thermal slowdown"),
        (0x0000000000000080UL, "Hardware power brake slowdown"),
        (0x0000000000000100UL, "Display clock setting")
    ];

    public static IReadOnlyList<NvmlDeviceReading> TryRead()
    {
        // Never use the default DLL search order here. The app may be started from a downloads or
        // game directory and some scans are useful to elevated users; loading a same-name DLL from
        // either location would turn telemetry into code execution. NVIDIA installs NVML into the
        // protected Windows system directory on supported display-driver systems.
        var trustedPath = Path.Combine(Environment.SystemDirectory, "nvml.dll");
        if (!File.Exists(trustedPath) || !NativeLibrary.TryLoad(trustedPath, out var library))
        {
            return [];
        }

        try
        {
            var initialize = Bind<NvmlInit>(library, "nvmlInit_v2");
            var shutdown = Bind<NvmlShutdown>(library, "nvmlShutdown");
            if (initialize is null || shutdown is null || initialize() != NvmlSuccess)
            {
                return [];
            }

            try
            {
                return ReadDevices(library);
            }
            finally
            {
                shutdown();
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return [];
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static IReadOnlyList<NvmlDeviceReading> ReadDevices(nint library)
    {
        var getCount = Bind<NvmlDeviceGetCount>(library, "nvmlDeviceGetCount_v2");
        var getHandle = Bind<NvmlDeviceGetHandleByIndex>(library, "nvmlDeviceGetHandleByIndex_v2");
        var getName = Bind<NvmlDeviceGetName>(library, "nvmlDeviceGetName");
        if (getCount is null || getHandle is null || getName is null || getCount(out var count) != NvmlSuccess)
        {
            return [];
        }

        var currentWidth = Bind<NvmlDeviceGetUInt>(library, "nvmlDeviceGetCurrPcieLinkWidth");
        var maxWidth = Bind<NvmlDeviceGetUInt>(library, "nvmlDeviceGetMaxPcieLinkWidth");
        var currentGeneration = Bind<NvmlDeviceGetUInt>(library, "nvmlDeviceGetCurrPcieLinkGeneration");
        var maxGeneration = Bind<NvmlDeviceGetUInt>(library, "nvmlDeviceGetMaxPcieLinkGeneration");
        var performanceState = Bind<NvmlDeviceGetUInt>(library, "nvmlDeviceGetPerformanceState");
        var throttleReasons = Bind<NvmlDeviceGetULong>(library, "nvmlDeviceGetCurrentClocksThrottleReasons")
                              ?? Bind<NvmlDeviceGetULong>(library, "nvmlDeviceGetCurrentClocksEventReasons");
        var bar1Memory = Bind<NvmlDeviceGetBar1Memory>(library, "nvmlDeviceGetBAR1MemoryInfo");

        var devices = new List<NvmlDeviceReading>();
        for (uint index = 0; index < count; index++)
        {
            if (getHandle(index, out var device) != NvmlSuccess)
            {
                continue;
            }

            var nameBuffer = new StringBuilder(96);
            var name = getName(device, nameBuffer, (uint)nameBuffer.Capacity) == NvmlSuccess
                ? nameBuffer.ToString()
                : $"NVIDIA device {index}";

            var reasons = new List<string>();
            if (throttleReasons is not null && throttleReasons(device, out var mask) == NvmlSuccess)
            {
                reasons.AddRange(
                    from entry in ThrottleReasonMap
                    where (mask & entry.Bit) != 0
                    select entry.Reason);
            }

            var linkWidth = ReadOptional(currentWidth, device);
            var linkMaxWidth = ReadOptional(maxWidth, device);
            var generation = ReadOptional(currentGeneration, device);
            var maximumGeneration = ReadOptional(maxGeneration, device);
            var pstate = ReadOptional(performanceState, device);

            var observation = new StringBuilder();
            observation.Append(
                linkWidth.HasValue && linkMaxWidth.HasValue
                    ? $"PCIe x{linkWidth} of x{linkMaxWidth}"
                    : "PCIe link not reported");
            if (generation.HasValue && maximumGeneration.HasValue)
            {
                observation.Append($", Gen {generation} of Gen {maximumGeneration}");
            }

            if (pstate.HasValue)
            {
                observation.Append($", performance state P{pstate}");
            }

            // BAR1 telemetry describes aperture allocation only. It does not prove that the
            // driver's per-game Resizable BAR profile is enabled for CS2.
            if (bar1Memory is not null && bar1Memory(device, out var bar1) == NvmlSuccess && bar1.Total > 0)
            {
                observation.Append($", BAR1 aperture {bar1.Total / 1024 / 1024} MiB");
            }

            observation.Append(reasons.Count > 0
                ? $"; limiting: {string.Join(", ", reasons)}."
                : "; no clock limiter reported at scan time.");

            devices.Add(new NvmlDeviceReading(
                name,
                linkWidth,
                linkMaxWidth,
                generation,
                maximumGeneration,
                pstate.HasValue ? $"P{pstate}" : null,
                reasons,
                null,
                observation.ToString()));
        }

        return devices;
    }

    private static int? ReadOptional(NvmlDeviceGetUInt? function, nint device)
        => function is not null && function(device, out var value) == NvmlSuccess ? (int)value : null;

    private static TDelegate? Bind<TDelegate>(nint library, string export)
        where TDelegate : Delegate
        => NativeLibrary.TryGetExport(library, export, out var address)
            ? Marshal.GetDelegateForFunctionPointer<TDelegate>(address)
            : null;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetCount(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndex(uint index, out nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvmlDeviceGetName(nint device, StringBuilder name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUInt(nint device, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetULong(nint device, out ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetBar1Memory(nint device, out NvmlBar1Memory memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlBar1Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}

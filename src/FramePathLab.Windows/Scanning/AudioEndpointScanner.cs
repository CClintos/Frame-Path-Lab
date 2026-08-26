using System.Diagnostics;
using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the audio render path.
///
/// This matters competitively for a reason that is easy to miss: a modern shooter does its own
/// head-related transfer function to place a sound in space. A virtual-surround renderer layered
/// on top applies a second one, and two spatial models in series do not compound into better
/// localisation — they blur it. The engine's cue and the renderer's cue disagree about phase, and
/// the footstep gets harder to place rather than easier.
///
/// The device format matters for a simpler reason: engines author at 48 kHz, and any other
/// shared-mode rate makes the audio engine resample every buffer.
/// </summary>
public static class AudioEndpointScanner
{
    private const string RenderRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    // PKEY_AudioEngine_DeviceFormat: the shared-mode mix format, stored as a WAVEFORMATEX blob.
    private const string DeviceFormatKey = "{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0";

    // PKEY_Device_FriendlyName.
    private const string FriendlyNameKey = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";

    // PKEY_AudioEndpoint_Disable_SysFx: 1 means endpoint effects are switched off.
    private const string DisableSysFxKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    // Exclusive-mode permission for the endpoint.
    private const string ExclusiveModeKey = "{b3f8fa53-0004-438e-9003-51a46e139bfc},3";

    private const int DeviceStateActive = 1;

    private static readonly (string ProcessName, string DisplayName)[] SpatialProviders =
    [
        ("DolbyAccess", "Dolby Access"),
        ("Dolby.Access", "Dolby Access"),
        ("DTSUnbound", "DTS Sound Unbound"),
        ("DTSAPO4Service", "DTS audio service"),
        ("RtkAudUService64", "Realtek audio service"),
        ("NahimicService", "Nahimic audio service"),
        ("NahimicSvc64", "Nahimic audio service")
    ];

    public static AudioState Scan()
    {
        var endpoints = new List<AudioEndpointState>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(RenderRoot, writable: false);
            if (root is null)
            {
                return new AudioState(false, [], [], "Audio endpoint registry could not be opened.");
            }

            foreach (var endpointId in root.GetSubKeyNames())
            {
                using var endpoint = root.OpenSubKey(endpointId, writable: false);
                if (endpoint?.GetValue("DeviceState") is not int state || state != DeviceStateActive)
                {
                    continue;
                }

                using var properties = endpoint.OpenSubKey("Properties", writable: false);
                if (properties is null)
                {
                    continue;
                }

                var parsed = ParseEndpoint(properties);
                if (parsed is not null)
                {
                    endpoints.Add(parsed);
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return new AudioState(false, [], [], $"Audio endpoints could not be read: {exception.Message}");
        }

        if (endpoints.Count == 0)
        {
            return new AudioState(false, [], [], "No active audio render endpoint was reported.");
        }

        // The registry does not record which endpoint is the default in a form that is stable
        // across Windows versions, so the first active endpoint carrying a usable format is
        // treated as representative and labelled as such rather than asserted as the default.
        var ordered = endpoints
            .OrderByDescending(candidate => candidate.SampleRateHz > 0)
            .ToList();
        ordered[0] = ordered[0] with { IsDefault = true };

        var providers = SpatialProviders
            .Where(provider => IsRunning(provider.ProcessName))
            .Select(provider => provider.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AudioState(
            true,
            ordered,
            providers,
            providers.Length == 0
                ? $"{ordered.Count} active render endpoint(s); no third-party spatial audio service was observed."
                : $"{ordered.Count} active render endpoint(s); spatial or effects services running: "
                  + string.Join(", ", providers) + ".");
    }

    private static AudioEndpointState? ParseEndpoint(RegistryKey properties)
    {
        var name = properties.GetValue(FriendlyNameKey) as string ?? "Audio endpoint";
        var (rate, bits, channels) = ParseWaveFormat(properties.GetValue(DeviceFormatKey) as byte[]);
        if (rate == 0)
        {
            return null;
        }

        return new AudioEndpointState(
            name,
            false,
            rate,
            bits,
            channels,
            properties.GetValue(DisableSysFxKey) is int sysFx ? sysFx != 0 : null,
            properties.GetValue(ExclusiveModeKey) is int exclusive ? exclusive != 0 : null,
            $"{name}: {rate / 1000d:0.###} kHz, {bits}-bit, {channels} channel(s).");
    }

    /// <summary>
    /// WAVEFORMATEX layout: wFormatTag(2) nChannels(2) nSamplesPerSec(4) nAvgBytesPerSec(4)
    /// nBlockAlign(2) wBitsPerSample(2).
    ///
    /// The stored value is a property variant, so the structure is preceded by an eight-byte type
    /// and length header. Some writers store the structure bare instead, so both placements are
    /// tried and the first that yields a plausible render format wins. A blob that yields nothing
    /// plausible reports nothing rather than a nonsense rate.
    /// </summary>
    private static (int Rate, int Bits, int Channels) ParseWaveFormat(byte[]? blob)
    {
        if (blob is null)
        {
            return (0, 0, 0);
        }

        foreach (var start in (int[])[8, 0])
        {
            if (blob.Length < start + 16)
            {
                continue;
            }

            var channels = BitConverter.ToUInt16(blob, start + 2);
            var rate = BitConverter.ToUInt32(blob, start + 4);
            var bits = BitConverter.ToUInt16(blob, start + 14);
            if (rate is >= 8000 and <= 768000 && channels is > 0 and <= 32)
            {
                return ((int)rate, bits, channels);
            }
        }

        return (0, 0, 0);
    }

    private static bool IsRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

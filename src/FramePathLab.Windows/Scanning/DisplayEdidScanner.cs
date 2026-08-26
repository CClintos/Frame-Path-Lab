using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the panel's own description of itself from EDID.
///
/// Windows only enumerates the modes the current link can carry. That means a display running
/// below its native resolution, or on a link that cannot carry its full timing, reports its
/// reduced ceiling as though it were the panel's ceiling — and a refresh check that only compares
/// modes at the current resolution will call that "already at maximum". EDID is the independent
/// second opinion: the panel states its preferred timing and its vertical rate range regardless of
/// how it happens to be connected right now.
/// </summary>
public static class DisplayEdidScanner
{
    private const string DisplayEnumRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";
    private const int EdidBlockBytes = 128;

    public static PanelIdentity Scan()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(DisplayEnumRoot, writable: false);
            if (root is null)
            {
                return Unavailable("Display device enumeration could not be opened.");
            }

            foreach (var monitorId in root.GetSubKeyNames())
            {
                using var monitor = root.OpenSubKey(monitorId, writable: false);
                if (monitor is null)
                {
                    continue;
                }

                foreach (var instanceId in monitor.GetSubKeyNames())
                {
                    using var parameters = monitor.OpenSubKey($@"{instanceId}\Device Parameters", writable: false);
                    if (parameters?.GetValue("EDID") is not byte[] edid || edid.Length < EdidBlockBytes)
                    {
                        continue;
                    }

                    var parsed = Parse(edid);
                    if (parsed.Available)
                    {
                        return parsed;
                    }
                }
            }

            return Unavailable("No attached display exposed a readable EDID block.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Unavailable($"EDID could not be read: {exception.Message}");
        }
    }

    private static PanelIdentity Parse(byte[] edid)
    {
        if (!HasValidHeader(edid))
        {
            return Unavailable("EDID header did not match the expected signature.");
        }

        var manufacturer = DecodeManufacturer(edid);
        var name = string.Empty;
        var minimumHz = 0;
        var maximumHz = 0;

        // Four 18-byte descriptors start at offset 54. The first is normally the preferred timing;
        // the rest carry monitor range limits and the product name.
        for (var index = 0; index < 4; index++)
        {
            var offset = 54 + (index * 18);
            if (offset + 18 > edid.Length)
            {
                break;
            }

            var isDisplayDescriptor = edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0;
            if (!isDisplayDescriptor)
            {
                continue;
            }

            switch (edid[offset + 3])
            {
                case 0xFC:
                    name = DecodeText(edid, offset + 5);
                    break;
                case 0xFD:
                    minimumHz = edid[offset + 5];
                    maximumHz = edid[offset + 6];
                    break;
            }
        }

        var (width, height) = DecodePreferredTiming(edid);
        return new PanelIdentity(
            true,
            manufacturer,
            string.IsNullOrWhiteSpace(name) ? "Unnamed panel" : name,
            width,
            height,
            minimumHz,
            maximumHz,
            $"{manufacturer} {(string.IsNullOrWhiteSpace(name) ? "panel" : name)}: native {width}x{height}"
            + (maximumHz > 0 ? $", vertical range {minimumHz}-{maximumHz} Hz" : ", vertical range not stated"));
    }

    private static bool HasValidHeader(byte[] edid)
        => edid[0] == 0x00 && edid[1] == 0xFF && edid[2] == 0xFF && edid[3] == 0xFF
           && edid[4] == 0xFF && edid[5] == 0xFF && edid[6] == 0xFF && edid[7] == 0x00;

    /// <summary>
    /// Bytes 8-9 hold three five-bit letters, each stored as an offset from 'A' minus one.
    /// </summary>
    private static string DecodeManufacturer(byte[] edid)
    {
        var packed = (edid[8] << 8) | edid[9];
        Span<char> letters =
        [
            (char)('A' + ((packed >> 10) & 0x1F) - 1),
            (char)('A' + ((packed >> 5) & 0x1F) - 1),
            (char)('A' + (packed & 0x1F) - 1)
        ];

        foreach (var letter in letters)
        {
            if (letter is < 'A' or > 'Z')
            {
                return "???";
            }
        }

        return new string(letters);
    }

    /// <summary>
    /// The preferred timing descriptor packs the high bits of each dimension into a shared byte:
    /// horizontal active low byte at +2, vertical active low byte at +5, and the upper nibbles of
    /// each in +4 and +7 respectively.
    /// </summary>
    private static (int Width, int Height) DecodePreferredTiming(byte[] edid)
    {
        const int offset = 54;
        if (offset + 8 > edid.Length)
        {
            return (0, 0);
        }

        var width = edid[offset + 2] | ((edid[offset + 4] & 0xF0) << 4);
        var height = edid[offset + 5] | ((edid[offset + 7] & 0xF0) << 4);
        return width is > 0 and <= 16384 && height is > 0 and <= 16384 ? (width, height) : (0, 0);
    }

    private static string DecodeText(byte[] edid, int start)
    {
        var end = Math.Min(start + 13, edid.Length);
        var text = new char[end - start];
        var length = 0;
        for (var index = start; index < end; index++)
        {
            if (edid[index] == 0x0A)
            {
                break;
            }

            text[length++] = (char)edid[index];
        }

        return new string(text, 0, length).Trim();
    }

    private static PanelIdentity Unavailable(string reason)
        => new(false, string.Empty, string.Empty, 0, 0, 0, 0, reason);
}

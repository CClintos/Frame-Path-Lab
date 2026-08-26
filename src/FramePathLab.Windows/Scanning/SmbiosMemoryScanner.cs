using System.Runtime.InteropServices;
using System.Text;
using FramePathLab.Core.Models;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the memory configuration straight out of the firmware tables.
///
/// This matters more than any Windows setting on a cache- and latency-sensitive CPU: a kit sitting
/// at its JEDEC fallback instead of its rated profile, or two modules landing in the wrong slot
/// pair, costs more frame consistency than the entire rest of this catalogue combined. Both are
/// invisible from inside Windows unless the firmware tables are parsed.
///
/// SMBIOS is read through the documented GetSystemFirmwareTable entry point, so there is no WMI
/// dependency and no elevation requirement.
/// </summary>
public static class SmbiosMemoryScanner
{
    // 'RSMB' as a little-endian DWORD, the raw SMBIOS firmware table provider.
    private const uint RawSmbiosProvider = 0x52534D42;
    private const byte MemoryDeviceType = 17;
    private const int RawSmbiosHeaderBytes = 8;

    public static MemoryConfiguration Scan()
    {
        var table = ReadFirmwareTable();
        if (table.Length <= RawSmbiosHeaderBytes)
        {
            return MemoryConfiguration.Unavailable("Firmware memory tables could not be read on this system.");
        }

        var modules = new List<MemoryModule>();
        var offset = RawSmbiosHeaderBytes;
        while (offset + 4 <= table.Length)
        {
            var structureType = table[offset];
            var structureLength = table[offset + 1];
            if (structureLength < 4 || offset + structureLength > table.Length)
            {
                break;
            }

            var strings = ReadStringSet(table, offset + structureLength, out var next);
            if (structureType == MemoryDeviceType)
            {
                var module = ParseMemoryDevice(table, offset, structureLength, strings);
                if (module is not null)
                {
                    modules.Add(module);
                }
            }

            if (next <= offset)
            {
                break;
            }

            offset = next;
        }

        return Summarize(modules);
    }

    private static MemoryModule? ParseMemoryDevice(
        byte[] table,
        int offset,
        byte length,
        IReadOnlyList<string> strings)
    {
        // Size lives at 0x0C; a zero size means the slot is empty.
        if (length < 0x15)
        {
            return null;
        }

        var rawSize = BitConverter.ToUInt16(table, offset + 0x0C);
        if (rawSize == 0)
        {
            return null;
        }

        long sizeMegabytes;
        if (rawSize == 0x7FFF && length >= 0x20)
        {
            // 0x7FFF is the escape value meaning "read the 32-bit extended size instead".
            sizeMegabytes = BitConverter.ToUInt32(table, offset + 0x1C) & 0x7FFFFFFF;
        }
        else
        {
            // Bit 15 clear means megabytes, set means kilobytes.
            sizeMegabytes = (rawSize & 0x8000) != 0 ? (rawSize & 0x7FFF) / 1024 : rawSize & 0x7FFF;
        }

        var ratedSpeed = length >= 0x17 ? BitConverter.ToUInt16(table, offset + 0x15) : (ushort)0;
        var configuredSpeed = length >= 0x22 ? BitConverter.ToUInt16(table, offset + 0x20) : (ushort)0;

        return new MemoryModule(
            ReadString(strings, table[offset + 0x10]),
            ReadString(strings, table[offset + 0x11]),
            ReadString(strings, length >= 0x1B ? table[offset + 0x1A] : (byte)0),
            ReadString(strings, length >= 0x18 ? table[offset + 0x17] : (byte)0),
            sizeMegabytes,
            ratedSpeed,
            configuredSpeed);
    }

    private static MemoryConfiguration Summarize(List<MemoryModule> modules)
    {
        if (modules.Count == 0)
        {
            return MemoryConfiguration.Unavailable("No populated memory modules were reported by firmware.");
        }

        var configured = modules.Where(module => module.ConfiguredSpeedMts > 0).ToArray();
        var rated = modules.Where(module => module.RatedSpeedMts > 0).ToArray();
        var configuredSpeed = configured.Length > 0 ? configured.Min(module => module.ConfiguredSpeedMts) : 0;
        var ratedSpeed = rated.Length > 0 ? rated.Max(module => module.RatedSpeedMts) : 0;

        // Channel population is inferred from the distinct channel letter in each slot's locator,
        // which is how every mainstream desktop firmware names its slots (DIMMA1, DIMMB2, ...).
        var channels = modules
            .Select(module => ExtractChannel(module.DeviceLocator))
            .Where(channel => channel is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new MemoryConfiguration(
            true,
            modules,
            modules.Sum(module => module.SizeMegabytes),
            configuredSpeed,
            ratedSpeed,
            channels > 0 ? channels : modules.Count,
            string.Empty);
    }

    /// <summary>Returns the channel letter from a slot locator such as "DIMMA1" or "ChannelB-DIMM0".</summary>
    private static string? ExtractChannel(string deviceLocator)
    {
        if (string.IsNullOrWhiteSpace(deviceLocator))
        {
            return null;
        }

        var upper = deviceLocator.ToUpperInvariant();
        var channelIndex = upper.IndexOf("CHANNEL", StringComparison.Ordinal);
        if (channelIndex >= 0 && channelIndex + 7 < upper.Length)
        {
            return upper[channelIndex + 7].ToString();
        }

        var dimmIndex = upper.IndexOf("DIMM", StringComparison.Ordinal);
        if (dimmIndex >= 0 && dimmIndex + 4 < upper.Length)
        {
            var candidate = upper[dimmIndex + 4];
            if (char.IsLetter(candidate))
            {
                return candidate.ToString();
            }
        }

        return null;
    }

    private static byte[] ReadFirmwareTable()
    {
        var size = NativeGetSystemFirmwareTable(RawSmbiosProvider, 0, null, 0);
        if (size == 0)
        {
            return [];
        }

        var buffer = new byte[size];
        return NativeGetSystemFirmwareTable(RawSmbiosProvider, 0, buffer, size) == 0 ? [] : buffer;
    }

    /// <summary>
    /// Reads the null-terminated string set that follows a structure's formatted area, and returns
    /// the offset of the next structure.
    /// </summary>
    private static IReadOnlyList<string> ReadStringSet(byte[] table, int start, out int nextStructure)
    {
        var strings = new List<string>();
        var index = start;

        // A structure with no strings is terminated by a double null rather than a single one.
        if (index + 1 < table.Length && table[index] == 0 && table[index + 1] == 0)
        {
            nextStructure = index + 2;
            return strings;
        }

        var builder = new StringBuilder();
        while (index < table.Length)
        {
            if (table[index] == 0)
            {
                strings.Add(builder.ToString());
                builder.Clear();
                if (index + 1 < table.Length && table[index + 1] == 0)
                {
                    nextStructure = index + 2;
                    return strings;
                }
            }
            else
            {
                builder.Append((char)table[index]);
            }

            index++;
        }

        nextStructure = table.Length;
        return strings;
    }

    private static string ReadString(IReadOnlyList<string> strings, byte index)
        => index == 0 || index > strings.Count ? string.Empty : strings[index - 1].Trim();

    [DllImport("kernel32.dll", EntryPoint = "GetSystemFirmwareTable", SetLastError = true)]
    private static extern uint NativeGetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        byte[]? buffer,
        uint bufferSize);
}

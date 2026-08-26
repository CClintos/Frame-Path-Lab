using System.Diagnostics;
using System.Runtime.InteropServices;
using FramePathLab.Core.Models;
using FramePathLab.Windows.Interop;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Resolves the scheduling topology that actually decides frame consistency on a modern CPU:
/// which logical processors share a last-level cache (a CCD/CCX on AMD), which are performance
/// versus efficiency cores on a hybrid Intel part, and where the game is currently allowed to run.
/// </summary>
public static class CpuTopologyScanner
{
    // A vertical-cache die carries roughly triple the L3 of its sibling. Requiring a 1.5x margin
    // identifies it without misreading ordinary per-CCD variation as a cache stack.
    private const double VerticalCacheRatioThreshold = 1.5;

    public static CpuTopology Scan(int? gameProcessId)
    {
        var buffer = QueryProcessorInformation(ExpertNativeMethods.RelationProcessorCore);
        var cacheBuffer = QueryProcessorInformation(ExpertNativeMethods.RelationCache);
        var cores = ParseCores(buffer);
        var l3Groups = ParseLastLevelCaches(cacheBuffer);

        var vendor = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", "unknown");
        var brand = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "unknown");

        var notes = new List<string>();
        var groups = BuildCoreGroups(cores, l3Groups, notes);
        var isHybrid = cores.Select(core => core.EfficiencyClass).Distinct().Count() > 1;
        var smt = cores.Any(core => core.LogicalCount > 1);

        var (preferredIndex, preferredMask, reason) = ChoosePreferredGroup(groups, isHybrid, notes);
        var (processMask, systemMask) = ReadAffinity(gameProcessId);
        var (maxMhz, currentMhz, mhzLimit) = ReadProcessorPower();

        return new CpuTopology(
            vendor,
            brand,
            cores.Count,
            cores.Sum(core => core.LogicalCount),
            smt,
            isHybrid,
            groups,
            preferredIndex,
            reason,
            preferredMask,
            systemMask,
            processMask,
            maxMhz,
            currentMhz,
            mhzLimit,
            notes);
    }

    private static byte[] QueryProcessorInformation(int relationship)
    {
        uint length = 0;
        ExpertNativeMethods.GetLogicalProcessorInformationEx(relationship, null, ref length);
        if (length == 0)
        {
            return [];
        }

        var buffer = new byte[length];
        return ExpertNativeMethods.GetLogicalProcessorInformationEx(relationship, buffer, ref length)
            ? buffer
            : [];
    }

    private readonly record struct CoreEntry(int EfficiencyClass, int LogicalCount, ulong Mask, ushort Group);

    private readonly record struct CacheEntry(ulong Mask, ushort Group, ulong SizeBytes);

    /// <summary>
    /// SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX is variable length, so the buffer is walked by the
    /// per-record Size field rather than by a fixed stride.
    /// </summary>
    private static List<CoreEntry> ParseCores(byte[] buffer)
    {
        var cores = new List<CoreEntry>();
        var offset = 0;
        while (offset + 8 <= buffer.Length)
        {
            var relationship = BitConverter.ToInt32(buffer, offset);
            var size = BitConverter.ToInt32(buffer, offset + 4);
            if (size <= 0 || offset + size > buffer.Length)
            {
                break;
            }

            // PROCESSOR_RELATIONSHIP: Flags at +8, EfficiencyClass at +9, GroupCount at +30,
            // GroupMask[0] at +32 (KAFFINITY at +32, Group WORD at +40).
            if (relationship == ExpertNativeMethods.RelationProcessorCore && offset + 42 <= buffer.Length)
            {
                var flags = buffer[offset + 8];
                var efficiencyClass = buffer[offset + 9];
                var mask = BitConverter.ToUInt64(buffer, offset + 32);
                var group = BitConverter.ToUInt16(buffer, offset + 40);
                var logicalCount = (flags & ExpertNativeMethods.LtpPcSmt) != 0
                    ? System.Numerics.BitOperations.PopCount(mask)
                    : 1;
                cores.Add(new CoreEntry(efficiencyClass, Math.Max(1, logicalCount), mask, group));
            }

            offset += size;
        }

        return cores;
    }

    private static List<CacheEntry> ParseLastLevelCaches(byte[] buffer)
    {
        var caches = new List<CacheEntry>();
        var offset = 0;
        while (offset + 8 <= buffer.Length)
        {
            var relationship = BitConverter.ToInt32(buffer, offset);
            var size = BitConverter.ToInt32(buffer, offset + 4);
            if (size <= 0 || offset + size > buffer.Length)
            {
                break;
            }

            // CACHE_RELATIONSHIP: Level at +8, CacheSize at +12, Type at +16, then 20 reserved
            // bytes, placing the first GROUP_AFFINITY at +40 (KAFFINITY at +40, Group WORD at +48)
            // in both the legacy and the GroupCount-carrying layout.
            if (relationship == ExpertNativeMethods.RelationCache && offset + 50 <= buffer.Length)
            {
                var level = buffer[offset + 8];
                var cacheSize = BitConverter.ToUInt32(buffer, offset + 12);
                var cacheType = BitConverter.ToInt32(buffer, offset + 16);
                var mask = BitConverter.ToUInt64(buffer, offset + 40);
                var group = BitConverter.ToUInt16(buffer, offset + 48);

                // Unified (0) and Data (2) L3 describe the shared victim cache a game benefits from.
                if (level == 3 && (cacheType == 0 || cacheType == 2) && mask != 0)
                {
                    caches.Add(new CacheEntry(mask, group, cacheSize));
                }
            }

            offset += size;
        }

        return caches;
    }

    private static List<CoreGroup> BuildCoreGroups(
        List<CoreEntry> cores,
        List<CacheEntry> caches,
        List<string> notes)
    {
        var groups = new List<CoreGroup>();
        if (caches.Count > 0)
        {
            var index = 0;
            ulong covered = 0;
            foreach (var cache in caches.OrderByDescending(entry => entry.SizeBytes).ThenBy(entry => entry.Mask))
            {
                var member = cores.Where(core => (core.Mask & cache.Mask) != 0).ToArray();
                if (member.Length == 0)
                {
                    continue;
                }

                covered |= cache.Mask;
                groups.Add(new CoreGroup(
                    index++,
                    cache.Mask,
                    member.Length,
                    member.Sum(core => core.LogicalCount),
                    cache.SizeBytes,
                    member.Max(core => core.EfficiencyClass)));
            }

            // Recent hybrid parts place a low-power core island outside the ring's last-level
            // cache. Those cores appear in no L3 relation at all, so without this they would be
            // dropped from the topology and the placement decision would wrongly look uniform.
            var uncovered = cores.Where(core => (core.Mask & covered) == 0).ToArray();
            if (uncovered.Length > 0)
            {
                ulong mask = 0;
                foreach (var core in uncovered)
                {
                    mask |= core.Mask;
                }

                notes.Add(
                    $"{uncovered.Length} core(s) sit outside any last-level cache domain; "
                    + "these are treated as a separate low-power island.");
                groups.Add(new CoreGroup(
                    index,
                    mask,
                    uncovered.Length,
                    uncovered.Sum(core => core.LogicalCount),
                    0,
                    uncovered.Max(core => core.EfficiencyClass)));
            }
        }

        if (groups.Count == 0)
        {
            notes.Add("No L3 cache topology was returned; falling back to efficiency-class grouping.");
            foreach (var byClass in cores.GroupBy(core => core.EfficiencyClass).OrderByDescending(group => group.Key))
            {
                ulong mask = 0;
                foreach (var core in byClass)
                {
                    mask |= core.Mask;
                }

                groups.Add(new CoreGroup(
                    groups.Count,
                    mask,
                    byClass.Count(),
                    byClass.Sum(core => core.LogicalCount),
                    0,
                    byClass.Key));
            }
        }

        return groups;
    }

    /// <summary>
    /// Picks the die or core class a latency-sensitive game should be pinned to. Cache asymmetry
    /// wins over core count, then performance class, and a uniform CPU deliberately returns none
    /// so the catalogue never invents a pinning recommendation that cannot help.
    /// </summary>
    private static (int? Index, ulong Mask, string Reason) ChoosePreferredGroup(
        IReadOnlyList<CoreGroup> groups,
        bool isHybrid,
        List<string> notes)
    {
        if (groups.Count <= 1)
        {
            return (null, 0, groups.Count == 1
                ? "Single unified core group; no placement decision exists on this CPU."
                : "Core topology could not be resolved.");
        }

        var withCache = groups.Where(group => group.LastLevelCacheBytes > 0).ToArray();
        if (withCache.Length > 1)
        {
            var largest = withCache.MaxBy(group => group.LastLevelCacheBytes)!;
            var smallest = withCache.MinBy(group => group.LastLevelCacheBytes)!;
            if (largest.LastLevelCacheBytes >= (ulong)(smallest.LastLevelCacheBytes * VerticalCacheRatioThreshold))
            {
                notes.Add(
                    $"Asymmetric last-level cache detected: {largest.LastLevelCacheMiB:0.#} MiB versus "
                    + $"{smallest.LastLevelCacheMiB:0.#} MiB. This is the vertical-cache die signature.");
                return (largest.GroupIndex, largest.AffinityMask,
                    $"Die {largest.GroupIndex} carries {largest.LastLevelCacheMiB:0.#} MiB of last-level cache "
                    + $"against {smallest.LastLevelCacheMiB:0.#} MiB on its sibling.");
            }
        }

        if (isHybrid)
        {
            var performanceClass = groups.Max(group => group.EfficiencyClass);
            ulong mask = 0;
            foreach (var group in groups.Where(group => group.EfficiencyClass == performanceClass))
            {
                mask |= group.AffinityMask;
            }

            var performanceGroup = groups.First(group => group.EfficiencyClass == performanceClass);
            return (performanceGroup.GroupIndex, mask,
                "Hybrid CPU: the highest efficiency class is the performance-core set.");
        }

        return (null, 0, "Core groups are symmetric; pinning cannot deliver a cache or class advantage.");
    }

    private static (ulong? ProcessMask, ulong SystemMask) ReadAffinity(int? gameProcessId)
    {
        if (gameProcessId is null)
        {
            using var current = Process.GetCurrentProcess();
            return ExpertNativeMethods.GetProcessAffinityMask(current.Handle, out _, out var system)
                ? (null, (ulong)system)
                : (null, 0UL);
        }

        var handle = ExpertNativeMethods.OpenProcess(
            ExpertNativeMethods.ProcessQueryLimitedInformation,
            false,
            (uint)gameProcessId.Value);
        if (handle == 0)
        {
            using var current = Process.GetCurrentProcess();
            var readable = ExpertNativeMethods.GetProcessAffinityMask(current.Handle, out _, out var fallback);
            return (null, readable ? (ulong)fallback : 0UL);
        }

        try
        {
            return ExpertNativeMethods.GetProcessAffinityMask(handle, out var process, out var system)
                ? ((ulong?)process, (ulong)system)
                : (null, 0UL);
        }
        finally
        {
            ExpertNativeMethods.CloseHandle(handle);
        }
    }

    private static (int? MaxMhz, int? CurrentMhz, int? MhzLimit) ReadProcessorPower()
    {
        var count = Environment.ProcessorCount;
        var entrySize = Marshal.SizeOf<ProcessorPowerInformation>();
        var buffer = new byte[entrySize * count];
        var status = ExpertNativeMethods.CallNtPowerInformation(
            ExpertNativeMethods.ProcessorInformationLevel,
            nint.Zero,
            0,
            buffer,
            (uint)buffer.Length);
        if (status != 0)
        {
            return (null, null, null);
        }

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var basePointer = handle.AddrOfPinnedObject();
            var maxMhz = 0;
            var currentMhz = 0;
            var mhzLimit = 0;
            for (var index = 0; index < count; index++)
            {
                var entry = Marshal.PtrToStructure<ProcessorPowerInformation>(basePointer + (index * entrySize));
                maxMhz = Math.Max(maxMhz, (int)entry.MaxMhz);
                currentMhz = Math.Max(currentMhz, (int)entry.CurrentMhz);
                mhzLimit = Math.Max(mhzLimit, (int)entry.MhzLimit);
            }

            return (maxMhz, currentMhz, mhzLimit);
        }
        finally
        {
            handle.Free();
        }
    }

    private static string ReadRegistryString(string path, string name, string fallback)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            return key?.GetValue(name) as string is { Length: > 0 } value ? value.Trim() : fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Persistence;

/// <summary>
/// Reads and writes the two portable file types: a machine snapshot and a chosen tweak set.
/// </summary>
public static class MachineSnapshotStore
{
    public const string SnapshotExtension = ".fplscan";
    public const string PlanExtension = ".fplplan";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,

        // Enum names rather than ordinals, so inserting a value into an enum later cannot silently
        // reinterpret an existing snapshot as a different kind of thing.
        Converters = { new JsonStringEnumConverter() },

        // A measurement that genuinely came back as not-a-number is data. Without this the whole
        // snapshot would fail to write because one statistic was undefined.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,

        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropComputedProperties }
        }
    };

    /// <summary>
    /// Removes properties the models derive rather than store.
    ///
    /// Positional record parameters carry init accessors and so survive; expression-bodied
    /// conveniences such as <c>HasStackedCache</c> do not. Dropping them keeps the file to the
    /// facts, and avoids a computed double that came out as infinity taking the whole write down.
    /// </summary>
    private static void DropComputedProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var index = info.Properties.Count - 1; index >= 0; index--)
        {
            if (info.Properties[index].Set is null)
            {
                info.Properties.RemoveAt(index);
            }
        }
    }

    public static void WriteSnapshot(string path, MachineSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        WriteAtomic(path, JsonSerializer.Serialize(snapshot, Options));
    }

    public static MachineSnapshot ReadSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = JsonSerializer.Deserialize<MachineSnapshot>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The snapshot file is empty.");

        if (snapshot.FormatVersion != MachineSnapshot.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Snapshot format {snapshot.FormatVersion} was written by a different version of "
                + $"FramePath Lab; this build reads format {MachineSnapshot.CurrentFormatVersion}. "
                + "Re-collect on the target machine with a matching build.");
        }

        return snapshot;
    }

    public static void WritePlan(string path, TweakPlanFile plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(plan);
        WriteAtomic(path, JsonSerializer.Serialize(plan, Options));
    }

    public static TweakPlanFile ReadPlan(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var plan = JsonSerializer.Deserialize<TweakPlanFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The plan file is empty.");

        if (plan.FormatVersion != TweakPlanFile.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Plan format {plan.FormatVersion} was written by a different version of FramePath "
                + $"Lab; this build reads format {TweakPlanFile.CurrentFormatVersion}.");
        }

        return plan;
    }

    public static void WriteReport(string path, PlanApplicationReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        WriteAtomic(path, JsonSerializer.Serialize(report, Options));
    }

    /// <summary>
    /// Derives a stable identifier for a machine from properties that do not change between boots.
    ///
    /// Deliberately not a security measure and not unique in any cryptographic sense — two
    /// identically specified machines with the same name would collide. It exists to catch the
    /// realistic mistake, which is carrying a plan back to the wrong computer.
    /// </summary>
    public static string Fingerprint(
        string machineName,
        string processorBrand,
        int physicalCores,
        int logicalProcessors,
        ulong totalMemoryBytes)
    {
        var material = string.Join(
            '|',
            machineName?.Trim().ToUpperInvariant() ?? string.Empty,
            processorBrand?.Trim() ?? string.Empty,
            physicalCores.ToString(CultureInfo.InvariantCulture),
            logicalProcessors.ToString(CultureInfo.InvariantCulture),

            // Reported physical memory moves by a few hundred megabytes depending on what the
            // firmware reserved, and it sits just under a round number rather than on one. Truncating
            // would therefore put a cliff exactly where every real machine sits: 31.9 GB truncates to
            // 31, and reserving a little more after a firmware update would drop it to 30 and refuse
            // every plan already written for that machine. Rounding to nearest puts the boundary at
            // half a gigabyte, where no real memory size lives.
            Math.Round(totalMemoryBytes / 1024d / 1024 / 1024)
                .ToString("F0", CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
    }

    public static MachineIdentity IdentityFor(ExpertScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var machineName = System.Environment.MachineName;
        var brand = string.IsNullOrWhiteSpace(context.Cpu.Brand) ? "Unknown CPU" : context.Cpu.Brand;
        var memory = context.Environment.TotalPhysicalMemoryBytes;

        return new MachineIdentity(
            machineName,
            Fingerprint(
                machineName,
                brand,
                context.Cpu.PhysicalCoreCount,
                context.Cpu.LogicalProcessorCount,
                memory),
            brand,
            context.Cpu.PhysicalCoreCount,
            context.Cpu.LogicalProcessorCount,
            memory,
            context.Environment.OsVersion,
            context.Gpus.Count > 0 ? context.Gpus[0].Name : string.Empty);
    }

    private static void WriteAtomic(string path, string payload)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = full + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, payload, new UTF8Encoding(false));
        try
        {
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // The write already failed; a leftover temporary file is the lesser problem.
            }

            throw;
        }
    }
}

using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

/// <summary>
/// The identity of a question asked of a machine.
///
/// Kind, target and value name are the three things that decide what gets read, and all three are
/// derived from the scan context rather than from anything live. Because the context travels with
/// the snapshot, the catalogue builds byte-identical keys on the reviewing machine, which is what
/// makes replay exact rather than approximate.
/// </summary>
public static class ReadKey
{
    public static string For(MutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return $"{plan.Kind}|{plan.Target}|{plan.ValueName}";
    }
}

/// <summary>
/// Passes reads through to the real machine and keeps a copy of every answer.
///
/// Wrapping rather than reimplementing matters: the recording is of what the actual executor said,
/// so a snapshot cannot drift from live behaviour as the executor changes.
/// </summary>
public sealed class RecordingStateReader(ITweakStateReader inner) : ITweakStateReader
{
    private readonly ITweakStateReader _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Dictionary<string, RecordedRead> _reads = new(StringComparer.Ordinal);

    public string? Read(MutationPlan plan, out bool exists)
    {
        var value = _inner.Read(plan, out exists);
        var key = ReadKey.For(plan);
        _reads[key] = new RecordedRead(key, exists, value);
        return value;
    }

    /// <summary>Sorted so two snapshots of an unchanged machine differ only where the machine did.</summary>
    public IReadOnlyList<RecordedRead> Recorded
        => _reads.Values.OrderBy(read => read.Key, StringComparer.Ordinal).ToArray();
}

/// <summary>
/// Answers reads from a snapshot instead of from the machine running the code.
///
/// A question with no recorded answer reports the surface as absent rather than guessing at one.
/// That is the same shape a machine genuinely lacking the surface produces, so the card degrades to
/// "not readable" and offers no write — which is the correct outcome, because a snapshot taken by
/// an older collector genuinely does not know the answer.
/// </summary>
public sealed class ReplayStateReader : ITweakStateReader
{
    private readonly Dictionary<string, RecordedRead> _reads;

    public ReplayStateReader(IEnumerable<RecordedRead> reads)
    {
        ArgumentNullException.ThrowIfNull(reads);
        _reads = reads
            .Where(read => !string.IsNullOrEmpty(read.Key))
            .GroupBy(read => read.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    /// <summary>Reads the snapshot answered for but the catalogue never asked about on review.</summary>
    public int RecordedCount => _reads.Count;

    public string? Read(MutationPlan plan, out bool exists)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_reads.TryGetValue(ReadKey.For(plan), out var recorded))
        {
            exists = recorded.Exists;
            return recorded.Value;
        }

        exists = false;
        return null;
    }
}

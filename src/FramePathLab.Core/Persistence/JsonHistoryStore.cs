using System.Text.Json;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Persistence;

public sealed class JsonHistoryStore : IHistoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonHistoryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "history.v1.json");
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false)).ToList();
            entries.Insert(0, entry);
            if (entries.Count > 200)
            {
                entries.RemoveRange(200, entries.Count - 200);
            }

            await WriteUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var candidate in EnumerateOwnedHistoryFiles())
            {
                File.Delete(candidate);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private IEnumerable<string> EnumerateOwnedHistoryFiles()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "history*.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(_directory, "history-*.tmp", SearchOption.TopDirectoryOnly))
            .Where(path => Path.GetDirectoryName(Path.GetFullPath(path))
                ?.Equals(_directory, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
    }

    private async Task<IReadOnlyList<HistoryEntry>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(
                       stream,
                       SerializerOptions,
                       cancellationToken).ConfigureAwait(false)
                   ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local history is malformed; it was not overwritten.", exception);
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyList<HistoryEntry> entries,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, $"history-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16_384,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    entries,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                var backup = Path.Combine(_directory, "history.previous.json");
                File.Replace(temporary, _path, backup, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

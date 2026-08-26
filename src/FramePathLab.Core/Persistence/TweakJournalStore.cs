using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Persistence;

/// <summary>
/// Durable ledger of every applied expert tweak.
///
/// The ledger is the reason an apply is safe to offer: it survives a crash, a reboot and an
/// uninstall of the running build, so a change can always be traced back to its exact prior value
/// and undone. Its SHA detects accidental corruption, not malicious same-user modification; the
/// full application therefore never treats this user-writable file as a privileged command source.
/// Writes are atomically replaced and bounded in size.
/// </summary>
public sealed class TweakJournalStore
{
    private const int MaximumJournalBytes = 1024 * 1024;
    private const int MaximumTransactions = 512;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _mutexName;

    public TweakJournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "expert-tweaks.v1.json");
        _backupPath = Path.Combine(_directory, "expert-tweaks.previous.json");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_directory.ToUpperInvariant())))[..24];
        _mutexName = $"Global\\FramePathLab.ExpertTweaks.{hash}";
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<TweakTransaction> Read() => WithLock(ReadUnsafe);

    /// <summary>Appends a new transaction, or replaces an existing one with the same identifier.</summary>
    public void Upsert(TweakTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        WithLock(() =>
        {
            var existing = ReadUnsafe().ToList();
            var index = existing.FindIndex(entry => entry.TransactionId == transaction.TransactionId);
            if (index >= 0)
            {
                existing[index] = transaction;
            }
            else
            {
                existing.Add(transaction);
            }

            // Resolved transactions are the ones safe to shed once the ledger is full; anything
            // still outstanding is retained so it never becomes unrevertible.
            if (existing.Count > MaximumTransactions)
            {
                var resolved = existing.Where(entry => !entry.IsOutstanding)
                    .OrderBy(entry => entry.AppliedAtUtc)
                    .Take(existing.Count - MaximumTransactions)
                    .ToHashSet();
                existing = existing.Where(entry => !resolved.Contains(entry)).ToList();
            }

            WriteUnsafe(existing);
            return true;
        });
    }

    public void DeleteResolved()
        => WithLock(() =>
        {
            WriteUnsafe(ReadUnsafe().Where(entry => entry.IsOutstanding).ToList());
            return true;
        });

    private List<TweakTransaction> ReadUnsafe()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var bytes = File.ReadAllBytes(_path);
            if (bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidDataException("The expert-tweak journal exceeded its bounded size.");
            }

            var envelope = JsonSerializer.Deserialize<JournalEnvelope>(bytes, SerializerOptions);
            if (envelope?.Transactions is null)
            {
                return [];
            }

            var recomputed = Convert.ToHexString(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope.Transactions, SerializerOptions)));
            if (!string.Equals(recomputed, envelope.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The expert-tweak journal failed its corruption check and was not used. "
                    + "Reverting from a modified ledger could write the wrong prior value.");
            }

            return envelope.Transactions.ToList();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The expert-tweak journal could not be parsed.", exception);
        }
    }

    private void WriteUnsafe(List<TweakTransaction> transactions)
    {
        Directory.CreateDirectory(_directory);
        var payload = JsonSerializer.SerializeToUtf8Bytes(transactions, SerializerOptions);
        var envelope = new JournalEnvelope(transactions, Convert.ToHexString(SHA256.HashData(payload)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (bytes.Length > MaximumJournalBytes)
        {
            throw new InvalidDataException("The expert-tweak journal exceeded its bounded size.");
        }

        var temporary = Path.Combine(_directory, $"expert-tweaks-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, _backupPath, ignoreMetadataErrors: false);
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

    private T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Another FramePath Lab instance is holding the expert-tweak journal.");
            }

            return action();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private sealed record JournalEnvelope(IReadOnlyList<TweakTransaction> Transactions, string Sha256);
}

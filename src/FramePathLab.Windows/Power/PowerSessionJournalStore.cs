using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Core.Services;

namespace FramePathLab.Windows.Power;

public sealed class PowerSessionJournalStore : IPowerSessionJournal
{
    private const int MaximumJournalBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _mutexName;

    public PowerSessionJournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "power-session.v1.json");
        _backupPath = Path.Combine(_directory, "power-session.previous.json");
        var normalizedDirectory = _directory.ToUpperInvariant();
        var directoryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory)))[..24];
        _mutexName = $"Global\\FramePathLab.PowerSession.{directoryHash}";
    }

    public string DirectoryPath => _directory;

    public string GetGuardianAckPath(Guid sessionId)
        => Path.Combine(_directory, $"power-guardian-{sessionId:N}.ack");

    public PowerSessionRecord? Read()
        => WithLock(ReadUnsafe);

    public void Write(PowerSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);
        WithLock(() =>
        {
            EnsureSafeDirectory();
            var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record, SerializerOptions);
            var envelope = new JournalEnvelope(record, Convert.ToHexString(SHA256.HashData(recordBytes)));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
            if (bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidDataException("The power-session journal exceeded its bounded size.");
            }

            var temporary = Path.Combine(_directory, $"power-session-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           16_384,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                {
                    RejectReparsePoint(_path);
                    if (File.Exists(_backupPath))
                    {
                        RejectReparsePoint(_backupPath);
                    }

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
        });
    }

    private PowerSessionRecord? ReadUnsafe()
    {
        if (Directory.Exists(_directory))
        {
            RejectReparsePoint(_directory);
        }

        if (!File.Exists(_path))
        {
            return File.Exists(_backupPath)
                ? ReadRecordFile(_backupPath)
                : null;
        }

        try
        {
            return ReadRecordFile(_path);
        }
        catch (InvalidDataException currentException) when (File.Exists(_backupPath))
        {
            try
            {
                return ReadRecordFile(_backupPath);
            }
            catch (InvalidDataException backupException)
            {
                throw new InvalidDataException(
                    "Both the current and previous power-session journals are invalid; no automatic write was attempted.",
                    new AggregateException(currentException, backupException));
            }
        }
    }

    private static PowerSessionRecord ReadRecordFile(string path)
    {
        RejectReparsePoint(path);
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumJournalBytes)
        {
            throw new InvalidDataException("The power-session journal has an invalid size and was not used.");
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var envelope = JsonSerializer.Deserialize<JournalEnvelope>(bytes, SerializerOptions)
                ?? throw new InvalidDataException("The power-session journal is empty.");
            ValidateRecord(envelope.Record);
            if (string.IsNullOrWhiteSpace(envelope.RecordSha256)
                || envelope.RecordSha256.Length != 64
                || envelope.RecordSha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The power-session journal checksum has an invalid format.");
            }

            var recordBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Record, SerializerOptions);
            var expected = Convert.ToHexString(SHA256.HashData(recordBytes));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(envelope.RecordSha256)))
            {
                throw new InvalidDataException("The power-session journal checksum does not match; no automatic write was attempted.");
            }

            return envelope.Record;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The power-session journal is malformed and was not overwritten.", exception);
        }
    }

    private void EnsureSafeDirectory()
    {
        Directory.CreateDirectory(_directory);
        RejectReparsePoint(_directory);
    }

    private static void ValidateRecord(PowerSessionRecord record)
    {
        if (record.SessionId == Guid.Empty
            || record.GuardianNonce == Guid.Empty
            || record.Operation != PowerSessionCoordinator.Operation
            || record.SchemaVersion != PowerSessionCoordinator.SchemaVersion
            || record.TargetSchemeId != PowerSessionCoordinator.HighPerformanceSchemeId
            || record.OriginalSchemeId == Guid.Empty
            || record.OriginalSchemeId == record.TargetSchemeId
            || record.OwnerProcessId <= 0
            || record.OwnerProcessStartTimeUtcTicks <= 0
            || !Enum.IsDefined(record.State)
            || record.UpdatedAtUtc < record.CreatedAtUtc
            || record.ExpiresAtUtc <= record.CreatedAtUtc
            || record.ExpiresAtUtc - record.CreatedAtUtc > TimeSpan.FromHours(2)
            || string.IsNullOrWhiteSpace(record.OriginalSchemeName)
            || record.OriginalSchemeName.Length > 1024
            || string.IsNullOrWhiteSpace(record.TargetSchemeName)
            || record.TargetSchemeName.Length > 1024
            || string.IsNullOrWhiteSpace(record.LastObservation)
            || record.LastObservation.Length > 4096
            || record.Failure?.Length > 4096)
        {
            throw new InvalidDataException("The power-session journal contains an unsupported or invalid transaction.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Power-session state cannot use a reparse point.");
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
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out waiting for the power-session journal lock.");
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

    private void WithLock(Action action)
        => WithLock(() =>
        {
            action();
            return true;
        });

    private sealed record JournalEnvelope(PowerSessionRecord Record, string RecordSha256);
}

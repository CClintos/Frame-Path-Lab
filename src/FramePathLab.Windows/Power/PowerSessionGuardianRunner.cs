using System.Diagnostics;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Interop;

namespace FramePathLab.Windows.Power;

public static class PowerSessionGuardianRunner
{
    public static int Run(
        Guid sessionId,
        Guid nonce,
        int ownerProcessId,
        PowerSessionJournalStore journal,
        IPowerSchemeController controller)
    {
        string? acknowledgementPath = null;
        PowerSessionRecord? recoverySnapshot = null;
        var acknowledged = false;
        try
        {
            var record = journal.Read();
            if (record is null
                || record.SessionId != sessionId
                || record.GuardianNonce != nonce
                || record.OwnerProcessId != ownerProcessId
                || !PowerSessionCoordinator.IsUnresolved(record.State))
            {
                return 2;
            }

            recoverySnapshot = record;
            var remainingLease = record.ExpiresAtUtc - DateTimeOffset.UtcNow;
            if (remainingLease <= TimeSpan.Zero
                || !ProcessIdentityMatches(record.OwnerProcessId, record.OwnerProcessStartTimeUtcTicks)
                || !NativeMethods.GetSystemPowerStatus(out var initialPowerStatus)
                || initialPowerStatus.AcLineStatus != 1)
            {
                return 2;
            }

            acknowledgementPath = journal.GetGuardianAckPath(sessionId);
            WriteAcknowledgement(acknowledgementPath, nonce);
            acknowledged = true;
            var leaseClock = Stopwatch.StartNew();

            while (true)
            {
                Thread.Sleep(500);
                record = journal.Read();
                if (record is null || record.SessionId != sessionId)
                {
                    GuardWithoutJournal(recoverySnapshot, controller);
                    return 3;
                }

                recoverySnapshot = record;

                if (!PowerSessionCoordinator.IsUnresolved(record.State))
                {
                    return 0;
                }

                var ownerAlive = ProcessIdentityMatches(
                    record.OwnerProcessId,
                    record.OwnerProcessStartTimeUtcTicks);
                var expired = leaseClock.Elapsed >= remainingLease
                    || DateTimeOffset.UtcNow >= record.ExpiresAtUtc;
                var acLost = !NativeMethods.GetSystemPowerStatus(out var powerStatus)
                    || powerStatus.AcLineStatus != 1;
                var recoveryPending = record.State is PowerSessionState.ApplyFailed
                    or PowerSessionState.VerificationFailed
                    or PowerSessionState.RecoveryFailed;
                if (ownerAlive && !expired && !acLost && !recoveryPending)
                {
                    continue;
                }

                var coordinator = new PowerSessionCoordinator(controller, journal, NoOpGuardian.Instance);
                var reason = !ownerAlive
                    ? "rollback guardian detected that the owner application exited"
                    : expired
                        ? "bounded power-plan lease expired"
                        : acLost
                            ? "AC power was lost or could not be verified"
                            : "a prior apply or recovery step requires immediate rollback";
                coordinator.Revert(sessionId, reason);
                return 0;
            }
        }
        catch
        {
            if (recoverySnapshot is not null)
            {
                if (acknowledged)
                {
                    GuardWithoutJournal(recoverySnapshot, controller);
                }
                else
                {
                    TryCompareAndSwapRestore(recoverySnapshot, controller);
                }
            }

            return 3;
        }
        finally
        {
            if (acknowledgementPath is not null && File.Exists(acknowledgementPath))
            {
                try
                {
                    File.Delete(acknowledgementPath);
                }
                catch
                {
                    // A stale acknowledgement cannot perform a mutation and is ignored.
                }
            }
        }
    }

    public static bool ProcessIdentityMatches(int processId, long expectedStartTimeUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited
                && process.StartTime.ToUniversalTime().Ticks == expectedStartTimeUtcTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void WriteAcknowledgement(string path, Guid nonce)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            throw new IOException("The guardian acknowledgement path already exists.");
        }

        var temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"power-guardian-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(nonce.ToString("D"));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool TryCompareAndSwapRestore(
        PowerSessionRecord record,
        IPowerSchemeController controller)
    {
        try
        {
            var current = controller.GetActiveScheme();
            if (current == record.OriginalSchemeId)
            {
                return true;
            }

            if (current != record.TargetSchemeId)
            {
                return false;
            }

            controller.SetActiveScheme(record.OriginalSchemeId);
            return controller.GetActiveScheme() == record.OriginalSchemeId;
        }
        catch
        {
            return false;
        }
    }

    private static void GuardWithoutJournal(
        PowerSessionRecord record,
        IPowerSchemeController controller)
    {
        var remaining = record.ExpiresAtUtc - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        else if (remaining > TimeSpan.FromHours(2))
        {
            remaining = TimeSpan.FromHours(2);
        }

        var leaseClock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var current = controller.GetActiveScheme();
                if (current == record.TargetSchemeId)
                {
                    TryCompareAndSwapRestore(record, controller);
                    return;
                }

                if (current != record.OriginalSchemeId)
                {
                    return;
                }
            }
            catch
            {
                // Retry until the bounded lease expires or the owner exits.
            }

            if (leaseClock.Elapsed >= remaining
                || !ProcessIdentityMatches(record.OwnerProcessId, record.OwnerProcessStartTimeUtcTicks)
                || !NativeMethods.GetSystemPowerStatus(out var powerStatus)
                || powerStatus.AcLineStatus != 1)
            {
                TryCompareAndSwapRestore(record, controller);
                return;
            }

            Thread.Sleep(100);
        }
    }

    private sealed class NoOpGuardian : IPowerSessionGuardian
    {
        public static readonly NoOpGuardian Instance = new();

        public void Arm(Guid sessionId, Guid nonce, int ownerProcessId)
        {
        }

        public bool IsArmed(Guid sessionId) => true;
    }
}

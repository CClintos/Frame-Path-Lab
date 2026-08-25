using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Services;

public sealed class PowerSessionCoordinator(
    IPowerSchemeController controller,
    IPowerSessionJournal journal,
    IPowerSessionGuardian guardian)
{
    private const string TransactionMutexName = "Global\\FramePathLab.PowerScheme.Transaction.v1";
    private static readonly TimeSpan TransactionLockTimeout = TimeSpan.FromSeconds(10);

    public static readonly Guid HighPerformanceSchemeId =
        Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public const string Operation = "WindowsPowerPlanLeaseV1";
    public const string SchemaVersion = "power-session-v1";

    public PowerSessionOverview Inspect()
    {
        var schemes = controller.EnumerateSchemes();
        var activeId = controller.GetActiveScheme();
        var activeName = schemes.FirstOrDefault(scheme => scheme.Id == activeId)?.Name
            ?? activeId.ToString("D");
        var highPerformanceAvailable = schemes.Any(scheme => scheme.Id == HighPerformanceSchemeId);
        var policyAllowed = false;
        var policyStatus = highPerformanceAvailable
            ? "Power-plan policy has not been checked."
            : "High performance is not installed.";
        if (highPerformanceAvailable)
        {
            try
            {
                controller.EnsureCanSetActiveScheme(HighPerformanceSchemeId);
                policyAllowed = true;
                policyStatus = "Current Group Policy permits active power-plan selection.";
            }
            catch (Exception exception)
            {
                policyStatus = $"Power-plan selection is restricted or unavailable: {Truncate(exception.Message, 512)}";
            }
        }

        var currentJournal = journal.Read();
        var unresolved = currentJournal is not null && IsUnresolved(currentJournal.State);
        var status = unresolved
            ? $"{currentJournal!.TargetSchemeName} session is {currentJournal.State}."
            : "No power-plan experiment is active.";
        return new PowerSessionOverview(
            activeId,
            activeName,
            schemes,
            currentJournal,
            highPerformanceAvailable,
            policyAllowed,
            policyStatus,
            unresolved,
            status);
    }

    public bool IsGuardianArmed(Guid sessionId)
        => guardian.IsArmed(sessionId);

    public PowerSessionTransition ApplyHighPerformance(
        int ownerProcessId,
        long ownerProcessStartTimeUtcTicks,
        Guid expectedOriginalSchemeId,
        bool isOnAcPower,
        TimeSpan leaseDuration)
    {
        using var transactionLock = AcquireTransactionLock();
        return ApplyHighPerformanceCore(
            ownerProcessId,
            ownerProcessStartTimeUtcTicks,
            expectedOriginalSchemeId,
            isOnAcPower,
            leaseDuration);
    }

    private PowerSessionTransition ApplyHighPerformanceCore(
        int ownerProcessId,
        long ownerProcessStartTimeUtcTicks,
        Guid expectedOriginalSchemeId,
        bool isOnAcPower,
        TimeSpan leaseDuration)
    {
        if (ownerProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerProcessId), "A valid owner process identifier is required.");
        }

        if (ownerProcessStartTimeUtcTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownerProcessStartTimeUtcTicks),
                "A valid owner process start time is required.");
        }

        if (expectedOriginalSchemeId == Guid.Empty
            || expectedOriginalSchemeId == HighPerformanceSchemeId)
        {
            throw new ArgumentException(
                "The explicitly approved original power plan is invalid.",
                nameof(expectedOriginalSchemeId));
        }

        if (!isOnAcPower)
        {
            throw new InvalidOperationException("High Performance can only be started when AC power is positively detected.");
        }

        if (leaseDuration < TimeSpan.FromSeconds(1) || leaseDuration > TimeSpan.FromHours(2))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The power-plan lease must be between one second and two hours.");
        }

        var schemes = controller.EnumerateSchemes();
        var target = schemes.SingleOrDefault(scheme => scheme.Id == HighPerformanceSchemeId)
            ?? throw new InvalidOperationException("Windows did not enumerate the built-in High Performance plan. FramePath Lab will not create or unhide it.");
        controller.EnsureCanSetActiveScheme(target.Id);
        var beforeId = controller.GetActiveScheme();
        if (beforeId != expectedOriginalSchemeId)
        {
            throw new InvalidOperationException(
                "The active power plan changed after the approval screen. No change was made; review the new state and approve again.");
        }

        if (beforeId == target.Id)
        {
            throw new InvalidOperationException("High Performance is already active; no change is needed.");
        }

        var before = schemes.SingleOrDefault(scheme => scheme.Id == beforeId)
            ?? throw new InvalidOperationException("The current power plan was not returned by Windows enumeration; no change was made.");
        var existing = journal.Read();
        if (existing is not null && IsUnresolved(existing.State))
        {
            throw new InvalidOperationException("Another power-plan transaction is unresolved. Restore or recover it before starting a new one.");
        }

        var now = DateTimeOffset.UtcNow;
        var prepared = new PowerSessionRecord(
            Guid.NewGuid(),
            Operation,
            SchemaVersion,
            now,
            now,
            now.Add(leaseDuration),
            ownerProcessId,
            ownerProcessStartTimeUtcTicks,
            before.Id,
            before.Name,
            target.Id,
            target.Name,
            PowerSessionState.Prepared,
            Guid.NewGuid(),
            $"Prepared exact change {before.Id:D} -> {target.Id:D}",
            null);
        journal.Write(prepared);

        var applyAttempted = false;
        try
        {
            guardian.Arm(prepared.SessionId, prepared.GuardianNonce, ownerProcessId);
            var stateBeforeApply = controller.GetActiveScheme();
            if (stateBeforeApply != prepared.OriginalSchemeId)
            {
                var drifted = prepared with
                {
                    State = PowerSessionState.ExternalChange,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    LastObservation = $"Active plan drifted to {stateBeforeApply:D} before apply.",
                    Failure = "Compare-and-set precondition failed; no write was attempted."
                };
                journal.Write(drifted);
                throw new InvalidOperationException("The active power plan changed before apply. FramePath Lab preserved the newer external state.");
            }

            applyAttempted = true;
            controller.SetActiveScheme(target.Id);
            var observed = controller.GetActiveScheme();
            if (observed != target.Id)
            {
                var failed = prepared with
                {
                    State = PowerSessionState.VerificationFailed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    LastObservation = $"Windows reported {observed:D} after apply.",
                    Failure = "The requested plan was not observed after PowerSetActiveScheme."
                };
                journal.Write(failed);
                throw new InvalidOperationException("Windows did not verify the requested High Performance plan; the prior plan was restored where safe.");
            }

            var applied = prepared with
            {
                State = PowerSessionState.AppliedVerified,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastObservation = $"Verified active plan {observed:D}.",
                Failure = null
            };
            journal.Write(applied);
            return new PowerSessionTransition(
                applied,
                observed,
                $"High Performance is verified for this bounded session. Previous plan: {before.Name}.");
        }
        catch
        {
            if (applyAttempted)
            {
                TryRecoverAfterFailedApply(prepared);
            }
            else
            {
                TryFinalizeUnappliedTransaction(prepared);
            }

            throw;
        }
    }

    public PowerSessionTransition Revert(Guid sessionId, string reason)
    {
        using var transactionLock = AcquireTransactionLock();
        return RevertCore(sessionId, NormalizeReason(reason));
    }

    private PowerSessionTransition RevertCore(Guid sessionId, string reason)
    {
        var record = journal.Read()
            ?? throw new InvalidOperationException("No power-plan transaction is available to restore.");
        if (record.SessionId != sessionId)
        {
            throw new InvalidOperationException("The requested rollback does not match the durable transaction journal.");
        }

        if (record.Operation != Operation
            || record.SchemaVersion != SchemaVersion
            || record.TargetSchemeId != HighPerformanceSchemeId
            || record.OriginalSchemeId == Guid.Empty
            || record.OriginalSchemeId == record.TargetSchemeId)
        {
            throw new InvalidDataException("The durable transaction is not a supported power-plan lease; no write was attempted.");
        }

        if (!IsUnresolved(record.State))
        {
            return new PowerSessionTransition(record, controller.GetActiveScheme(), "The recorded transaction is already terminal.");
        }

        var current = controller.GetActiveScheme();
        if (current == record.TargetSchemeId)
        {
            controller.SetActiveScheme(record.OriginalSchemeId);
            var verified = controller.GetActiveScheme();
            if (verified != record.OriginalSchemeId)
            {
                var failed = record with
                {
                    State = PowerSessionState.RecoveryFailed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    LastObservation = $"Rollback requested but Windows reports {verified:D}.",
                    Failure = "Original power plan was not observed after rollback."
                };
                journal.Write(failed);
                throw new InvalidOperationException("Rollback could not be verified. Open Windows Power Options and restore the recorded original plan.");
            }

            return CompleteRevert(record, verified, reason);
        }

        if (current == record.OriginalSchemeId)
        {
            return CompleteRevert(record, current, $"{reason}; the original plan was already active");
        }

        var conflict = record with
        {
            State = PowerSessionState.ExternalChange,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastObservation = $"Rollback found third-party plan {current:D}.",
            Failure = "A newer external power-plan selection was preserved."
        };
        journal.Write(conflict);
        return new PowerSessionTransition(
            conflict,
            current,
            "The plan changed outside FramePath Lab. That newer selection was preserved instead of being overwritten.");
    }

    public PowerSessionTransition? RecoverInterruptedSession(
        int currentProcessId,
        Func<int, long, bool> ownerStillMatches)
    {
        var record = journal.Read();
        if (record is null || !IsUnresolved(record.State))
        {
            return null;
        }

        if (record.OwnerProcessId != currentProcessId
            && ownerStillMatches(record.OwnerProcessId, record.OwnerProcessStartTimeUtcTicks))
        {
            return new PowerSessionTransition(
                record,
                controller.GetActiveScheme(),
                "Another running FramePath Lab instance owns the active power-plan session.");
        }

        return Revert(record.SessionId, "Recovered an interrupted power-plan session on startup");
    }

    public static bool IsUnresolved(PowerSessionState state)
        => state is PowerSessionState.Prepared
            or PowerSessionState.AppliedVerified
            or PowerSessionState.ApplyFailed
            or PowerSessionState.VerificationFailed
            or PowerSessionState.RecoveryFailed;

    private PowerSessionTransition CompleteRevert(PowerSessionRecord record, Guid observed, string reason)
    {
        var reverted = record with
        {
            State = PowerSessionState.RevertedVerified,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastObservation = $"Verified original plan {observed:D}. Reason: {reason}.",
            Failure = null
        };
        journal.Write(reverted);
        return new PowerSessionTransition(
            reverted,
            observed,
            $"Restored and verified {record.OriginalSchemeName}.");
    }

    private void TryFinalizeUnappliedTransaction(PowerSessionRecord record)
    {
        try
        {
            if (controller.GetActiveScheme() == record.OriginalSchemeId)
            {
                CompleteRevert(record, record.OriginalSchemeId, "apply was not attempted");
            }
        }
        catch
        {
            // The durable Prepared record remains for the guardian or next-launch recovery.
        }
    }

    private void TryRecoverAfterFailedApply(PowerSessionRecord record)
    {
        try
        {
            var current = controller.GetActiveScheme();
            if (current == record.TargetSchemeId)
            {
                controller.SetActiveScheme(record.OriginalSchemeId);
                current = controller.GetActiveScheme();
            }

            if (current == record.OriginalSchemeId)
            {
                CompleteRevert(record, current, "apply failed and the exact prior plan was restored");
                return;
            }

            var conflict = record with
            {
                State = PowerSessionState.ExternalChange,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastObservation = $"Failure recovery found third-party plan {current:D}.",
                Failure = "A newer external power-plan selection was preserved."
            };
            journal.Write(conflict);
        }
        catch (Exception exception)
        {
            TryRecordRecoveryFailure(record, exception);
        }
    }

    private void TryRecordRecoveryFailure(PowerSessionRecord record, Exception exception)
    {
        try
        {
            journal.Write(record with
            {
                State = PowerSessionState.RecoveryFailed,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastObservation = "Automatic recovery did not verify the original power plan.",
                Failure = Truncate(exception.Message, 1024)
            });
        }
        catch
        {
            // Preserve the original exception. The guardian retains the prepared state in memory.
        }
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? "rollback requested" : reason.Trim();
        return Truncate(normalized, 512);
    }

    private static string Truncate(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];

    private static TransactionLock AcquireTransactionLock()
    {
        var mutex = new Mutex(false, TransactionMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TransactionLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out waiting for the machine-wide power-plan transaction lock.");
            }

            return new TransactionLock(mutex);
        }
        catch
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            throw;
        }
    }

    private sealed class TransactionLock(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _mutex, null);
            if (owned is null)
            {
                return;
            }

            owned.ReleaseMutex();
            owned.Dispose();
        }
    }
}

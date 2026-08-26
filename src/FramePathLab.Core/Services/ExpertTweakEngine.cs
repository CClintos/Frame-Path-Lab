using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Models;
using FramePathLab.Core.Persistence;

namespace FramePathLab.Core.Services;

/// <summary>
/// Coordinates evaluate, apply and revert for the expert catalogue.
///
/// The ordering rule that makes this safe: a transaction is written to the durable ledger with its
/// captured before-state <em>before</em> the first mutation lands, and updated after. A crash
/// between those two points leaves a recoverable record rather than an orphaned change.
/// </summary>
public sealed class ExpertTweakEngine
{
    private readonly IMutationExecutor _executor;
    private readonly TweakJournalStore _journal;
    private readonly bool _isElevated;

    public ExpertTweakEngine(IMutationExecutor executor, TweakJournalStore journal, bool isElevated)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _isElevated = isElevated;
    }

    public IReadOnlyList<ExpertTweakCard> Evaluate(ExpertScanContext context)
    {
        var cards = ExpertTweakCatalog.Evaluate(context, _executor);
        return cards.Select(GateOnElevation).ToArray();
    }

    /// <summary>
    /// A tweak needing an elevated write from a non-elevated process is reported as blocked rather
    /// than attempted, so a partial apply cannot happen halfway through a multi-value tweak.
    /// </summary>
    private ExpertTweakCard GateOnElevation(ExpertTweakCard card)
    {
        if (_isElevated || card.Plan.Count == 0 || card.BlockedReason is not null)
        {
            return card;
        }

        return card.Plan.Any(_executor.RequiresElevation)
            ? card with { BlockedReason = "Restart FramePath Lab as administrator to apply this change." }
            : card;
    }

    public IReadOnlyList<TweakTransaction> OutstandingTransactions()
        => _journal.Read().Where(transaction => transaction.IsOutstanding).ToArray();

    public IReadOnlyList<TweakTransaction> AllTransactions() => _journal.Read();

    public TweakTransaction Apply(ExpertTweakCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanApply)
        {
            throw new InvalidOperationException(
                card.BlockedReason ?? $"{card.Definition.Title} has no applicable change.");
        }

        var transactionId = Guid.NewGuid();
        var captured = new List<MutationRecord>();

        // Capture every before-state first. If any value cannot be read, nothing is written at all.
        foreach (var plan in card.Plan)
        {
            var before = _executor.Read(plan, out var exists);
            captured.Add(new MutationRecord(
                plan.MutationId,
                plan.Kind,
                plan.Target,
                plan.ValueName,
                plan.ValueType,
                plan.Description,
                exists,
                before,
                plan.DesiredValue,
                null,
                false,
                "Captured; not yet applied.",
                AttemptedWrite: false));
        }

        var pending = new TweakTransaction(
            transactionId,
            card.Definition.Id,
            card.Definition.Title,
            DateTimeOffset.UtcNow,
            null,
            card.Definition.RequiresReboot,
            captured,
            TweakTransaction.StatePartiallyApplied,
            "Prepared. Applying now.");
        _journal.Upsert(pending);

        var applied = new List<MutationRecord>();
        Exception? failure = null;
        foreach (var plan in card.Plan)
        {
            try
            {
                applied.Add(_executor.Apply(plan));
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }
        }

        // Carry forward captures for anything that was never reached, so a partial apply is still
        // fully described in the ledger.
        foreach (var capture in captured.Where(entry => applied.All(record => record.MutationId != entry.MutationId)))
        {
            applied.Add(capture);
        }

        var allVerified = failure is null && applied.All(record =>
            record.VerifiedAfterWrite || !card.Plan.Any(plan => plan.MutationId == record.MutationId));

        var result = pending with
        {
            Mutations = applied,
            State = failure is null && allVerified
                ? TweakTransaction.StateApplied
                : TweakTransaction.StatePartiallyApplied,
            LastObservation = failure is not null
                ? $"Stopped after a failure: {failure.Message}. Revert to restore captured values."
                : allVerified
                    ? "Applied and verified by read-back."
                    : "Applied, but at least one value did not verify on read-back."
        };

        _journal.Upsert(result);

        if (failure is not null)
        {
            // Roll the partial apply back immediately rather than leaving the machine mixed.
            return Revert(result.TransactionId, $"automatic rollback after {failure.Message}");
        }

        return result;
    }

    public TweakTransaction Revert(Guid transactionId, string reason)
    {
        var transaction = _journal.Read().FirstOrDefault(entry => entry.TransactionId == transactionId)
                          ?? throw new InvalidOperationException("No such transaction is recorded.");

        var reverted = new List<MutationRecord>();
        var failures = 0;

        // Reverse order, so a tweak whose values depend on each other unwinds the way it was built.
        foreach (var record in transaction.Mutations.AsEnumerable().Reverse())
        {
            if (!record.AttemptedWrite)
            {
                // Captured but never written, so there is nothing to undo and nothing to fail.
                reverted.Insert(0, record with { Observation = "Skipped: this value was never written." });
                continue;
            }

            try
            {
                var result = _executor.Revert(record);
                reverted.Insert(0, result);
                if (!result.VerifiedAfterWrite
                    && !result.Observation.StartsWith("Left unchanged", StringComparison.Ordinal))
                {
                    failures++;
                }
            }
            catch (Exception exception)
            {
                failures++;
                reverted.Insert(0, record with { Observation = $"Revert failed: {exception.Message}" });
            }
        }

        var result2 = transaction with
        {
            Mutations = reverted,
            RevertedAtUtc = DateTimeOffset.UtcNow,
            State = failures == 0 ? TweakTransaction.StateReverted : TweakTransaction.StateRevertFailed,
            LastObservation = failures == 0
                ? $"Reverted ({reason}). Every value was restored and verified."
                : $"Reverted with {failures} unverified value(s) ({reason}). Review the detail before playing."
        };

        _journal.Upsert(result2);
        return result2;
    }

    /// <summary>Reverts every outstanding transaction, newest first.</summary>
    public IReadOnlyList<TweakTransaction> RevertAll(string reason)
        => OutstandingTransactions()
            .OrderByDescending(transaction => transaction.AppliedAtUtc)
            .Select(transaction => Revert(transaction.TransactionId, reason))
            .ToArray();
}

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
    private readonly object _operationGate = new();

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
        if (card.Plan.Count == 0 || card.BlockedReason is not null)
        {
            return card;
        }

        // The desktop/CLI process reads a user-writable journal. Running the whole process elevated
        // would turn that data into a privileged command channel. Machine writes require a future
        // restricted broker that resolves allowlisted action IDs rather than trusting paths.
        if (_isElevated)
        {
            return card with
            {
                BlockedReason = "Automatic expert writes are disabled while the full application is elevated. "
                                + "Run normally; machine-scope writes require a future restricted broker."
            };
        }

        return card.Plan.Any(_executor.RequiresElevation)
            ? card with
            {
                BlockedReason = "Machine-scope writes are unavailable in this build. Do not restart the full app "
                                + "as administrator; a future restricted broker must own privileged actions."
            }
            : card;
    }

    public IReadOnlyList<TweakTransaction> OutstandingTransactions()
        => _journal.Read().Where(transaction => transaction.IsOutstanding).ToArray();

    public IReadOnlyList<TweakTransaction> AllTransactions() => _journal.Read();

    public TweakTransaction Apply(ExpertTweakCard card)
    {
        lock (_operationGate)
        {
            return ApplyCore(card);
        }
    }

    private TweakTransaction ApplyCore(ExpertTweakCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanApply)
        {
            throw new InvalidOperationException(
                card.BlockedReason ?? $"{card.Definition.Title} has no applicable change.");
        }

        var outstanding = OutstandingTransactions();
        var overlap = outstanding
            .SelectMany(transaction => transaction.Mutations)
            .FirstOrDefault(existing => card.Plan.Any(plan =>
                plan.Kind == existing.Kind
                && TargetsOverlap(plan, existing)
                && string.Equals(plan.ValueName, existing.ValueName, StringComparison.Ordinal)));
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"{overlap.Description} already has an outstanding transaction. Revert it before applying another change.");
        }

        var transactionId = Guid.NewGuid();
        var captured = new List<MutationRecord>(card.Plan.Count);

        // Capture every before-state first. If any value cannot be read, nothing is written at all.
        foreach (var plan in card.Plan)
        {
            captured.Add(_executor.Capture(plan));
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

        var progress = captured.ToList();
        Exception? failure = null;
        for (var index = 0; index < card.Plan.Count; index++)
        {
            var plan = card.Plan[index];
            try
            {
                // Persist write intent before the write. If the process dies at any later boundary,
                // recovery assumes this value may have landed and safely compare-before-reverts it.
                progress[index] = progress[index] with
                {
                    AttemptedWrite = true,
                    Observation = "Write intent recorded; this value may be in progress."
                };
                _journal.Upsert(pending with
                {
                    Mutations = progress.ToArray(),
                    LastObservation = $"Applying {plan.Description}."
                });

                var applied = _executor.Apply(plan, captured[index]);
                progress[index] = applied;
                _journal.Upsert(pending with
                {
                    Mutations = progress.ToArray(),
                    LastObservation = applied.Observation
                });

                if (!applied.VerifiedAfterWrite)
                {
                    throw new InvalidOperationException(applied.Observation);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                break;
            }
        }

        var allVerified = failure is null && progress.All(record => record.VerifiedAfterWrite);

        var result = pending with
        {
            Mutations = progress,
            State = failure is null && allVerified
                ? TweakTransaction.StateApplied
                : TweakTransaction.StatePartiallyApplied,
            LastObservation = failure is not null
                ? $"Stopped after a failure: {failure.Message}. Revert to restore captured values."
                : allVerified
                    ? "Applied and verified by read-back."
                    : "A value did not verify on read-back; automatic rollback is required."
        };

        _journal.Upsert(result);

        if (failure is not null)
        {
            // Roll the partial apply back immediately rather than leaving the machine mixed.
            return RevertCore(result.TransactionId, $"automatic rollback after {failure.Message}");
        }

        return result;
    }

    private static bool TargetsOverlap(MutationPlan plan, MutationRecord existing)
        => string.Equals(plan.Target, existing.Target, StringComparison.OrdinalIgnoreCase)
           || (plan.Kind == MutationKind.PowerSchemeValue
               && existing.Target.EndsWith($"|{plan.Target}", StringComparison.OrdinalIgnoreCase));

    public TweakTransaction Revert(Guid transactionId, string reason)
    {
        lock (_operationGate)
        {
            return RevertCore(transactionId, reason);
        }
    }

    private TweakTransaction RevertCore(Guid transactionId, string reason)
    {
        if (_isElevated)
        {
            throw new InvalidOperationException(
                "Journal-driven expert reverts are disabled while the full application is elevated. "
                + "Run normally; privileged recovery requires a future restricted allowlisted broker.");
        }

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

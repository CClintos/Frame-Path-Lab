using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Analysis;
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
    private readonly IMutationGuard _guard;
    private readonly object _operationGate = new();

    public ExpertTweakEngine(
        IMutationExecutor executor,
        TweakJournalStore journal,
        bool isElevated,
        IMutationGuard? guard = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _isElevated = isElevated;

        // Defaults to the sealed allowlist. Only a test supplies anything else.
        _guard = guard ?? AllowlistMutationGuard.Instance;
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

        // The ledger is user-writable data that a restore replays, so an elevated process must not
        // take its targets on trust. Rather than refusing privileged writes altogether — which
        // would leave half the catalogue permanently unreachable — every target is checked against
        // a compiled-in allowlist here and again immediately before each write and each restore.
        var violation = card.Plan
            .Select(_guard.FindViolation)
            .FirstOrDefault(problem => problem is not null);
        if (violation is not null)
        {
            return card with { BlockedReason = $"Refused by the write allowlist: {violation}" };
        }

        return !_isElevated && card.Plan.Any(_executor.RequiresElevation)
            ? card with
            {
                BlockedReason = "Machine-scope change. Restart FramePath Lab as administrator to apply it; "
                                + "every privileged write is still checked against the allowlist first."
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

    /// <summary>
    /// Applies every card whose disposition is a recommended default and which this machine
    /// actually needs.
    ///
    /// Experiments are deliberately excluded: their value depends on measurement, so applying a
    /// batch of them together would make any subsequent capture impossible to attribute. Each card
    /// still becomes its own transaction, so one can be reverted without disturbing the others.
    /// </summary>
    public IReadOnlyList<TweakTransaction> ApplyRecommendedDefaults(IEnumerable<ExpertTweakCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var applied = new List<TweakTransaction>();
        lock (_operationGate)
        {
            foreach (var card in cards.Where(candidate =>
                         candidate.Definition.Disposition == TweakDisposition.RecommendDefault
                         && candidate.CanApply))
            {
                try
                {
                    applied.Add(ApplyCore(card));
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                      or UnauthorizedAccessException
                                                      or System.Security.SecurityException
                                                      or IOException
                                                      or TimeoutException)
                {
                    // One refused card must not abandon the rest; each is independent. The journal
                    // can also fail on its own account — another instance holding the lock, or a
                    // transient IO error — and that is no reason to abandon the remaining cards
                    // either, which the narrower catch list here used to do.
                    applied.Add(new TweakTransaction(
                        Guid.Empty,
                        card.Definition.Id,
                        card.Definition.Title,
                        DateTimeOffset.UtcNow,
                        null,
                        false,
                        [],
                        TweakTransaction.StateRevertFailed,
                        $"Not applied: {exception.Message}"));
                }
            }
        }

        return applied;
    }

    /// <summary>
    /// Measures one recorded change against a pair of captures and, when the evidence says the
    /// change was not worth keeping, reverses it.
    ///
    /// This closes the loop the rest of the product only half-completes: applying a change with a
    /// rollback record is useful, but deciding whether to keep it from a measurement rather than
    /// from a claim is the part no settings guide can offer.
    /// </summary>
    public (TweakVerification Verification, TweakTransaction? Reverted) Verify(
        Guid transactionId,
        CaptureAnalysis before,
        CaptureAnalysis after,
        bool revertOnFailure)
    {
        lock (_operationGate)
        {
            var transaction = _journal.Read().FirstOrDefault(entry => entry.TransactionId == transactionId)
                              ?? throw new InvalidOperationException("No such transaction is recorded.");

            var verification = TweakVerifier.Compare(before, after, transaction);
            if (!revertOnFailure || !verification.ShouldRevert || !transaction.IsOutstanding)
            {
                return (verification, null);
            }

            var reverted = RevertCore(
                transactionId,
                $"measured verdict: {verification.Verdict}");
            return (verification, reverted);
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

        // Re-check at the point of use, not just at evaluation, so a card that was constructed
        // elsewhere or mutated in between cannot reach a write.
        foreach (var plan in card.Plan)
        {
            var violation = _guard.FindViolation(plan);
            if (violation is not null)
            {
                throw new InvalidOperationException($"Refused by the write allowlist: {violation}");
            }
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

            // The ledger is untrusted input on the way back out. A record naming a location this
            // application may not write is refused rather than replayed, which is what stops an
            // edited ledger from choosing where a privileged restore writes.
            var violation = _guard.FindViolation(record);
            if (violation is not null)
            {
                failures++;
                reverted.Insert(0, record with
                {
                    Observation = $"Refused by the write allowlist: {violation}"
                });
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

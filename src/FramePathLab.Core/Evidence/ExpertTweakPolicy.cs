using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

/// <summary>
/// Decides which candidates the shipping app may actually write.
///
/// The gate is safety and reversibility, not certainty of benefit. Requiring proof that a change
/// helps <em>before</em> allowing the change makes the product unable to produce the evidence that
/// would satisfy it, which collapses into an advice list — and an advice list is the one thing a
/// player can already get for free and cannot trust. The way out is to apply the change against a
/// verified rollback ledger and then measure it, which is what the verification workflow exists
/// for.
///
/// So a candidate may be written when all three hold:
///   1. the surface is documented or exposed in a supported user interface,
///   2. the exact prior value can be captured and restored, and
///   3. it regresses no security guarantee and cannot leave a device unable to start.
///
/// Whether it <em>helps</em> is then a measurement, not a gate. What separates the two write
/// dispositions is confidence, not permission: a default is broadly established, an experiment is
/// workload-dependent and expected to be benchmarked either side.
///
/// Failing any of the three keeps the card visible as a reading, because knowing a value is wrong
/// is useful even when this application is not the right thing to change it.
/// </summary>
public static class ExpertTweakPolicy
{
    public static ExpertTweakCard Apply(ExpertTweakCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var (disposition, reason) = Classify(card.Definition.Id);
        var definition = card.Definition with
        {
            Disposition = disposition,
            DispositionReason = reason
        };

        if (disposition is TweakDisposition.RecommendDefault or TweakDisposition.OptInExperiment)
        {
            return card with { Definition = definition };
        }

        return card with
        {
            Definition = definition,
            Plan = [],
            BlockedReason = reason
        };
    }

    /// <summary>True for the dispositions this application is allowed to write.</summary>
    public static bool IsWritable(TweakDisposition disposition)
        => disposition is TweakDisposition.RecommendDefault or TweakDisposition.OptInExperiment;

    private static (TweakDisposition Disposition, string Reason) Classify(string id)
    {
        // --- Written by default -------------------------------------------------------------
        // Per-user settings with a documented surface, an instant and exact restore, and broad
        // agreement on the correct value for competitive play.
        if (HasPrefix(id, "INPUT-ACCEL-", "INPUT-SPEED-"))
        {
            return (TweakDisposition.RecommendDefault,
                "Applied by default: a documented pointer setting with an exact, instant restore. This is an "
                + "input-consistency change rather than a frame-rate claim — acceleration makes identical hand "
                + "movements produce different view angles, and a non-unity pointer speed scales counts before "
                + "the game receives them.");
        }

        if (HasPrefix(id, "BACKGROUND-DO-", "TELEMETRY-TRACE-", "GAMEDVR-POLICY-"))
        {
            return (TweakDisposition.RecommendDefault,
                "Applied by default: background activity that runs on its own schedule rather than yours, with "
                + "an exact restore and no effect on the game itself.");
        }

        if (HasPrefix(id, "GAMEDVR-", "DX-SWAPCHAIN-", "DX-GPUPREF-", "GAMEMODE-",
                "BACKGROUND-APPS-", "VISUAL-DWM-"))
        {
            return (TweakDisposition.RecommendDefault,
                "Applied by default: a per-user value that Windows exposes in its own settings interface, "
                + "captured exactly and restorable in place.");
        }

        if (HasPrefix(id, "NET-THROTTLE-"))
        {
            return (TweakDisposition.RecommendDefault,
                "Applied by default: a documented mechanism with a defined default and published semantics. "
                + "The cap applies to exactly the traffic an online game depends on, so what removing it does "
                + "is not in question — only how much it is worth on a given connection.");
        }

        // --- Written inside an experiment ---------------------------------------------------
        // Real mechanisms with an exact restore, whose benefit is workload- and hardware-dependent.
        // These are applied so they can be measured, not because they are assumed to help.
        if (HasPrefix(id, "CPU-PARKING-", "CPU-MINSTATE-", "CPU-BOOST-", "POWER-OVERLAY-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: apply on AC power, capture before and after, and keep it only if the frame-time "
                + "tails improve without a thermal or clock regression. On a power-limited part a raised floor "
                + "can cost boost headroom rather than gain it.");
        }

        if (HasPrefix(id, "DX-FSO-", "DX-FSE-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: which presentation path is faster is genuinely engine- and driver-dependent. Apply "
                + "it, then confirm the present mode in a capture rather than assuming.");
        }

        if (HasPrefix(id, "CPU-POWERTHROTTLE-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: this reaches the same outcome as clearing the throttle on the game process without "
                + "opening a handle to the game, which is why it is offered where the per-process route is not. "
                + "Whether Windows was throttling anything that mattered is a measurement.");
        }

        if (HasPrefix(id, "PCIE-ASPM-", "DISK-LPM-", "DISK-NVME-IDLE-", "CPU-LATENCY-HINT-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: an ordinary power-scheme value that Windows hides from its own settings "
                + "interface. Hidden is not undocumented — the power API reads and writes it directly, "
                + "and it reverts through the ledger like anything else. Whether holding a link or a "
                + "drive awake is worth the idle power is a measurement.");
        }

        if (HasPrefix(id, "CPU-IDLE-DISABLE-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment, and the most double-edged one here: cores that never idle give back no "
                + "power budget, so the cores doing the work have less headroom to boost into. On a "
                + "modern part this frequently costs more than it saves. Measure both states.");
        }

        if (HasPrefix(id, "DEVICE-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment, and one the community oversells. Disabling a device does remove its "
                + "interrupt and deferred-call activity, but most idle devices were generating almost "
                + "none to remove. The candidates worth the time are drivers that stay busy with "
                + "nothing attached. The disable does not persist across a restart, so a wrong call "
                + "costs a reboot — which makes this cheap to test and not worth guessing at.");
        }

        if (HasPrefix(id, "SERVICE-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: the start type is captured exactly and restored in place, and nothing live "
                + "depends on the service or it would not be offered. What it is worth is a different "
                + "question — most services cost background wakeups rather than frame time, and several "
                + "will measure as doing nothing. Read what the card says you lose, then measure it.");
        }

        if (HasPrefix(id, "MEMORY-KERNEL-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: holding kernel code resident removes a fault that can land mid-frame, but only on "
                + "a machine with memory to spare. Requires a restart.");
        }

        if (HasPrefix(id, "BOOT-FASTSTART-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: a documented power policy with an exact restore and no security surface. It ends "
                + "the kernel session at shutdown, which is what makes a tuned machine behave the same way every "
                + "boot; the cost is a slightly longer cold start.");
        }

        if (HasPrefix(id, "TIMER-GLOBAL-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: current Windows scopes timer-resolution requests to the requesting process, so a "
                + "game asking for a finer tick no longer necessarily gets one. This policy value restores the "
                + "earlier system-wide behaviour. The mechanism is well established; the size of the effect on "
                + "any particular machine is not, so measure it. Requires a restart.");
        }

        if (HasPrefix(id, "MMCSS-001"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: the reservation is a documented value with defined semantics, but it only bites "
                + "when the system is actually contended. Measure it under the contention you actually play in.");
        }

        if (HasPrefix(id, "NET-MODERATION-", "NET-EEE-", "NET-RSC-"))
        {
            return (TweakDisposition.OptInExperiment,
                "Experiment: these are the adapter's own documented properties, the same ones its driver exposes "
                + "in Device Manager, and they are restored exactly. Both trade a little power and CPU for "
                + "delivery latency. Applying resets the adapter, which briefly drops the link.");
        }

        // --- Not written here, but pointed at ------------------------------------------------
        if (HasPrefix(id, "GPU-HAGS-"))
        {
            return (TweakDisposition.GuidedAction,
                "Guided action: change this in the Windows graphics settings and restart. It is a driver-level "
                + "scheduling change whose effective state this build cannot verify afterwards, so the supported "
                + "interface stays the authority.");
        }

        if (HasPrefix(id, "SECURITY-HVCI-"))
        {
            return (TweakDisposition.GuidedAction,
                "Guided action: memory integrity measurably costs frame rate in CPU-bound scenes, and turning it "
                + "off measurably reduces kernel driver verification. That trade belongs to the account holder "
                + "in the Windows Security interface, not to a tweak this application writes on their behalf.");
        }

        if (HasPrefix(id, "SECURITY-SPECULATIVE-"))
        {
            return (TweakDisposition.DiagnosticOnly,
                "Reported only: overriding the processor's speculative-execution mitigations re-exposes the "
                + "vulnerability class they exist to close. The reading is shown because the trade is real in "
                + "both directions; the change is not one this application makes.");
        }

        if (HasPrefix(id, "CPU-RESERVED-"))
        {
            return (TweakDisposition.DiagnosticOnly,
                "Reported only: a reserved processor set is kernel scheduling policy. A wrong mask is only "
                + "recoverable by editing it back before the next boot completes, which is not a state to "
                + "reach by pressing a button. The recommended mask is shown so it can be set deliberately.");
        }

        if (HasPrefix(id, "INPUT-IMOD-", "CPU-STABILITY-"))
        {
            return (TweakDisposition.DiagnosticOnly,
                "Reported only: the underlying control lives in device registers or is a measurement rather "
                + "than a setting.");
        }

        if (HasPrefix(id, "NET-MSI-"))
        {
            return (TweakDisposition.DiagnosticOnly,
                "Reported only: an incorrect adapter interrupt configuration can leave the adapter unable to "
                + "start, which is worse than the delay it was meant to remove.");
        }

        // --- Never written --------------------------------------------------------------------
        if (HasPrefix(id, "CPU-PLACEMENT-", "CPU-ECOQOS-"))
        {
            return (TweakDisposition.Excluded,
                "Excluded: this would require opening a handle to the running game with rights to change its "
                + "execution. An anti-cheat cannot distinguish that from hostile behaviour, and the placement is "
                + "reachable at launch instead. The card shows the mask and the launch command.");
        }

        if (HasPrefix(id, "MMCSS-GAMES-"))
        {
            return (TweakDisposition.Excluded,
                "Excluded: Microsoft documents GPU Priority and SFIO Priority as unused, and forces Priority to "
                + "2 for the High scheduling category. Writing these values changes nothing.");
        }

        if (HasPrefix(id, "SCHED-QUANTUM-"))
        {
            return (TweakDisposition.Excluded,
                "Excluded: a system-wide scheduler change whose preset values are folklore. The mechanism is "
                + "real but the published presets are not derived from measurement.");
        }

        if (HasPrefix(id, "GPU-MSI-"))
        {
            return (TweakDisposition.Excluded,
                "Excluded: an unsupported display-driver interrupt edit that can leave the adapter unable to "
                + "start, which is not recoverable from inside Windows.");
        }

        // Everything else is a measurement rather than a candidate change.
        return (TweakDisposition.DiagnosticOnly,
            "Diagnostic: a reading that informs what to change, not a change this application makes.");
    }

    private static bool HasPrefix(string id, params string[] prefixes)
        => prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

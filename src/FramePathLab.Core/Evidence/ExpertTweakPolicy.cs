using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

/// <summary>
/// Independent product-policy gate for the deep scanner. The catalogue may retain a candidate so
/// researchers can see and study it, but this gate decides whether the shipping app may mutate it.
/// Obscurity is not evidence: unsupported registry values and game-process changes stay visible as
/// rejected hypotheses rather than becoming one-click tweaks.
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

    private static (TweakDisposition Disposition, string Reason) Classify(string id)
    {
        if (HasPrefix(id,
                "CPU-PLACEMENT-", "CPU-ECOQOS-", "TIMER-GLOBAL-", "MMCSS-", "MMCSS-GAMES-",
                "SCHED-QUANTUM-", "SECURITY-HVCI-", "GPU-MSI-"))
        {
            return (TweakDisposition.Excluded, id switch
            {
                "SECURITY-HVCI-001" =>
                    "Excluded: disabling Memory Integrity is a security regression and is outside the product boundary.",
                "CPU-PLACEMENT-001" or "CPU-ECOQOS-001" =>
                    "Excluded: FramePath Lab does not alter the running game's affinity, priority or power-throttling state.",
                "TIMER-GLOBAL-001" =>
                    "Excluded: this undocumented registry policy is not supported by the cited timer documentation and has no decision-grade CS2 benefit.",
                "MMCSS-GAMES-001" =>
                    "Excluded: Microsoft documents GPU Priority and SFIO Priority as unused and forces Priority=2 for the High category.",
                "MMCSS-001" =>
                    "Excluded: documented value semantics do not establish that changing the system-wide reservation improves CS2.",
                "SCHED-QUANTUM-001" =>
                    "Excluded: Win32PrioritySeparation presets are scheduler folklore without decision-grade CS2 evidence.",
                "GPU-MSI-001" =>
                    "Excluded: direct display-driver interrupt registry edits are unsupported and can prevent a device from starting.",
                _ => "Excluded by the safety and evidence policy."
            });
        }

        if (HasPrefix(id,
                "GPU-HAGS-", "DX-SWAPCHAIN-", "DX-GPUPREF-", "DX-FSO-", "GAMEDVR-", "GAMEMODE-"))
        {
            return (TweakDisposition.GuidedAction,
                "Guided action: change this through the supported Windows Settings or vendor UI, then rescan and benchmark.");
        }

        if (HasPrefix(id,
                "CPU-CEILING-", "TIMING-JITTER-", "GPU-PCIE-", "GPU-LIMITER-", "DISPLAY-HDR-",
                "DISPLAY-CAP-", "INPUT-ACCEL-", "INPUT-SPEED-", "INPUT-POLL-", "MEMORY-PROFILE-",
                "MEMORY-CHANNELS-", "CPU-STACKED-CACHE-", "GPU-REBAR-", "TIMER-PLATFORM-",
                "STEAM-TRANSFER-", "NET-"))
        {
            return (TweakDisposition.DiagnosticOnly,
                "Diagnostic only: the reading is useful context, but this build cannot prove or safely automate a performance improvement.");
        }

        if (HasPrefix(id, "CPU-PARKING-", "CPU-MINSTATE-", "CPU-BOOST-", "POWER-OVERLAY-"))
        {
            return (TweakDisposition.OptInExperiment,
                "A/B experiment only: apply temporarily on AC power, measure repeated runs, and keep it only if frame-time tails improve without thermal or clock regression.");
        }

        return (TweakDisposition.DiagnosticOnly,
            "Unclassified candidates fail closed as diagnostic-only until an explicit evidence review promotes them.");
    }

    private static bool HasPrefix(string id, params string[] prefixes)
        => prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

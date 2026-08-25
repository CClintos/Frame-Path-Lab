using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Core.Evidence;

public sealed class DefaultEvidenceCatalog : IEvidenceCatalog
{
    private static readonly EvidenceSource MicrosoftDisplay = new(
        "Microsoft advanced display settings",
        new Uri("https://support.microsoft.com/en-us/windows/change-your-display-refresh-rate-in-windows-c8ea729e-0678-015c-c415-f806f04aae5a"));

    private static readonly EvidenceSource NvidiaReflex = new(
        "NVIDIA Reflex in Counter-Strike 2",
        new Uri("https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/"));

    private static readonly EvidenceSource AmdAntiLag = new(
        "AMD Radeon Anti-Lag",
        new Uri("https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html"));

    private static readonly EvidenceSource MicrosoftHags = new(
        "Microsoft hardware-accelerated GPU scheduling",
        new Uri("https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/"));

    private static readonly EvidenceSource MicrosoftTimers = new(
        "Microsoft BCDEdit set options",
        new Uri("https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set"));

    private static readonly EvidenceSource ValveTrustedMode = new(
        "Valve CS2 Trusted Mode",
        new Uri("https://help.steampowered.com/en/faqs/view/09A0-4879-4353-EF95"));

    public IReadOnlyList<FindingCard> Evaluate(EnvironmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var findings = new List<FindingCard>
        {
            BuildSafetyBoundary(),
            BuildRefreshFinding(snapshot),
            BuildPlatformFinding(snapshot),
            BuildGameFinding(snapshot),
            BuildPowerFinding(snapshot),
            BuildOverlayFinding(snapshot),
            BuildHagsFinding(),
            BuildExcludedTweaksFinding()
        };

        var adapterText = string.Join(' ', snapshot.Displays.Select(display => display.AdapterDescription));
        if (adapterText.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(BuildReflexFinding());
        }

        if (adapterText.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || adapterText.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(BuildAntiLagFinding());
        }

        if (adapterText.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(BuildIntelFinding());
        }

        return findings
            .OrderBy(finding => DispositionOrder(finding.Disposition))
            .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FindingCard BuildSafetyBoundary()
        => new(
            "SAFE-001",
            "Game-integrity boundary",
            "FramePath Lab does not inject, hook, inspect game memory, automate input, manipulate packets, or write driver, display, Steam, or CS2 settings. Its only automatic system action is a separately approved, bounded power-plan session with verified compare-before-write recovery.",
            "Constrained application architecture",
            ObservationProvenance.ProcessObservation,
            EvidenceQuality.Strong,
            FindingKind.Exclusion,
            FindingDisposition.NoAction,
            CapabilityState.Supported,
            false,
            ["Keep Trusted Mode enabled", "Use local/imported measurement workflows"],
            ["No software can guarantee future anti-cheat compatibility"],
            [ValveTrustedMode]);

    private static FindingCard BuildRefreshFinding(EnvironmentSnapshot snapshot)
    {
        var primary = snapshot.Displays.FirstOrDefault(display => display.IsPrimary)
            ?? snapshot.Displays.FirstOrDefault();
        if (primary is null)
        {
            return new FindingCard(
                "DISPLAY-001",
                "Active refresh rate",
                "Windows did not return a usable attached-display mode.",
                "Unknown",
                ObservationProvenance.Unknown,
                EvidenceQuality.Strong,
                FindingKind.DiagnosticHypothesis,
                FindingDisposition.Unsupported,
                CapabilityState.Unknown,
                false,
                ["A local physical display must be resolved"],
                ["Do not infer refresh rate from monitor marketing specifications"],
                [MicrosoftDisplay]);
        }

        var lowerThanAvailable = primary.MaximumRefreshAtCurrentResolutionHz > primary.CurrentRefreshHz + 0.5;
        var summary = lowerThanAvailable
            ? "Windows reports a higher enumerated refresh rate at the current resolution. This is a guided configuration check, not proof that changing it improves this PC without trade-offs."
            : "The primary display is already using the highest Windows-enumerated refresh rate at the current resolution.";
        return new FindingCard(
            "DISPLAY-001",
            "Active refresh rate",
            summary,
            $"{primary.Width}×{primary.Height} at {primary.CurrentRefreshHz:0.###} Hz; maximum enumerated at this resolution {primary.MaximumRefreshAtCurrentResolutionHz:0.###} Hz",
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Strong,
            lowerThanAvailable ? FindingKind.GuidedCorrection : FindingKind.ConfigurationMismatch,
            lowerThanAvailable ? FindingDisposition.GuidedExperiment : FindingDisposition.NoAction,
            CapabilityState.Supported,
            false,
            ["Same resolution and verified quality fields", "User confirms the display remains stable"],
            ["HDR, bit depth, chroma, VRR range, scaling, link stability, or audio routing can change"],
            [MicrosoftDisplay]);
    }

    private static FindingCard BuildPlatformFinding(EnvironmentSnapshot snapshot)
    {
        var limitations = snapshot.CapabilityLimitations.Count == 0
            ? "No current platform-level blockers were detected by the prototype scanner."
            : string.Join("; ", snapshot.CapabilityLimitations);
        return new FindingCard(
            "PLATFORM-001",
            "Decision-grade platform readiness",
            limitations,
            snapshot.DecisionGradeCapability.ToString(),
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Moderate,
            FindingKind.DiagnosticHypothesis,
            snapshot.DecisionGradeCapability == CapabilityState.Supported
                ? FindingDisposition.Measure
                : FindingDisposition.ExplainOnly,
            snapshot.DecisionGradeCapability,
            false,
            ["Local physical session", "Resolved single-display path", "Qualified collector and scenario still required"],
            ["A clean scan is not an optimized score or causal benchmark"],
            []);
    }

    private static FindingCard BuildGameFinding(EnvironmentSnapshot snapshot)
    {
        var game = snapshot.SteamGame;
        var state = game.Cs2Installed
            ? $"Installed; build {game.BuildId}; running: {(game.Cs2Running ? "yes" : "no")}"
            : game.SteamDetected ? "Steam detected; CS2 app manifest not found" : "Steam location not resolved";
        return new FindingCard(
            "CS2-001",
            "CS2 installation identity",
            "A build change invalidates scenario and evidence compatibility until it is requalified.",
            state,
            game.Cs2Installed ? ObservationProvenance.SteamManifest : ObservationProvenance.Unknown,
            EvidenceQuality.Strong,
            FindingKind.ConfigurationMismatch,
            game.Cs2Installed ? FindingDisposition.NoAction : FindingDisposition.Unsupported,
            game.Cs2Installed ? CapabilityState.Supported : CapabilityState.Unknown,
            false,
            ["Supported Steam installation", "Qualified build and scenario revision"],
            ["The prototype does not read or modify CS2 configuration files"],
            []);
    }

    private static FindingCard BuildPowerFinding(EnvironmentSnapshot snapshot)
        => new(
            "POWER-001",
            "Power and battery context",
            snapshot.Power.IsOnAc
                ? "AC power is available. Power-policy experiments still require qualified clock, temperature and power guardrails."
                : "Battery operation is diagnostic-only in the initial support model.",
            snapshot.Power.Status,
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Moderate,
            FindingKind.DiagnosticHypothesis,
            snapshot.Power.IsOnAc ? FindingDisposition.Measure : FindingDisposition.ExplainOnly,
            snapshot.Power.IsOnAc ? CapabilityState.Provisional : CapabilityState.Unsupported,
            false,
            ["AC power", "Qualified thermal and power sensors"],
            ["Higher-performance policies can increase heat, noise, battery use, or throttling"],
            []);

    private static FindingCard BuildOverlayFinding(EnvironmentSnapshot snapshot)
    {
        var observed = snapshot.ObservedOptionalApplications.Count == 0
            ? "No selected optional overlay/recording application was observed"
            : string.Join(", ", snapshot.ObservedOptionalApplications);
        return new FindingCard(
            "BACKGROUND-001",
            "Optional overlay or recording activity",
            "Process presence alone is not evidence of a performance problem. Isolation is worth testing only when time-correlated contention is observed.",
            observed,
            ObservationProvenance.ProcessObservation,
            EvidenceQuality.Moderate,
            FindingKind.DiagnosticHypothesis,
            snapshot.ObservedOptionalApplications.Count == 0
                ? FindingDisposition.NoAction
                : FindingDisposition.Measure,
            CapabilityState.Provisional,
            false,
            ["One user-selected nonessential application at a time", "Reopen/restoration instructions"],
            ["Never blanket-disable services or security software"],
            []);
    }

    private static FindingCard BuildHagsFinding()
        => new(
            "WINDOWS-HAGS-001",
            "Hardware-accelerated GPU scheduling",
            "Treat HAGS as a reboot-blocked opt-in experiment. The prototype does not infer or change the setting.",
            "Not detected by a validated API in this build",
            ObservationProvenance.Unknown,
            EvidenceQuality.Moderate,
            FindingKind.CausalExperiment,
            FindingDisposition.ExplainOnly,
            CapabilityState.Unknown,
            false,
            ["Supported OS/GPU/driver", "Balanced reboot blocks", "Qualified metrics under HAGS"],
            ["Can help, do nothing, or regress depending on hardware and driver"],
            [MicrosoftHags]);

    private static FindingCard BuildExcludedTweaksFinding()
        => new(
            "EXCLUDE-001",
            "Timer, debloat and security tweak packs",
            "HPET/BCD timer forcing, blanket service removal, security disabling, priority/affinity folklore and undocumented network/registry packs are excluded.",
            "Excluded by product policy",
            ObservationProvenance.Unknown,
            EvidenceQuality.Disproven,
            FindingKind.Exclusion,
            FindingDisposition.Excluded,
            CapabilityState.Unsupported,
            false,
            ["Use documented settings and measured reversible experiments"],
            ["Instability, security regression, broken updates, misleading attribution"],
            [MicrosoftTimers]);

    private static FindingCard BuildReflexFinding()
        => new(
            "NVIDIA-REFLEX-001",
            "NVIDIA Reflex",
            "Reflex is a supported in-game latency control on compatible NVIDIA systems. This build can explain a guided experiment but cannot verify configured state or engagement, so it cannot issue a causal Keep.",
            "NVIDIA display adapter observed; Reflex state unknown",
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Strong,
            FindingKind.CausalExperiment,
            FindingDisposition.GuidedExperiment,
            CapabilityState.Provisional,
            false,
            ["Compatible CS2/NVIDIA path", "Verified state/engagement", "CPU- and GPU-bound scenarios"],
            ["Boost can increase power and temperature; missing markers remain missing"],
            [NvidiaReflex]);

    private static FindingCard BuildAntiLagFinding()
        => new(
            "AMD-ANTILAG2-001",
            "AMD integrated Anti-Lag 2",
            "Integrated Anti-Lag 2 must not be conflated with driver Anti-Lag controls. This build cannot verify its state or engagement and therefore limits the card to guided observation.",
            "AMD/Radeon display adapter observed; Anti-Lag 2 state unknown",
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Strong,
            FindingKind.CausalExperiment,
            FindingDisposition.GuidedExperiment,
            CapabilityState.Provisional,
            false,
            ["Compatible game/GPU/driver", "Verified integrated feature state"],
            ["Driver and integrated controls are not interchangeable"],
            [AmdAntiLag]);

    private static FindingCard BuildIntelFinding()
        => new(
            "INTEL-001",
            "Intel graphics path",
            "Intel systems receive read-only frame and presentation diagnostics. The app does not infer a vendor latency feature from adapter identity.",
            "Intel display adapter observed",
            ObservationProvenance.WindowsApi,
            EvidenceQuality.Moderate,
            FindingKind.DiagnosticHypothesis,
            FindingDisposition.Measure,
            CapabilityState.Provisional,
            false,
            ["Resolved render/display route", "Qualified collector metrics"],
            ["Hybrid routes require separate qualification"],
            []);

    private static int DispositionOrder(FindingDisposition disposition)
        => disposition switch
        {
            FindingDisposition.GuidedExperiment => 0,
            FindingDisposition.Measure => 1,
            FindingDisposition.ExplainOnly => 2,
            FindingDisposition.NoAction => 3,
            FindingDisposition.Unsupported => 4,
            FindingDisposition.Excluded => 5,
            _ => 6
        };
}

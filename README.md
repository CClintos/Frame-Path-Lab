# FramePath Lab

FramePath Lab is a Windows pre-game readiness application for inspecting a competitive-FPS system, showing which supported checks are ready, available, unknown, or unavailable, analyzing local frame-delivery captures, and running one explicit, bounded power-plan experiment with rollback safeguards. It avoids registry-tweak bundles, debloat scripts, game-process interaction, packet tools, input automation, and unsupported system changes.

## Current build

Implemented:

- WPF desktop application targeting .NET 10 on Windows x64.
- Scan-first pre-game dashboard with separate Ready / already set, Change available, Check manually, and Unavailable counts.
- Plain-language current state, recommended next step, evidence, risks, and a directly connected action for every displayed check.
- Supported deep links to Windows Advanced display, Game Mode, and default graphics settings; manual changes are followed by an explicit rescan prompt.
- 59/60 Hz TV-compatible reporting is treated as a manual verification case, never as an automatic optimization opportunity.
- Read-only Windows display, refresh, power, Steam/CS2-build and selected optional-application scan.
- A 15-minute, opt-in switch to an already-installed Windows High performance power plan using documented PowrProf APIs.
- Durable before-state journal, exact read-back verification, monitored independent rollback guardian, compare-before-write restoration, and next-launch recovery.
- Guided buttons for supported Windows settings and an in-game CS2/Reflex checklist; those settings remain user-controlled.
- Evidence cards that separate facts, hypotheses, guided experiments and exclusions.
- Bounded PresentMon-style CSV parser with SHA-256 provenance.
- Frame-time mean, median, P95, P99, conditional P99.9, standard deviation, mean-FPS equivalent, explicit frame-budget share, CPU/GPU busy medians and present-mode counts.
- Target-process protection: CS2 rows are selected explicitly; ambiguous multi-process imports fail closed.
- Local derived-history store with atomic replacement and no raw-capture copy.
- Collision-safe Markdown report export.
- Headless research CLI and dependency-free automated test runner.

## Expert tier

A second, deeper layer for systems that already have the obvious settings right. It keeps detection separate from product policy: each item is labelled **default recommendation**, **A/B experiment**, **guided action**, **diagnostic only**, or **excluded**. Only supported, bounded power-policy candidates can retain an Apply plan.

What it measures that the base scan cannot:

- **CPU scheduling topology** — physical/logical cores, SMT, efficiency classes, and last-level-cache groups. Detects an asymmetric-cache die (the vertical-cache CCD) and a hybrid performance-core set, then names the affinity mask a latency-sensitive game belongs on. Cores outside any L3 domain — the low-power island on recent hybrid parts — are grouped separately instead of being dropped.
- **Exact display timing** via `QueryDisplayConfig` — the true rational refresh rate, not a truncated integer, so 59.94-class timings need no guessing. The frame cap is computed from it.
- **GPU telemetry** through the driver's own NVML — active clock limiter, PCIe link width and generation against maximum, performance state. Loaded only from the protected Windows system directory; no bundled binary, and absence degrades to "unavailable".
- **Hardware-accelerated GPU scheduling** remains a guided Windows Settings check. The reserved `D3DKMT_WDDM_2_7_CAPS` structure and a registry request are not treated as a supported effective-state API.
- **Mouse message-arrival sampling** — a descriptive raw-input message-pump observation. It is not presented as the device's configured polling rate, a missed-report count, or a decision-grade 2-8 kHz measurement.
- **Thread wake-up sampling** — a coarse managed `Thread.Sleep` observation. It is not labelled DPC/ISR latency; attribution requires WPR/ETW.
- **Presentation path from the capture itself** — independent flip versus composed, vertical sync read from the sync interval rather than from game configuration, CPU- versus GPU-bound classification, frame-pacing cadence, and dropped presents.
- **Memory configuration** parsed from SMBIOS — per-slot size and firmware-reported configured/maximum speed, with channel layout only when locator strings can be parsed. This is a consistency check, not proof of XMP/EXPO state or stability.
- **Stacked-cache CPU profile** — a part carrying vertically stacked cache is power- and thermally-limited by design, so the catalogue changes its own advice there: it offers no affinity change (one cache domain leaves nothing to choose between) and demotes the processor performance floor from a recommendation to an explicit A/B, because a raised floor competes with the boost headroom the active cores need.
- **Resizable BAR / BAR1 context** without claiming that aperture size proves the driver's per-game Resizable BAR profile is active.
- **Performance-counter frequency** retained as provenance without claiming that QPC frequency proves a `useplatformclock` boot setting.
- **Display adapter interrupt mode**, and **Steam transfer in progress**, which is among the most common causes of stutter in an otherwise clean session and is invisible to any settings audit.

Roughly thirty research candidates span CPU/power policy, timing, GPU/presentation, display, input, background activity, and network context. Unsupported registry writes, security reductions, game-process manipulation, raw NIC/driver changes, MMCSS folklore, and timer myths remain visible as **Excluded** with no Apply plan. The retained power-policy experiments show their literal supported writes before approval.

Headless equivalents:

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert --measure-input
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-apply POWER-OVERLAY-001
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-revert all
```

Deliberately deferred:

- Direct PresentMon execution until an exact component/hash/argument/lost-event/licence contract passes.
- Causal A/A or A/B decisions until scenario and statistical calibration pass.
- Reflex/Anti-Lag/config state verification.
- Automatic display, NVIDIA driver, Steam and CS2 setting changes.
- Cloud analytics, accounts and background updates.

## Safety boundary

The expert tier fails closed through an evidence policy. Most cards are read-only or guided. A small set of supported power-policy candidates can be offered only as temporary A/B experiments; every retained write is explicit, individually approved, and reversible.

The contract for all of them is identical:

1. The exact prior value is read and recorded in a durable, integrity-checked ledger **before** anything is written. If a before-state cannot be read, nothing is written at all.
2. A durable **write intent** is flushed immediately before each atomic write, and the verified result is flushed immediately after. A crash at either boundary therefore leaves a conservative recovery record.
3. The write happens from the recorded capture with compare-before-write drift protection. A write that does not verify triggers automatic rollback and is never called success.
4. A tweak with several values applies them as a unit. If one fails, the values that may have landed are rolled back automatically.
5. Reverting compares before writing. If something else changed the value after FramePath Lab did, that newer state is preserved rather than overwritten.

The ledger lives in `%LOCALAPPDATA%\FramePathLab` and supports recovery after a crash, reboot, or reinstall. It is a corruption check, not a security boundary against another process running as the same user. The full app must remain unelevated; a future privileged broker must resolve allowlisted action IDs rather than trust journal-supplied targets.

The full desktop/CLI process must not be used as an elevated mutation broker. Automatic expert writes are disabled while it is elevated, and machine-scope expert writes remain blocked until a restricted allowlisted broker exists.

The application still does not:

- inject or load code into CS2;
- read or write game memory;
- intercept input or automate gameplay;
- inspect or manipulate packets;
- write driver, Steam or CS2 configuration files;
- install a driver, service, overlay or background updater;
- change firmware, power limits or boot configuration.

**CPU affinity/EcoQoS**, **Memory Integrity disable**, **global timer policy**, **MMCSS task edits**, **Win32PrioritySeparation**, **GPU MSI registry edits**, and raw NIC/driver registry changes are explicitly excluded and have no executable plan.

Imported captures are observational. A single capture cannot produce a causal Keep/Revert decision, and software timing fields are not presented as physical mouse-to-photon latency. Every tweak in the catalogue is an experiment with an uncertain benefit on any particular machine, not a guarantee.

Normal app exit, explicit restore, AC loss, or the 15-minute limit triggers a restoration attempt followed by read-back verification. If another program selected a third plan, FramePath Lab preserves that newer state; if restoration fails, it surfaces the failure. The guardian handles a UI crash. Because the unsigned portable build installs no service or startup task, an OS crash/reboot can leave High performance active until FramePath Lab is opened again; startup recovery then compares the actual plan before writing.

Normal first-run startup is observational. If the durable journal records an earlier approved but unfinished session, startup recovery may restore the exact recorded prior plan before scanning; it never substitutes an assumed default.

## Build

The workspace-local .NET 10 SDK is used automatically when present:

```powershell
.\build.ps1
```

Run the desktop app:

```powershell
.\work\dotnet\dotnet.exe run --project .\src\FramePathLab.App\FramePathLab.App.csproj --configuration Release
```

Run the CLI:

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- scan
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- analyze .\samples\presentmon-sample.csv 4.1667
```

## Local data

Derived history and the separate power-session recovery journal are stored under `%LOCALAPPDATA%\FramePathLab`. Raw imported captures are not copied. Deleting derived history does not delete the recovery journal.

## Repository layout

- `src/FramePathLab.Core` — models, evidence, analysis, reporting and persistence.
- `src/FramePathLab.Windows` — Windows scanner, documented native API interop, transaction journal and rollback guardian.
- `src/FramePathLab.App` — WPF desktop UI.
- `tools/FramePathLab.Cli` — headless research CLI.
- `tests/FramePathLab.Tests` — dependency-free executable test suite.
- `samples` — synthetic import fixture; not a performance reference.
- `outputs` — research scope and build roadmap.

The synthetic sample exists only to verify the import path. It is not CS2 performance evidence.

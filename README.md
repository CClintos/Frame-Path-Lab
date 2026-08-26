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

A second, deeper layer for systems that already have the obvious settings right. It reads state nothing in the base scan could see, and it applies changes with a full rollback ledger.

What it measures that the base scan cannot:

- **CPU scheduling topology** — physical/logical cores, SMT, efficiency classes, and last-level-cache groups. Detects an asymmetric-cache die (the vertical-cache CCD) and a hybrid performance-core set, then names the affinity mask a latency-sensitive game belongs on. Cores outside any L3 domain — the low-power island on recent hybrid parts — are grouped separately instead of being dropped.
- **Exact display timing** via `QueryDisplayConfig` — the true rational refresh rate, not a truncated integer, so 59.94-class timings need no guessing. The frame cap is computed from it.
- **GPU telemetry** through the driver's own NVML — active clock limiter, PCIe link width and generation against maximum, performance state. Loaded by name at runtime; no bundled binary, and absence degrades to "unavailable".
- **Hardware-accelerated GPU scheduling** read from the documented `D3DKMT_WDDM_2_7_CAPS` capability query rather than inferred from a registry value.
- **Mouse report delivery** — measured sustained report rate, interval scatter, and missed reports, timed from raw input. Catches a device set to a high rate that does not sustain it, which frame capture cannot see because it happens before the engine samples input.
- **Thread wake-up punctuality** — lateness measured against the timer tick, which isolates scheduling delay from timer granularity. The unprivileged stand-in for a kernel latency trace.
- **Presentation path from the capture itself** — independent flip versus composed, vertical sync read from the sync interval rather than from game configuration, CPU- versus GPU-bound classification, frame-pacing cadence, and dropped presents.

Roughly thirty checks across CPU placement and power policy, timing and scheduling, GPU and presentation, display, input, background services, and network adapter latency settings. Each states the mechanism it acts on, why it helps, its trade-off, and the literal writes it will make — shown before you commit.

Headless equivalents:

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert --measure-input
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-apply INPUT-ACCEL-001
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-revert all
```

Deliberately deferred:

- Direct PresentMon execution until an exact component/hash/argument/lost-event/licence contract passes.
- Causal A/A or A/B decisions until scenario and statistical calibration pass.
- Reflex/Anti-Lag/config state verification.
- Automatic display, NVIDIA driver, Steam and CS2 setting changes.
- Cloud analytics, accounts and background updates.

## Safety boundary

The expert tier writes system state. Every write is explicit, individually approved, and reversible.

The contract for all of them is identical:

1. The exact prior value is read and recorded in a durable, integrity-checked ledger **before** anything is written. If a before-state cannot be read, nothing is written at all.
2. The write happens, then the value is read back. A write that does not verify is reported as unverified, never as success.
3. A tweak with several values applies them as a unit. If one fails, the ones that already landed are rolled back automatically.
4. Reverting compares before writing. If something else changed the value after FramePath Lab did, that newer state is preserved rather than overwritten.

The ledger lives in `%LOCALAPPDATA%\FramePathLab` and survives a crash, a reboot, and a reinstall of the app, so anything applied can always be undone — from the Expert tab, or with `expert-revert all` from the CLI.

Machine-scope writes need administrator rights. Running unelevated does not attempt them and does not partially apply them; the affected items report themselves as blocked.

The application still does not:

- inject or load code into CS2;
- read or write game memory;
- intercept input or automate gameplay;
- inspect or manipulate packets;
- write driver, Steam or CS2 configuration files;
- install a driver, service, overlay or background updater;
- change firmware, power limits or boot configuration.

Two entries deserve to be called out by name. **CPU thread placement** sets the affinity of the running game process; it is reversible instantly and is lost when the game restarts. **Memory integrity** is the one item in the catalogue that trades a real kernel security guarantee for frame rate — it is detected and surfaced with its cost stated in both directions, and it is never recommended, only offered.

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

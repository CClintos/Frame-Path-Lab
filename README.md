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

Deliberately deferred:

- Direct PresentMon execution until an exact component/hash/argument/lost-event/licence contract passes.
- Causal A/A or A/B decisions until scenario and statistical calibration pass.
- Reflex/Anti-Lag/config state verification.
- Automatic display, NVIDIA driver, Steam and CS2 setting changes.
- Cloud analytics, accounts and background updates.

## Safety boundary

The sole automatic system mutation is a temporary switch of the current user's active power-plan pointer to the standard High performance GUID, and only when Windows already enumerates that plan. The application does not create, edit, unhide, or delete power plans and remains `asInvoker`.

The application otherwise does not:

- inject or load code into CS2;
- read or write game memory;
- intercept input or automate gameplay;
- inspect or manipulate packets;
- change process priority or affinity;
- write registry, driver, display, Steam or CS2 settings;
- disable security features or services;
- install a driver, service, overlay or background updater.

Imported captures are observational. A single capture cannot produce a causal Keep/Revert decision, and software timing fields are not presented as physical mouse-to-photon latency. The power-plan change is an opt-in benchmark experiment with uncertain benefit, not a recommendation or guarantee.

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

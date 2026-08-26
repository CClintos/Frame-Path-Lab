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
- **Audio-path context** — shared-mode format, endpoint-effects flags, channel count, and possible third-party audio services. These are diagnostic observations: the scan cannot prove the active default endpoint, CS2's source rate, spatial-sound state, or a latency benefit.
- **Local network path stability** — round-trip time and variation to the default gateway. This can reveal a local Wi-Fi, cable, router, or contention problem; it does not measure the route to a CS2 server or hit registration.
- **Panel identity from EDID** — native timing and vertical rate range, read from the panel's own description of itself. Windows only enumerates what the current link can carry, so a display running below native reports its reduced ceiling as though it were the panel's; EDID is the independent second opinion.
- **NVIDIA driver-profile context**, read-only through the vendor interface and loaded from the protected Windows system directory. Values are observations rather than a universal competitive preset; Reflex, VRR, driver version, and bottleneck conditions must be benchmarked.
- **Fast startup**, which hibernates the kernel session instead of ending it, and **interrupt affinity policy** on the display adapter, reported but never written.

- **Multimedia network throttling.** While any process is registered with the multimedia scheduler, the network stack caps non-multimedia packet processing at a documented default of ten per millisecond — which is exactly the traffic an online game depends on. This sits on the same registry key as the CPU reservation and is the more clearly specified of the two.
- **System-wide power throttling**, which reaches the same outcome as clearing the throttle on the game process without opening a handle to the game to do it.
- **Receive segment coalescing**, peer-to-peer update sharing, background packaged-application activity, kernel paging, the machine-wide recording policy, desktop transparency, and the always-on diagnostics trace session.
- **Boot timing options read directly** (`useplatformclock`, `useplatformtick`, `disabledynamictick`, `tscsyncpolicy`) rather than inferred from the performance-counter frequency. Needs elevation, and reports "not read" rather than "nothing set" when it cannot look — those mean opposite things.
- **Speculative-execution mitigation state**, reported alongside memory integrity. Both are surfaced with the trade stated in both directions and neither is written.

### Checked and excluded

The catalogue also ships entries for changes that are widely recommended and do not survive scrutiny — USB selective suspend for an actively-used mouse, disabling the page file, SysMain and memory compression, turning off simultaneous multithreading, debloat scripts, legacy launch options, and network stack registry packs. Each states why it was rejected.

For someone whose ranking is their income the failure mode is not a missing tweak; it is an endless spiral of applying changes that do nothing and attributing normal variance to them. Saying "this was checked and it does not help, here is why" is worth as much as another setting.

Roughly thirty research candidates span CPU/power policy, timing, GPU/presentation, display, input, background activity, and network context. Unsupported registry writes, security reductions, game-process manipulation, raw NIC/driver changes, MMCSS folklore, and timer myths remain visible as **Excluded** with no Apply plan. The retained power-policy experiments show their literal supported writes before approval.

### CPU & platform

A dedicated view for the firmware-level tuning this application deliberately does not write, plus the one stability measurement that is actually available.

**Validating a voltage offset is where almost every guide goes wrong.** A curve offset lowers voltage at every point on the frequency ladder, but the margin it removes is not evenly spread. It bites at the top of the boost range — highest clock, lowest voltage for that clock — and at idle, where the processor makes constant brief boosts and low-power transitions. An all-core stress test can reach neither: loading every core drops boost clocks and raises the voltage supplied for them, so it exercises the safest part of the curve. A configuration can pass one for hours and still reboot sitting at the desktop.

The tab lays out the validation sequence in the order that actually covers those regions — single-core boost cycling first, then real idle uptime, and the all-core run last and least — and states for each step what it cannot catch.

**Hardware error history** is the measurement that makes this tractable. The platform logs machine-check exceptions and corrected errors whether or not anything visible goes wrong, so counting them over real uptime is the only stability signal that covers idle. A clean log is not proof; a dirty one is proof of instability, and on a machine running an offset the offset is the first suspect.

The firmware controls are described per-processor: which ones this part actually exposes, what each does, and — for a cache-stacked part — which are locked and will silently ignore anything entered.

### Verify — did the change actually do anything?

This is the part a settings guide cannot do. Anyone can list registry values; nobody watching a video can tell whether they did anything on *your* machine, so the honest answer to "did that help?" has always been a shrug. Capture the same scenario either side of one recorded change and the shrug becomes a number.

The verdict follows the **tails**, not the average. A change that lifts mean frame rate while widening P99 is a change a competitive player should reject, and that case is common enough that judging by average frame rate is actively misleading. Movement smaller than run-to-run noise is reported as *no measured change* rather than as a win, and a single pair is never called proof — an improvement is reported as worth keeping and worth repeating.

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-apply-all
# play a round, capture before and after
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-verify <transaction-id> before.csv after.csv --revert-on-failure
```

With `--revert-on-failure` a change that regressed or did nothing is undone automatically from the ledger. Pass `any` instead of a transaction id to compare two captures without attributing the difference to a recorded change.

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

The expert tier writes system state. What gates a write is safety and reversibility, not certainty of benefit:

1. the surface is documented or exposed in a supported user interface,
2. the exact prior value can be captured and restored, and
3. it regresses no security guarantee and cannot leave a device unable to start.

Requiring proof that a change helps *before* allowing the change would make the product unable to produce the evidence that would satisfy it — which collapses into an advice list, and an advice list is the one thing a player can already get for free and cannot trust. Whether a change helps is answered by measuring it afterwards, which is what **Verify** exists for.

Every target is additionally checked against a compiled-in allowlist immediately before each write and each restore. This matters because the ledger is user-writable data that a restore replays: without an independent check it would be a command channel rather than a record, and its integrity hash cannot close that, since whoever can rewrite the file can recompute the hash. The allowlist is why privileged writes can be permitted at all instead of being disabled wholesale.

Still never written: anything reaching into the running game, memory integrity, display-driver interrupt edits, boot configuration, and the MMCSS task values Microsoft documents as unused.

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

CPU topology may be reported as context, but the app does not prescribe a launch-time affinity mask or modify a running game. Memory Integrity is reported only; disabling it is neither offered nor recommended.

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

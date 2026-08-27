# FramePath Lab

A Windows tuning tool for competitive FPS that measures your specific machine, applies changes against a verified rollback ledger, and then tells you from a capture whether they actually did anything.

The last part is the point. Anyone can hand you a list of registry values. Nobody watching a video can tell you whether those values did anything on *your* hardware, so the honest answer to "did that help?" has always been a shrug — and a shrug is what a placebo spiral is built on.

---

## Why this is not another tweak list

A typical optimizer script — and every guide it was copied from — has four structural problems. FramePath Lab exists because of them.

**It doesn't know anything about your machine.** It writes the same values everywhere. FramePath Lab reads CPU cache topology, exact display timing, memory configuration from firmware tables, GPU telemetry, audio format, and the live mouse report stream, then only offers what is actually wrong here. A value already correct is reported as settled, not written again.

**It can't undo itself.** The usual safety net is a System Restore Point — coarse, all-or-nothing, and frequently disabled by default. Every change here captures its exact prior value into a durable integrity-checked ledger *before* writing, reads back to confirm, and can be reversed individually. The ledger survives a crash, a reboot, and reinstalling the app.

**It can't tell you if it worked.** This is the big one. Capture the same scenario either side of one recorded change and `expert-verify` gives you a measured delta — judged on the **frame-time tails, not the average**, because a change that lifts mean frame rate while widening P99 is worse in a match and reads as a win on every FPS counter.

**It doesn't know what's fake.** Guides accumulate folklore and never prune it. Fourteen entries here are things that were checked and rejected, each with the reason recorded — because for someone whose ranking is their income, knowing what *not* to chase is worth as much as another setting.

---

## What it measures

Most of this is invisible to any settings audit.

| | |
|---|---|
| **CPU scheduling topology** | Physical/logical cores, SMT, efficiency classes, and last-level-cache groups. Identifies an asymmetric-cache die and a hybrid performance-core set. Cores outside any L3 domain — the low-power island on recent hybrid parts — are grouped separately instead of being dropped |
| **Exact display timing** | The true rational refresh rate via `QueryDisplayConfig`, not a truncated integer, so 59.94-class timings need no guessing. The frame cap is computed from it |
| **Memory configuration** | Parsed straight from SMBIOS firmware tables: per-slot size, part number, configured speed against SMBIOS maximum, channel population. Catches a kit at its JEDEC fallback and modules in one channel |
| **GPU telemetry** | Active clock limiter, PCIe link width and generation against maximum, performance state, BAR1 aperture — through the driver's own library, loaded from the protected system directory |
| **Driver profile** | The per-game profile: performance-state policy, render queue depth, vertical sync, frame limiting, shader cache. The one settings surface no Windows API exposes |
| **Mouse report delivery** | Sustained rate, interval scatter and missed reports, timed from raw input. **Frame capture cannot see this** — it happens before the engine samples input |
| **Wake-up punctuality** | Thread lateness measured against the timer tick, which isolates scheduling delay from timer granularity |
| **Audio render path** | Shared-mode format, endpoint effects, layered spatial processing. A shooter applies its own HRTF; a virtual-surround renderer applies a second, and two spatial models in series blur localisation rather than sharpening it |
| **Hardware error history** | Machine-check and corrected errors over a rolling week. The only stability signal that covers idle |
| **Presentation path** | From the capture itself: independent flip vs composed, vertical sync read from the sync interval rather than game config, CPU/GPU-bound classification, pacing cadence, dropped presents |

---

## How a change gets applied

Four rules, and they hold for every write:

1. **Capture first.** The exact prior value goes into the ledger before anything is written. If a before-state can't be read, nothing is written at all — applying it would create a change that could never be undone.
2. **Verify by read-back.** A write that doesn't verify is reported as unverified, never as success.
3. **All or nothing.** A tweak with several values applies as a unit. If one fails, the ones that landed roll back automatically.
4. **Compare before restoring.** If something else changed the value after FramePath Lab did, that newer state is preserved rather than clobbered.

Every target is additionally checked against a **compiled-in allowlist**, immediately before each write *and each restore*. The ledger is user-writable data that a restore replays — without an independent check it would be a command channel rather than a record, and its integrity hash can't close that, since whoever can rewrite the file can recompute the hash. The allowlist is keyed by registry key *and* value name, because several keys the catalogue legitimately writes also hold values it must never touch.

That check is what makes privileged writes safe to permit at all, instead of disabling them wholesale.

---

## What gates a write

**Safety and reversibility — not certainty of benefit.** A candidate may be written when the surface is documented or UI-exposed, the exact prior value can be captured and restored, and it regresses no security guarantee and cannot leave a device unable to start.

Requiring proof that a change helps *before* allowing the change would make the product unable to produce the evidence that would satisfy it. That collapses into an advice list — the one thing you can already get for free and can't trust. Whether it helps is answered afterwards, by measurement.

Cards fall into five dispositions: **applied by default**, **applied as an experiment**, **guided** (change it in the supported UI), **diagnostic** (a reading, not a change), and **excluded** (with the reason recorded).

### Never written

Anything reaching into the running game, memory integrity, speculative-execution mitigations, display-driver interrupt edits, boot configuration, and the MMCSS task values Microsoft documents as unused.

**Thread placement is the notable one.** Pinning the game means opening a handle to it with rights to change its execution. Nothing about that is cheating, but an anti-cheat can't distinguish intent from behaviour, and for a player whose account is their livelihood that asymmetry isn't worth a few percent. The card shows the preferred mask and the launch command that reaches the same placement without touching the process — alongside the reserved-processor-set reading, which moves everything *else* off those cores instead.

---

## Verify — did it actually do anything?

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-apply-all
# play a round, capture before and after
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-verify <transaction-id> before.csv after.csv --revert-on-failure
```

The verdict follows the tails, weighted by how far each moved rather than counted. P99 carries the most weight because it is the frame you feel in a duel; P99.9 is rarer and noisier; consistency and frames-over-budget largely restate what the percentiles already said. Counting them instead would let two marginal 2% improvements outvote a 20% P99 regression and report an improvement — the exact reading this tool exists to refuse. Frames-over-budget also has an absolute floor, because on a machine holding its budget, 0.05% of frames becoming 0.06% is a 20% "regression" and a rounding error.

A real example — mean frame rate flat, and the change rejected:

```
Median frame time      4.009 →  3.986    -0.57%  noise
P95 frame time         4.832 →  5.302    +9.73%  WORSE
P99 frame time         5.140 →  5.856   +13.94%  WORSE
Frame-time consistency 0.499 →  0.796   +59.38%  WORSE
Mean frame rate      249.787 → 249.984   +0.08%  noise
→ verdict: regressed
```

Every FPS counter calls that a wash. It's a 14% worse P99 — stutter you feel every round.

Movement smaller than run-to-run noise reports as **no measured change** rather than a win, mismatched or too-short captures are refused outright, and a single pair is never called proof. `--revert-on-failure` undoes a change the measurement didn't justify. Pass `any` instead of a transaction id to compare two captures without attributing the difference to a recorded change.

---

## Autotune

One command: measure, apply, measure again, keep what earned its place and reverse what did not.

```powershell
.\work\dotnet\dotnet.exe run --project .	ools\FramePathLab.Cli\FramePathLab.Cli.csproj -- autotune --balanced --isolate
```

Three levels — `--conservative` (recommended defaults only), `--balanced` (adds bounded experiments), `--aggressive` (everything policy permits writing). No level can reach something the policy refuses to write.

Two modes. `--bundle` applies everything and measures once: fast, and answers "was this set worth it" — but cannot say which member did the work. `--isolate` measures one change per pair, which is the only way to attribute a result.

Isolate mode measures the before-state **immediately before each change** rather than carrying one baseline forward through the run. Chaining is cheaper by a run per candidate, but it puts the machine's warm-up drift inside the comparison and it accumulates: six identical runs on an untouched machine trend +1.5%, so the tenth candidate in a chained queue is judged against a machine 1.5% slower than the one the first candidate was judged against, and gets rejected for it. A verdict that depends on queue position is not a verdict. The cost is two benchmark runs per candidate.

**Nothing is kept on the catalogue's own opinion.** A change is applied because policy permits writing it, and retained only because the measurement afterwards supports it. A change that could not be measured is reversed and reported as unmeasured — never counted as a pass, because "we applied it and couldn't tell" is precisely the failure this tool exists to avoid.

**With one exception, and it matters.** A recommended default that measures as no change is kept rather than reversed. Those cards are not frame-rate claims — pointer acceleration is an input-consistency change, and the benchmark never moves the mouse, so acceleration can only ever measure as no change. Reversing it on that verdict would turn acceleration back *on* and call it evidence, which is the tool making the machine worse under its own banner. The exemption is narrow: it covers the no-measured-change verdict only. A default that actually widens the frame-time tails is still reversed, because that is a measurement the workload genuinely made.

### Paired A/B — the honest way to test one change

```powershell
.\work\dotnet\dotnet.exe run --project .	ools\FramePathLab.Cli\FramePathLab.Cli.csproj -- abtest INPUT-ACCEL-001 --pairs 5
```

Two problems make "measure, change, measure" unreliable, and both showed up in this project's own benchmark.

**Drift.** Six identical runs on an untouched machine, in order:

```
14.09  14.12  14.18  14.17  14.28  14.30      median frame time, ms
```

That is not scatter, it is a monotonic +1.5% trend as the machine warms. Measure three "before" then three "after" and you record a **0.85% regression that does not exist**. So measurements are interleaved in a balanced order — off, on, on, off — giving both conditions the same average position in time, which cancels a linear trend instead of attributing it to the change.

**No error bar.** One difference cannot say whether it exceeds what two identical runs would produce anyway. Several pairs give a distribution of differences, and the interval around their mean separates a real effect from luck. The comparison is paired, not pooled: each pair shares its own conditions, so what matters is the difference *within* a pair.

Measured noise floor on this machine, six runs, no change applied:

| metric | run-to-run sd | range |
|---|---|---|
| median frame time | 0.58% | 1.45% |
| P95 frame time | 0.77% | 1.80% |
| P99 frame time | 1.16% | 3.14% |
| frame-time consistency | 1.34% | 3.13% |

Anything below roughly 2% is indistinguishable from the machine's own variation, which is where the practical threshold comes from.

**How many pairs, and how long?** Detection margin scales as `1 / sqrt(frames × pairs)`, so total time is what buys precision — but *how* it is split matters, because the small-sample critical value is brutal. Five thirty-second runs beat two two-minute runs decisively despite being shorter overall: at two samples the 95% critical value is 12.7, at five it is 2.78. Below three pairs almost nothing can be concluded.

The default is five pairs against a frame target rather than a duration, with early stopping once the interval settles. That detects a 2% change on P99 and refuses to pretend otherwise when it cannot.

A result is called **conclusive only when the whole interval sits past the 2% threshold**, not merely when it excludes zero. A mean of 2.1% with an interval running from 0.3% to 3.9% is consistent with an effect far too small to act on, and calling that settled is how a tool starts manufacturing wins out of noise.

Early stopping is honest about what it costs. Re-examining the interval after every pair is optional stopping: each extra look is another chance for noise alone to produce one that clears the bar, so the true false-positive rate sits above the nominal 5%. Two things hold it down — the conclusive rule above is much harder to clear than "excludes zero", and a positive verdict cannot end a run before four pairs. Ruling an effect *out* early is unrestricted, because stopping there costs a false negative rather than a false claim.

### The benchmark it runs

The app renders its own, so nothing external is needed and there is no capture to lose.

It presents through a real DXGI flip-model swap chain — **the same path a modern game uses** — so present-path changes actually register. An OpenGL timedemo cannot answer that, and it reports average throughput, which is the metric that hides the problem.

The frame is shaped like a shooter's rather than like a throughput loop: a **pointer chase over a 32 MiB working set**, where each step's address comes from the previous step's value. That work is bound by memory latency, not arithmetic, which is why cache capacity moves it — and why a processor carrying stacked cache pulls ahead here the same way it does in a real title. A tight loop over a cache-resident array would never miss, so it would report cache and memory changes as doing nothing. Part of the work is dispatched across threads so core availability and scheduling policy register the way an engine's job system would.

The workload is **fixed, not calibrated**. A benchmark that adjusts its work until every machine reports the same frame time cannot compare machines — and worse, it would quietly compensate for a change that made the processor slower. Runs continue until enough frames exist to compare, so a slow machine takes longer rather than producing a result too short to use.

Measured repeatability on an idle machine: P99 spread **0.5%**, median **1.8%** — below the 2% noise band, which is what makes a real difference attributable.

The load is **not flat**. A constant workload leaves only system noise in the frame-time tail, so the percentile that decides every verdict measures the wrong thing. A real session is not flat either — an engagement puts more entities in view, smoke puts heavy overdraw on screen — and those transitions are what a player feels when a machine handles them badly. So the run cycles through phases (holding, engagement, smoke, rotate, flash) that scale both processor work and graphics fill, **on a fixed schedule driven by the frame index**. Identical every run. Realistic tails and exact repeatability at once, which is the thing "just play a round" can never give you.

Graphics work is real fill through the driver's command submission path, so changes to hardware scheduling, driver settings and power states have something to move rather than presenting an empty frame.

It is a proxy, not the game. It exercises the presentation path, the scheduler, memory latency, graphics submission and power behaviour faithfully, because those are shared. It does not reproduce a specific engine's shader or draw-call mix. Anything that turns on engine specifics still gets confirmed against a real capture.

## CPU & platform

A dedicated view for the firmware-level tuning this app deliberately doesn't write.

**Validating a voltage offset is where almost every guide goes wrong.** A curve offset lowers voltage at every point on the frequency ladder, but the margin it removes isn't evenly spread. It bites at maximum single-core boost — highest clock, lowest voltage for that clock — and at idle, where the processor makes constant brief boosts and low-power transitions.

An all-core stress test reaches neither. Loading every core drops boost clocks and raises the voltage supplied for them, so it exercises the *safest* part of the curve. A configuration can pass one for hours and still reboot sitting at the desktop.

So the tab lays out the validation sequence in the order that covers those regions — single-core cycling first, real idle uptime second, the all-core run last and least — and states for every step what it structurally cannot catch.

**Hardware error history** is what makes this tractable. The platform logs machine checks whether or not anything visible goes wrong, so counting them over real uptime is the only stability signal covering idle. A clean log isn't proof; a dirty one is proof of instability, and on a machine running an offset, that offset is the first suspect.

Firmware controls are described per-processor: which ones this part actually exposes, what each is for, and — on a cache-stacked part — which are locked and will silently ignore anything entered. It also flags that the idle power-state setting produces the same symptom as an over-aggressive curve; the two are constantly mistaken for each other.

---

## Power settings Windows hides from itself

Windows marks a number of power-scheme values hidden. Hidden means absent from the settings interface — the power API reads and writes them regardless, so no unhiding step is needed and they revert through the ledger like anything else. Most guides tell you to run an attributes command to expose them in the UI first; that is only necessary if you intend to click them.

- **PCIe link state power management.** The link to the graphics card and to storage drops into a low-power state between transfers and must be woken before the next one. That wake is paid on the first access after any idle gap, which for a frame beginning with a texture fetch is paid inside the frame. The one hidden setting with a plausible mechanism for graphics and storage stutter simultaneously, which is why it circulates.
- **Storage link power management** and **drive idle timeout** — the same argument one layer down, and the shape of a game streaming an asset mid-round rather than at a load screen.
- **Latency sensitivity hint response** — what share of maximum performance the processor jumps to when something signals it is latency sensitive, instead of ramping. Better behaved than disabling idle, because it lifts clocks only when asked.
- **Processor idle states**, offered and deliberately flagged as the most double-edged item here: cores that never idle give back no power budget, so the cores doing the work have less headroom to boost into. On a modern part this frequently costs more than it saves.

## Services

Around 35 Windows services, offered one at a time, each stating exactly what stops working — "printing stops entirely", "other machines can no longer reach shares hosted here", "biometric sign-in stops".

Three gates before anything is offered:

1. **It exists** on this edition. Many do not.
2. **Nothing live depends on it** — checked against the dependency graph inverted from the live system, not from a hardcoded list. Windows records dependencies one way, so answering "what breaks if this stops" means walking the whole set and inverting it.
3. **It is not load-bearing.** A never-offered list covers the remote procedure call layer, plug and play, the event log, audio, cryptography, networking core, the firewall filtering engine and the security services. Those are refused at the allowlist, not merely omitted from the catalogue.

The dependency gate is not theoretical. On the machine this was built against it refused four candidates that appear on essentially every debloat list — including **Windows Search**, which had two live dependents.

Only the start type is ever written, only on a curated service, and the prior value goes in the ledger like any other change. A nested key beneath a service is refused.

**On what this is worth:** mostly not frame rate. Services cost background wakeups, memory and — for the few that touch storage — disk activity that can land inside a frame. Content indexing and the prefetcher are the two most likely to show up. Several will measure as doing nothing, which is a fine result and precisely why each is separate and why `abtest` exists. Do not take the list on faith.

## Devices

Present devices that are running a driver while nothing is using them. A loaded driver can take interrupts and queue deferred calls whether or not the hardware is doing anything, and the ones that misbehave are usually a second network adapter, a Bluetooth radio scanning for nothing, or an onboard codec left enabled while sound leaves over USB.

Two gates:

1. **The class is offerable.** Bluetooth, network, audio, imaging, biometrics, smart-card readers, sensors, printers and modems. Everything else is refused at the allowlist — input, USB, storage, display, processors, firmware, system devices. Unrecognised classes fail closed.
2. **Nothing is using it.** Checked against the live routing table and the audio endpoints, not against the device name. The adapter carrying the connection is refused outright, and where only one audio endpoint exists every audio device is treated as in use, because mapping an endpoint back to the codec behind it by name is not reliable and being wrong there means offering to disable the sound.

The disable is deliberately **not persistent** — the device returns on the next restart. A wrong call costs a reboot rather than a support session, which is what makes this cheap enough to actually test.

### System devices

The class every tweak guide points at, and the one where class membership decides nothing: it holds both the PCI root complex, where a wrong disable means the machine does not come back, and a virtual drive enumerator, where it means nothing at all. So System is allowed **one device at a time**, keyed on the instance identifier — which, unlike the friendly name Device Manager shows, is not localised and does not change with a driver package.

Two things fall out of looking at it properly:

- **Most of that advice targets software.** The composite bus, UMBus, the virtual drive enumerator, the Hyper-V providers, the NDIS virtual bus — all enumerated under `ROOT`, all with no hardware behind them. They raise no interrupts and queue no deferred calls, so disabling one unloads a driver image and changes nothing measurable. They never reach the policy; the hardware-prefix filter removes them first.
- **Nearly everything left is load-bearing.** Host bridges, ACPI roots, motherboard resource reservations, the interrupt controller, the real-time clock, the embedded controller, the management engine — refused by name, with the reason shown rather than the device silently going missing.

On the development machine that is 71 System devices present and **2** offered.

The one that survives on most machines is the **high precision event timer**, and it is gated on a measurement rather than on faith: it is only offered when boot configuration is confirmed not to force the platform clock *and* the performance counter is running at the 10 MHz Windows reports when it is backed by the timestamp counter. If either says the platform timer is serving the counter, the card is blocked and points at the `useplatformclock` entry instead — because removing the clock the system is currently reading is not something to try and measure. An unreadable boot state also blocks it; failing closed is the whole point.

Worth being blunt about the size of this one: the effect has shrunk with every hardware generation, from a widely reported 10–15% on 2019-era platforms to low single digits or nothing today, and on Ryzen it was always smaller than on the Intel platforms the advice grew up on. It is offered because it is measurable and cheap to undo, not because it is expected to help.

A diagnostic card lists what was refused and why, so an absent item reads as a decision rather than an oversight.

**On what this is worth:** less than the internet thinks. Mechanically it does remove that driver's interrupt activity. Whether removing it matters depends on whether the driver was doing anything, and most idle devices were not. On the development machine the filters left exactly two candidates out of everything present. It is offered as an experiment with an A/B behind it, not as a recommendation.

## Drivers

"Vendor driver, or the one Windows installed?" is normally argued rather than checked, and the honest answer is per subsystem rather than universal. Windows records the provider, version, date and bound kernel service for every device, so the machine can be asked instead of the internet.

One card per subsystem where the answer actually differs — audio, network, graphics, storage — reporting what is bound and what that means:

- **Audio: the vendor driver, minus what ships beside it.** The generic High Definition Audio class driver cannot do jack detection, front-panel retasking or channel configuration, and on several codecs will not offer the full set of sample rates. For an onboard codec the vendor driver is the better choice. What it usually arrives with is not: board vendors bundle an audio effects suite, and a processing object inserted into the render path is a real and repeatedly measured source of added output latency. Install the driver, decline the effects suite, and check `AUDIO-EFFECTS-001`. If sound leaves over USB or a wireless receiver, none of this applies and the onboard codec becomes a candidate for the device list instead.
- **Network: the driver matters less than its settings.** Inbox network drivers are often older vendor builds and expose fewer of the advanced properties that decide latency behaviour, which is the real reason to install the vendor package. Whichever is bound, the energy-efficiency, interrupt moderation, flow control and power-down cards are where the behaviour is actually set.
- **Graphics: age is the signal.** Flagged past a year. Engine-specific fixes and presentation-path work land continuously; that is an argument against a stale build rather than for the newest possible one.
- **Storage: the inbox driver usually wins.** The one class where what Windows ships is generally preferable — best-tested path, and vendor storage stacks have historically caused more stalls than they cured. A RAID or volume-management layer on a machine not running an array is a translation layer for nothing. Reported rather than offered, because moving off one means changing the firmware storage mode, which makes the installation unbootable unless prepared for first.

Judged on recorded provenance, not on service-name pattern matching — matching names for "raid" called Intel's VMD stack a Microsoft driver, which it is not.

Software devices are filtered first. The network class is mostly miniports, virtual adapters and debug shims, which carry driver bindings exactly like real hardware; without that filter the audit reports a dozen Microsoft-provided drivers that were never a choice in the first place. On the development machine that is 26 bindings down to 13.

Nothing here writes. Installing a driver is an installer's job, and a class-key write is not a supported way to change one.

## Another machine

The machine worth tuning and the machine worth sitting at are rarely the same one, and that is not incidental — a competitive machine is kept deliberately clean, and the point of tuning it is that it is about to be played on.

So collection, review and application are three separate steps, and only two of them happen on the target:

```bash
FramePathLab.exe --collect
```

Reads the machine and writes a `.fplscan` file to the desktop. About a minute, no prompts, no writes. Run it as administrator or most of the catalogue comes back unreadable, and it will say so.

Copy that file anywhere — a laptop, a different desk — and open it with **Open snapshot**. The whole catalogue evaluates against it: same cards, same verdicts, same exact writes listed, with every apply control replaced by a tick box and a banner that says nothing there writes to the machine you are sitting at. Tick what you want and **Export plan**.

Take the `.fplplan` back and:

```bash
FramePathLab.exe --apply GAMING-PC-20260827-1830.fplplan
```

Which rescans, refuses if the fingerprint says this is the wrong computer, and applies through the same ledger, allowlist and read-back verification as any other change — then writes a result file listing what landed and what did not.

Three properties make this safe rather than convenient:

- **A snapshot carries observations only.** There is no instruction in it. Opening one from an untrusted source can at worst describe a machine that does not exist.
- **A plan carries identifiers only** — never registry paths, values or device nodes. The target re-derives every write from its own compiled-in catalogue against its own fresh scan. An edited plan file can only ever select from what that machine was already willing to do; it cannot introduce a write, retarget one, or smuggle a value past the guard. A plan that carried mutations directly would be a command channel into an elevated process, which is the hole the allowlist exists to close.
- **Review reproduces the target's gates**, including the allowlist and whether the collection ran elevated, so what is selectable on the laptop is what will actually be offered on the desktop.

Snapshots work by recording every question the catalogue asks the machine and replaying the answers, so the review path and the live path run identical code. A read with no recorded answer reports the surface as absent rather than guessing at it, which is what an older snapshot genuinely knows.

## Checked and excluded

Fourteen entries, each with its reasoning recorded. A sample:

- **Debloat scripts** — the objection is the bundling, not the idea. Forty changes at once means nothing is attributable, nothing is individually reversible, and several entries are load-bearing for something the author never considered. Individual services are offered separately; see below.
- **USB selective suspend** — a mouse in use is never idle, so it's never suspended. One of the most-recommended tweaks on the internet; does nothing during a match.
- **Nagle and TCP acknowledgement tuning** — real settings that do exactly what they claim, on a protocol competitive shooters don't use.
- **Disabling graphics timeout detection** — converts a recoverable two-second stall into a machine that needs a power cycle.
- **Disabling Secure Boot and TPM** — several anti-cheats require them. Being locked out of your league is categorically worse than any frame-time gain.
- **All-core validation of a voltage offset** — structurally the wrong test, for the reason above.
- **The flip queue registry value** — the real control is a driver profile setting this app already reads.
- Plus page-file removal, SysMain and memory compression, disabling SMT, debloat scripts, legacy launch options, GPU preemption keys, input queue sizes, and RTC interrupt priority.

---

## Build and run

The workspace-local .NET 10 SDK is used automatically when present:

```powershell
.\build.ps1
```

Desktop app:

```powershell
.\work\dotnet\dotnet.exe run --project .\src\FramePathLab.App\FramePathLab.App.csproj --configuration Release
```

CLI:

```powershell
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert --measure-input
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-apply-all
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- expert-revert all
.\work\dotnet\dotnet.exe run --project .\tools\FramePathLab.Cli\FramePathLab.Cli.csproj -- analyze .\samples\presentmon-sample.csv 4.1667
```

Machine-scope changes need an elevated session. Every privileged write is still checked against the allowlist first.

## Local data

The tweak ledger, derived history and the power-session recovery journal live under `%LOCALAPPDATA%\FramePathLab`. Raw imported captures are never copied. Deleting derived history does not delete the ledger — that would strand applied changes with no way back.

## Repository layout

- `src/FramePathLab.Core` — models, catalogue, policy, allowlist, analysis, verification, persistence.
- `src/FramePathLab.Windows` — scanners, documented native interop, the mutation executor.
- `src/FramePathLab.App` — WPF desktop UI.
- `tools/FramePathLab.Cli` — headless CLI.
- `tests/FramePathLab.Tests` — dependency-free executable test suite.
- `samples` — synthetic import fixture; not a performance reference.
- `outputs` — research scope and build roadmap.

The synthetic sample exists only to verify the import path. It is not performance evidence.

## Honest limits

- A single before/after pair is a measurement, not proof of causation. Repeat it.
- The mouse probe times delivery to a user-mode process, so USB scheduling and driver batching are inside the number. It is not the device's electrical report rate.
- Software timing fields from a capture are not mouse-to-photon latency. True click-to-photon needs hardware.
- The network probe measures the first hop, not the route to any game server.
- Firmware readings are reported as firmware states them; SMBIOS values are wrong on some boards, and the cards say so where that applies.
- Every tweak is an experiment with an uncertain benefit on any particular machine. That is what the verification workflow is for.

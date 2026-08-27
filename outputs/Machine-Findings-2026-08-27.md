# FramePath Lab — findings for this machine

Scanned 2026-08-27, unelevated and elevated, with the fixed build.

## The machine

| | |
|---|---|
| CPU | Ryzen 7 5800X3D — 8C/16T, one core group, 96 MiB L3 (12 MiB/core), stacked cache detected, multiplier locked |
| GPU | RTX 3080 — driver 591.44 (2025-12-02, 268 days old), PCIe x16 Gen4 of x16 Gen4, BAR1 aperture 16384 MiB |
| Display | BenQ ZOWIE XL, 1920×1080 @ **360 Hz** |
| Memory | 2×16 GB G.Skill F4-3600C16-16GVKC, Channel A + B, running 3600 of rated 3600 |
| Board | MSI MAG X570 Tomahawk WiFi, BIOS 1.H0 (2024-07-16) |
| OS | Windows 11 Pro 25H2, build 26200 |
| Power | Ultimate Performance, min/max processor state 100, core parking off, PCIe ASPM off |

**The shape of the problem:** 1080p on a 3080 with a 5800X3D is hard CPU-bound. The GPU sits
idle-ish (the scan caught it in P0 with "GPU idle" as the limiter). Frame times at 360 Hz are
~2.8 ms, so tail latency and input path dominate. GPU-side tuning has very little to win here;
CPU scheduling, presentation path and input latency have most of it.

## Starting position: this machine is already tuned

126 cards evaluated. 62 optimal, 27 suboptimal, 19 not applicable, 14 unknown, 4 blocked.

Already correct and left alone: GameDVR off (all four keys), Game Bar capture off, background apps
off, transparency off, fast startup off, `GlobalTimerResolutionRequests` on, `DisablePagingExecutive`
on, `SystemResponsiveness` 10, `NetworkThrottlingIndex` 0xFFFFFFFF, `PowerThrottlingOff` on,
Delivery Optimization off, pointer acceleration off, pointer speed at the middle notch, VBS and
Memory Integrity off, HDMI audio disabled, both SATA controllers disabled, and roughly fifteen
services already disabled including SysMain, Windows Search, DiagTrack, DPS and the print spooler.

NIC is already right too: interrupt moderation off, EEE off, Green Ethernet off, flow control off.

So the list below is short, and that is the correct outcome rather than a disappointing one.

---

## Worth doing — highest value first

### 1. NVIDIA driver settings (not written by the app — do it in the control panel)

The scan found **no driver profile for cs2.exe at all**; it fell back to the global base profile,
where two settings are wrong for this workload:

| Setting | Now | Should be |
|---|---|---|
| Power management mode | **Adaptive** | **Prefer maximum performance** |
| Low latency mode | **Off (up to three frames queued)** | On, or leave to Reflex |

Power management mode is the one that actually matters here, and it matters *because* the machine
is CPU-bound. With Adaptive, low GPU utilisation lets the driver drop core and memory clocks; the
next frame that does need the GPU pays the ramp. At 1080p in CS2 the GPU is under-loaded almost
constantly, which is exactly the condition Adaptive misreads. Set it per-profile for cs2.exe rather
than globally so desktop idle power is unaffected.

**Enable NVIDIA Reflex in CS2's own video settings** (Enabled, or Enabled + Boost). CS2 has Reflex
built in, and when it is active it manages the render queue directly and supersedes the driver's
Low Latency Mode. Reflex in-game beats Low Latency Mode in the driver — do not set both.

### 2. `DX-SWAPCHAIN-001` — optimisations for windowed games (currently **off**)

`DirectXUserGlobalSettings = SwapEffectUpgradeEnable=0`. Turning this on lets windowed and
borderless presentation use the flip model instead of being composited through DWM. If you play
fullscreen-windowed — most CS2 players do, for alt-tab — this removes a compositor copy from every
frame. Recommended default, instant restore.

### 3. `GAMEMODE-001` — Windows Game Mode (currently **off**)

Game Mode is off. On Windows 11 this is worth having on: it holds back background scheduling and
deferred work while the game is foreground. The old advice to disable it is a Windows 10-era
holdover. Recommended default, and cheap to A/B if you want to confirm.

### 4. `POWER-OVERLAY-001` — power mode overlay (currently **Balanced**)

The scheme is Ultimate Performance but the overlay on top of it still reads Balanced, which is a
genuinely confusing Windows behaviour — the overlay biases the processor's energy preference
independently of the plan. Worth setting to Best performance and measuring.

Related: **Processor energy performance preference policy is 10**, not 0. Ultimate Performance
normally sets 0. On Zen 3 the CPPC EPP path does less than it does on Intel, so expect little —
but it is a free A/B.

### 5. `DISK-NVME-IDLE-001` — NVMe idle timeout (currently **200 ms**)

Primary and secondary NVMe idle timeouts are 200 ms. Setting to 0 keeps the controller awake so an
asset streamed mid-round does not pay a wake. This is the one hidden power setting on this machine
with a plausible mechanism and a real target. Experiment, exact restore.

### 6. `TELEMETRY-TRACE-001` — DiagTrack autologger (needs admin, needs reboot)

The autologger session is still Running even though the DiagTrack *service* is disabled — the
kernel trace session is separate and still writing to disk. Recommended default.

---

## Worth testing, genuinely uncertain

- **`DX-FSO-001`** — disabling fullscreen optimisations for cs2.exe. Contested for CS2 specifically;
  FSO is the mechanism that gives you flip-model in borderless. Measure both ways, do not assume.
- **HAGS** is currently **on** (`HwSchMode=2`). Competitive players report both directions. It is a
  guided action — change it in Windows graphics settings, restart, and A/B it.
- **`CPU-LATENCY-HINT-001`** — 99 → 100. One point. Almost certainly nothing.
- **`CPU-IDLE-DISABLE-001`** — flagged high risk, and the app's own card is right to be sceptical:
  on a power- and thermally-limited stacked-cache part, cores that never idle give back no budget
  for the working cores to boost into. Likely negative. Test it last, if at all.

## Not worth doing

- **14 services** are offered (Smart Card, Biometrics, Phone, Wallet, NFC, Camera Frame Server,
  UPnP, Print Notify, and similar). All currently Manual — meaning they are not running and cost
  nothing while idle. Disabling a stopped Manual service saves you nothing at runtime. Skip them.
- **HPET** is now offered (after the fix below), and it is the only device on the machine that can
  be disabled. Be blunt about the size: QPC is confirmed TSC-backed at 10 MHz, so HPET is not
  serving the clock, and on Ryzen this was always a smaller effect than on the Intel platforms the
  advice came from. Offered because it is measurable and a reboot undoes it, not because it will help.

## Device Manager: nothing left to disable

- 141 present devices, 68 of them System class. **One** is offered (HPET).
- Already disabled by you: NVIDIA HDMI audio, both SATA AHCI controllers, GS Wavetable Synth,
  Device Association Root Enumerator, SteelSeries GG component.
- Refused correctly: Realtek NIC and Realtek audio are both in use. Everything else in System is
  host bridges, root ports, motherboard resources, ACPI, DMA, RTC and the interrupt controller.
- No Bluetooth or WiFi device is present at all — the AX200 appears to be off in BIOS. Good.

---

## Things to look at outside the app

**Process Lasso is running** (`ProcessGovernor` service, auto-start). Two consequences:

1. It will corrupt every measurement this tool takes. It reassigns affinity and priority
   dynamically, so a benchmark pair can differ for reasons that have nothing to do with the change
   under test. **Stop it before any `abtest` or `autotune` run**, or the numbers are noise.
2. It does continuously what FramePath Lab deliberately refuses to do — open handles to running
   games to change their execution. That is your call to make and Process Lasso is widely used
   without incident, but you should make it knowingly, given the app's stated reasoning.

On a single-CCD 5800X3D there is nothing for affinity management to choose between anyway — the
scan says so directly: *"Single unified core group; no placement decision exists on this CPU."*

**Chrome was holding 8.5 GB** in one process with ~18 more alongside, plus a ChatGPT client. Close
them before measuring, and before playing.

**GPU driver is 268 days old.** Not stale enough for the app's one-year flag, but CS2 and Reflex
work lands continuously. Worth updating — and re-testing after, since it invalidates prior baselines.

**Panel EDID reports a 48–240 Hz range** while the display runs at 360 Hz. Either the app is only
reading the base EDID block and missing the CTA extension, or the panel is not the model it appears
to be. Worth confirming which — it does not affect anything today because this ZOWIE has no VRR, so
the app's 357 FPS VRR cap advice does not apply to you. Run uncapped with V-Sync off.

**BIOS 1.H0 is from July 2024.** Check MSI for a newer AGESA. On a 5800X3D the thing to want from a
firmware update is Curve Optimizer exposure — the CPU & platform tab lays out the validation order
if you go there, and it is right that single-core cycling and idle uptime catch what an all-core
stress run structurally cannot.

**One Defender path exclusion is configured.** Worth confirming it is the CS2 install directory.

---

## App bugs found while doing this

Fixed and pushed on `fix/review-findings`:

- Autotune reverted recommended defaults on a "no measured change" verdict — including turning
  pointer acceleration back **on**, because the benchmark never moves the mouse.
- `MutationKind.DeviceState` had no case in the executor's write switch, so every device card threw
  on the write and threw again on the rollback. Device disabling could not work at all.
- `NET-PNPCAP-` and `NET-FLOW-` built write plans that policy never classified, so both were
  silently downgraded to diagnostics.
- Isolate mode chained baselines, so warm-up drift decided late candidates' verdicts.
- `NetworkThrottlingIndex` (0xFFFFFFFF) overflowed on write and failed read-back verification.
- The platform-timer gate consulted a value hardcoded to null, and read an absent bcdedit option as
  "unknown" rather than "not set" — so HPET could never be offered on a healthy machine.
- Conclusive verdicts, tail weighting, ledger backup recovery, registry type fidelity, and removal
  of the dead process-mutation code and its `OpenProcess`/`SetProcessInformation` imports.

Still outstanding, not fixed:

- `memory.populatedChannels` reports **0** despite both `bankLocator` values reading Channel A and
  Channel B. The derived `isSingleChannel` flag is correct, the count is not.
- `networkPath` reports median 0 ms, jitter 0 ms, worst 0 ms to the gateway. LAN RTT is sub-
  millisecond and the probe's resolution is whole milliseconds, so the jitter metric cannot say
  anything on a wired LAN. Needs sub-millisecond timing to be useful.
- The EDID parser appears to read only the base block's range limits (see the 48–240 vs 360 Hz
  discrepancy above).

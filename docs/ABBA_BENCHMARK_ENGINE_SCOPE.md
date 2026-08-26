# ABBA benchmark engine safety and v0.5 scope

## Product boundary

The engine automates experiment orchestration, not CS2 gameplay. It may apply an already-approved reversible setting, wait for thermal stability, start and stop an external PresentMon ETW capture, collect out-of-process hardware telemetry, validate the run, analyse paired results, and restore the baseline. It must not inject a DLL, hook or overlay into CS2, read or write game memory, synthesize input, run a gameplay macro, alter packets, automate matchmaking, or modify game files.

Valve says hardware configurations and updated system drivers do not trigger VAC, while third-party modifications designed to give a player an advantage do. Valve does not publish an explicit PresentMon approval, so FramePath Lab must not promise zero ban risk. Sources: [Valve VAC FAQ](https://help.steampowered.com/en/faqs/view/571A-97DA-70E9-FF74), [PresentMon project](https://github.com/GameTechDev/PresentMon), and [PresentMon console documentation](https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md).

The strict mode therefore uses the standalone PresentMon console collector without an in-game overlay and never requests a game-process handle. PresentMon observes Windows ETW presentation events and can target an executable name or process identifier. It may require membership in Windows' Performance Log Users group or elevation to start its trace session; the app must explain this rather than silently elevating the whole desktop process.

## CS2-relevant protocol

Use a local practice session or a private controlled server, never Premier, Competitive, or another live match. The player performs a rehearsed 60-90 second route manually. The app supplies an external countdown and audio cues, but sends no keyboard or mouse input. This retains CS2's real executable, renderer, assets, presentation path, input sampling, and audio workload without automating play.

A deterministic demo-playback workload may later be offered for renderer/frame-pacing studies only after its current CS2 support and launch path are validated. It must not be described as input-latency evidence because playback removes the player's input path and differs from live simulation/network work.

## Automatic sequence

1. Record consent, exact A and B settings, rollback data, app/PresentMon hashes, CS2 build, OS, driver, display mode, refresh rate, power state, and active overlays.
2. Require local/private-session confirmation. Wait for `cs2.exe` and foreground focus through ordinary Windows observation; do not open the game process.
3. Warm the workload with two unscored rehearsal runs and wait until GPU temperature and clock trends are inside configured stability bands.
4. Run an A/A noise calibration on first use. Refuse to judge changes smaller than the measured repeatability floor.
5. Randomly choose an `ABBA` or `BAAB` block. Use at least two blocks (eight scored runs), each 60-90 seconds, with the same scenario and a thermal-stability gate between runs.
6. Before each run, verify the setting by a supported read-back, start a uniquely named PresentMon session, show the countdown, capture for the fixed window, stop normally, and reject captures with focus loss, unexpected display-mode changes, insufficient frames, lost ETW events, or telemetry gaps.
7. Restore A after every B run and at completion, cancellation, crash recovery, or failed verification. Reboot-required experiments such as HAGS are separate resumable sessions, not live toggles.
8. Keep B only after explicit user approval and only when the paired result exceeds both the A/A noise floor and a practical threshold without a material regression.

## Measurements

Per run retain frame-time median, p95, p99 and p99.9, displayed-frame/drop counts, present mode, CPU busy/wait, GPU time, and capture integrity. Sample NVIDIA/AMD telemetry at a conservative rate such as 10 Hz: utilization, effective clocks, temperature, board power where supported, performance limit/throttle reason, VRAM use, and PCIe state. Timestamp every sample from the same monotonic clock and measure the telemetry collector's own overhead.

PresentMon warns that several GPU execution metrics are less accurate with Hardware-Accelerated GPU Scheduling enabled. Comparisons must never mix HAGS states inside one non-reboot block, and the affected fields must be qualified rather than treated as exact GPU work. Software telemetry is not physical input-to-photon latency; that requires an LDAT/Reflex Analyzer or another optical/input instrument.

## Decision rule

Analyse paired B-minus-A differences by block, report every run, and bootstrap a confidence interval over paired block effects. A result is a candidate win only when its interval excludes zero, its direction is consistent across blocks, it clears the machine's A/A noise floor and configured practical threshold, and it causes no thermal, clock, frame-drop, stability, security, or presentation-path regression. Otherwise report **inconclusive**. One capture can never produce Keep/Revert advice.

## Required implementation gates

- Hash-pinned, licensed PresentMon binary or a user-selected verified binary; no unverified download or PATH lookup.
- No overlay, input hook, input synthesis, game-file edit, RCON gameplay automation, DLL injection, or packet access.
- Per-run manifests, append-safe durable state, crash recovery, exact rollback, and read-back verification.
- Observer-effect calibration for PresentMon alone, telemetry alone, and both together.
- Synthetic test application for CI; CS2 is never launched in automated tests.
- A visible **Strict passive / career-safe** mode that is the default and cannot be bypassed by a tweak definition.

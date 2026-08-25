# FramePath Lab 0.3.0 — scan-first workflow

FramePath Lab 0.3.0 is an unsigned Windows x64 developer preview. It redesigns the app around the pre-game task: scan the PC, see what is ready or unknown, choose one supported action, then rescan or benchmark to verify it.

## What changed

- Opens on a clear **Pre-game** dashboard instead of a technical evidence or system-change page.
- Shows four honest status groups: **Ready / already set**, **Change available**, **Check manually**, and **Unavailable**.
- Every card shows the current observation, recommended next step, evidence level, trade-offs, and its directly connected action.
- Unknown settings are never guessed as disabled.
- Manual actions use supported Windows Settings pages for Advanced display, Game Mode, and default graphics settings.
- A 59 Hz versus 60 Hz TV-compatible timing pair is shown as a verification case, not a tweak. Microsoft documents that these labels can map to the same 59.94 Hz timing and require no corrective action by themselves.
- The CS2 checklist keeps Reflex, Boost, V-Sync, VRR, frame caps, and display mode as user-controlled, one-variable-at-a-time tests.
- Technical scan evidence, frame-pacing analysis, local history, and the safety boundary remain available on secondary tabs.
- The dark theme now applies explicit foregrounds to the window, text, item containers, data grids, and keyboard focus states, fixing the unreadable black-on-dark and pale-on-white rendering.

## Real system-changing capability

The only automatic mutation remains an explicitly approved, 15-minute switch to an already-installed standard Windows **High performance** power plan. It records the exact prior plan, verifies apply and restore, arms an independent rollback guardian, preserves external changes, and does not elevate, create, edit, unhide, or delete plans. A performance benefit is uncertain and may be zero.

All other actions are read-only scans, supported Settings links, user-controlled in-game checks, local capture analysis, or report/history operations.

## Important limits

- This build cannot reliably read Game Mode, HAGS, VRR, Reflex, V-Sync, or CS2 frame-cap state through supported public APIs. Those cards say **Check manually** rather than pretending the setting is off.
- The display scanner currently enumerates compatible integer refresh modes. It does not yet use rational `QueryDisplayConfig` timing, detect in-game refresh, prove VRR is active, or change display topology.
- Software frame-delivery captures do not measure physical mouse-to-photon latency.
- The executable is not code-signed or installed; Windows may show an unknown-publisher warning.

## Primary references for the new guided actions

- [Microsoft Settings URI reference](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Microsoft refresh-rate guidance](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-the-refresh-rate-on-your-monitor-in-windows)
- [Microsoft 59/60 Hz timing explanation](https://support.microsoft.com/en-gb/topic/screen-refresh-rate-in-windows-does-not-apply-the-user-selected-settings-on-monitors-tvs-that-report-specific-tv-compatible-timings-0a7a6a38-6c6a-2aec-debc-5183a76b9e1d)
- [NVIDIA system-latency optimization guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/)

## Verification performed

- Release solution build with warnings treated as errors.
- 17 dependency-free tests covering capture parsing, evidence boundaries, history, power-plan transactions, rollback conflicts, AC/policy rejection, guardian failure, recovery, and journal tamper detection.
- Live startup scan and visual inspection of the rebuilt WPF window.
- Accessibility-tree inspection confirming labelled tabs, scan button, status text, cards, actions, and Details controls.
- No live system mutation was performed during release verification.

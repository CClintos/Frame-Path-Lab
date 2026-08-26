# FramePath Lab 0.4.0 — evidence-gated expert tier

This release audits and hardens the expert-tier changes introduced after 0.3.0. It deliberately removes unsafe breadth before adding more automatic tuning.

## Changed

- Every expert candidate is now classified as a default recommendation, A/B experiment, guided action, diagnostic-only item, or exclusion.
- Security disabling, CS2 process affinity/EcoQoS, timer/MMCSS/quantum folklore, GPU MSI, raw network-driver registry writes, and unsupported graphics registry changes have no executable Apply plan.
- Only supported power-policy candidates remain eligible as benchmark-only experiments.
- The Windows 11 power-mode experiment now uses documented `PowerGetUserConfiguredACPowerMode` / `PowerSetUserConfiguredACPowerMode` APIs.
- Power-plan sub-values bind to the exact captured scheme GUID.
- Expert writes are disabled while the full app/CLI process is elevated; machine writes require a future restricted broker.

## Rollback and integrity

- The transaction journal now persists write intent before every atomic mutation and persists read-back immediately afterward.
- Apply uses the captured before-state and refuses pre-write drift.
- Read-back mismatch triggers automatic rollback.
- Overlapping outstanding targets are rejected.
- Registry read-denied is distinct from value-absent and fails closed.
- Apply/Revert UI handlers reject concurrent work and surface errors without terminating the app.

## Scanner and parser corrections

- NVML loads only from the protected Windows system directory.
- Active-network detection requires a usable default gateway and excludes tunnel/PPP interfaces.
- PresentMon `DisplayedTime=NA` rows count as dropped presents; the drop denominator is corrected.
- Capture SHA-256 and parsing now use the same open file handle.
- The primary display is identified by desktop source position instead of path order.
- Reserved WDDM caps are no longer treated as a supported HAGS state API.
- BAR1 aperture no longer proves per-game Resizable BAR engagement.
- QPC frequency no longer proves a forced platform timer.
- SMBIOS speed/channel, raw-input message timing and managed sleep timing are explicitly heuristic rather than decision-grade.

## Verification

Run:

```powershell
.\build.ps1
```

The repository includes regression tests for PresentMon displayed drops, exclusion-plan stripping, durable write-intent ordering, apply/revert, partial rollback, drift protection and journal integrity.

# FramePath Lab v0.2.0 — release notes

FramePath Lab now provides one real system-change workflow instead of being diagnostic-only: an explicit 15-minute experiment that temporarily selects an already-installed Windows **High performance** power plan.

This is an unsigned developer preview. It does not promise lower CS2 latency, and the power-plan experiment may provide no measurable benefit on a Ryzen 7 5800X3D.

## How to use it

1. Extract the entire ZIP.
2. Run `FramePathLab.exe` normally; do not run it as administrator.
3. Open **System changes** (it is selected by default).
4. Wait for the suitability scan.
5. If **Start temporary experiment** is enabled, read the exact before/after plans and approve the change.
6. Keep the app open and repeat the same CS2 scenario or benchmark.
7. Select **Restore previous plan now**, or close the app, and confirm that restoration was verified.

The Apply button remains disabled when High performance is already active or absent, AC power is not positively detected, Group Policy restricts the operation, Remote Desktop is active, or an earlier transaction is unresolved.

## Exactly what it changes

- Changes only the current user's active power-plan pointer using Microsoft's documented `PowerSetActiveScheme` API.
- Allows only the standard High performance GUID `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` when Windows already enumerates it.
- Does not create, duplicate, edit, unhide, reset, or delete a power plan.
- Does not request elevation or use `powercfg`, PowerShell, registry writes, BCD changes, services, drivers, process manipulation, game-file writes, packet tools, or anti-cheat interaction.

Microsoft API references: [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate), [PowerSettingAccessCheck](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersettingaccesscheck), [PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme), and [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme).

## Safeguards

- Captures the exact approved original plan and rejects apply if that state changes before the write.
- Writes and flushes a bounded, checksummed recovery journal before mutation, with a validated previous-copy fallback.
- Starts and acknowledges a separate hidden rollback guardian before mutation.
- Reads the active plan back after apply and after restoration.
- Monitors guardian liveness; if the guardian stops while the UI is alive, the UI attempts immediate recovery.
- Uses compare-before-write recovery: target → restore exact original; already original → no system write; third plan → preserve the newer external selection and surface the conflict.
- Attempts recovery on explicit Restore, normal app close, owner-process loss, AC loss, lease expiry, and the next app launch after an interrupted transaction.

## Guided-only actions

- **Advanced display** opens the documented Windows Settings page; the app does not change display topology or refresh rate automatically.
- NVIDIA/CS2 guidance remains manual. The app does not write NVIDIA profiles or CS2 configuration files.

## Validation completed

- Release build: 0 warnings, 0 errors.
- Automated suite: 17/17 passed.
- Tests cover exact apply/restore with fakes, external third-plan preservation, AC and target rejection, approval drift, Group Policy rejection, guardian-arm failure, setter failure after mutation, durable journal integrity, tamper rejection, and backup recovery.
- Published Windows x64 package starts and closes normally without creating a mutation journal.
- Malformed guardian arguments fail closed with exit code 2.
- Manifest remains `asInvoker`.

Automated tests do **not** change the real Windows power plan. The real apply/guardian/rollback path has not been exercised on the user's gaming PC during packaging; the first live test therefore remains user-approved and should be treated as developer-preview validation.

## Known recovery limit

A full OS crash, power loss, or reboot can terminate both the UI and guardian after High performance was selected. Because the portable build deliberately installs no service, task, or startup entry, it cannot guarantee recovery before the next launch. Reopening FramePath Lab attempts and verifies recovery from its journal. If the recorded original plan was removed, policy blocks restoration, or Windows cannot verify it, the app surfaces the failure instead of substituting an assumed plan.


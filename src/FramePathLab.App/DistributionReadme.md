# FramePath Lab 0.5.0 — unsigned build

A Windows tuning tool for competitive FPS. It measures your specific machine, applies changes
against a verified rollback ledger, and then tells you from a capture whether they actually did
anything.

Nothing in this folder needs installing. Nothing needs .NET installed either — the runtime is
included.

## Start here

1. **Extract the whole ZIP** to a normal folder — Desktop or Documents is fine. Do not run it from
   inside the ZIP; it will not find the files next to it.
2. **Run `FramePathLab.exe`.**
3. Windows will warn you that it is from an unknown publisher, because this build is unsigned.
   Choose **More info → Run anyway**. If you would rather not, right-click the ZIP before
   extracting, choose Properties, and tick **Unblock**.
4. The app opens and performs a **read-only scan**. Nothing is written until you press an Apply
   button on a specific card.

## Run it as administrator, or don't — both work

Unelevated is fine and is the safer way to start. You get every per-user card: capture, Game Mode,
presentation, pointer, background apps.

Machine-scope cards — services, the diagnostics trace session, network adapter properties, device
disabling, boot-timer state — need an elevated session and will say so rather than failing
silently. Right-click `FramePathLab.exe` → **Run as administrator** when you want those.

Every privileged write is still checked against a compiled-in allowlist first. Elevation widens
what is reachable; it does not widen what is permitted.

## What the cards mean

Each card is one setting, showing what it is now, what it would become, and the reasoning. They
fall into five kinds:

- **Applied by default** — documented, per-user, instantly reversible, broad agreement.
- **Experiment** — a real mechanism whose value depends on your hardware and workload. Apply it,
  then measure it. Do not assume.
- **Guided** — change it in the supported Windows or vendor interface. The app points you there
  rather than writing it.
- **Diagnostic** — a reading, not a change.
- **Excluded** — checked and rejected, with the reason recorded.

## Undo

Every change captures its exact prior value into a ledger *before* writing, and reads back to
confirm afterwards. Any change can be reversed individually, and the ledger survives a crash, a
reboot, and deleting this folder.

The ledger lives in `%LOCALAPPDATA%\FramePathLab`. Deleting the app does not delete it — that would
strand applied changes with no way back.

## The command line

`FramePathLab.Cli.exe` in this folder does the same work without a window.

```
FramePathLab.Cli.exe expert                     read-only scan, as JSON
FramePathLab.Cli.exe expert --measure-input     adds the mouse report-rate probe
FramePathLab.Cli.exe expert-apply <tweak-id>    apply one card, journalled
FramePathLab.Cli.exe expert-history             list every recorded transaction
FramePathLab.Cli.exe expert-revert <id|all>     undo
FramePathLab.Cli.exe benchmark --quick          the self-contained frame benchmark
FramePathLab.Cli.exe abtest <tweak-id> --pairs 5    interleaved paired A/B on one change
FramePathLab.Cli.exe autotune --balanced --isolate  measure, apply, re-measure, keep what earned it
```

`expert` prints a lot of JSON. Redirect it: `FramePathLab.Cli.exe expert > scan.json`.

## Before you measure anything

Close what is running. A background process that comes and goes between two benchmark runs shows up
as a difference the tool will attribute to your change. Browsers are the usual culprit.

If you run a process manager that reassigns priority or affinity — Process Lasso and similar — stop
it first. It will change the thing being measured while it is being measured.

## Honest limits

- A single before/after pair is a measurement, not proof. Repeat it.
- Anything under about 2% is inside normal run-to-run variation on most machines and is reported as
  no measured change rather than as a win.
- The benchmark is a proxy for a game, not a game. It exercises the presentation path, the
  scheduler, memory latency and power behaviour faithfully. It does not reproduce a specific
  engine's shader or draw-call mix, and it never moves the mouse — so input settings are held on
  their documented behaviour rather than on a frame-rate measurement that cannot see them.

## This build is unsigned

There is no code-signing certificate on it, so SmartScreen will complain and some environments will
block it outright. Source is at https://github.com/CClintos/Frame-Path-Lab if you would rather build
it yourself.

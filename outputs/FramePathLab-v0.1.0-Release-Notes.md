# FramePath Lab 0.1.0 — developer preview

FramePath Lab 0.1.0 is the first working, read-only vertical slice from the audited roadmap.

## Run it

1. Extract `FramePathLab-v0.1.0-win-x64-unsigned.zip`.
2. Open the extracted `FramePathLab-v0.1.0-win-x64-unsigned` folder.
3. Run `FramePathLab.exe`.

The package is self-contained for Windows x64. It is not code-signed or installed, so Windows may display an unknown-publisher warning.

## Implemented

- WPF desktop interface with Overview, Scan & Evidence, Capture Analysis, History & Privacy, and Safety views.
- Read-only Windows display, refresh, power and local/remote-session scan.
- Steam-library and CS2 app-manifest/build discovery without reading or modifying CS2 configuration files.
- Selected optional overlay/recording-process observations without claiming they are a performance problem.
- Evidence cards that distinguish configuration checks, hypotheses, guided experiments and exclusions.
- Bounded PresentMon-style CSV import with SHA-256 provenance and explicit target-process selection.
- Mean/median/P95/P99 frame time, conditional P99.9, standard deviation, mean-FPS equivalent, explicit frame-budget share, CPU/GPU busy medians, present modes and tearing-field summaries.
- Local derived history with atomic replacement and confirmed deletion of owned history/backup/temp files.
- Collision-safe Markdown report export.
- Research CLI and dependency-free automated tests.

## Safety boundary

This build contains no:

- system, registry, driver, display, Steam or CS2 setting writes;
- process injection, hooks or game-memory access;
- input interception or gameplay automation;
- packet inspection or manipulation;
- priority/affinity changes;
- service, driver, overlay or background updater;
- causal Keep/Revert decision from an imported capture.

## Deliberately deferred

- Direct PresentMon execution until an exact component, hash, argument set, lost-event signal, licence path and observer-effect budget pass.
- A/A and A/B decisions until the scenario and statistical calibration gates pass.
- Verified Reflex, integrated Anti-Lag 2, VRR, HAGS or driver-profile state.
- Guided or automatic setting changes.
- Signed installer and automatic update mechanism.

## Verification

- Release build: 0 warnings, 0 errors.
- Automated tests: 8/8 passed.
- Framework-dependent WPF startup smoke test: passed.
- Published self-contained WPF startup smoke test: passed.
- Sample CSV import and real local scanner CLI paths: passed.

The included `samples/presentmon-sample.csv` is synthetic parser data and is not CS2 performance evidence.

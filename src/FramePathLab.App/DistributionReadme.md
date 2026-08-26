# FramePath Lab 0.4.0 — unsigned developer preview

This build replaces the research-first workflow with a pre-game scan that clearly separates settings that are ready, changes that are available, settings that need confirmation, and checks that are unavailable. It retains one real, explicit, bounded Windows system-change experiment with rollback safeguards. It is not a one-click optimiser and does not promise lower latency.

## Start here

1. Extract the entire ZIP to a normal folder.
2. Run `FramePathLab.exe` on Windows 11 x64. Do not run as administrator.
3. The app opens on **Pre-game** and performs a read-only scan. If an earlier approved power session was interrupted, startup first performs compare-before-write recovery of that recorded session, then scans and shows the result.
4. Review the four status counts and cards. Unknown settings are labelled **Check manually**, never guessed as disabled.
5. Use the action on one card. Supported manual actions open the correct Windows page or show the in-game checklist; return and select **Scan this PC** after changing anything.
6. If the Windows power-plan card shows **Change available**, select **Start 15-minute test** and approve the exact before/after plans. Keep FramePath Lab open, repeat the same benchmark scenario, then restore the previous plan.

If High performance is already active, missing, AC power is not positively detected, or the app is in Remote Desktop, the Apply button stays unavailable. FramePath Lab never creates or unhides a missing plan.

Implemented:

- Scan-first dashboard with readable, human status labels and direct actions.
- Explicit current value, next step, evidence, risks, and verification guidance on every card.
- Supported links to Advanced display, Game Mode, and default graphics settings.
- Safe handling of the common 59/60 Hz reporting pair without claiming an optimization opportunity.
- Local Windows/display/power/Steam/CS2 scan.
- One 15-minute switch to an already-installed Windows High performance plan.
- Exact prior-plan snapshot, durable transaction journal, apply/read-back verification, monitored independent rollback guardian, and compare-before-write restoration.
- Verified restoration attempts on explicit request, normal app exit, AC loss, or lease expiry; external third-plan changes are preserved and conflicts are shown.
- Next-launch recovery for an interrupted session.
- Guided Windows settings links and manual NVIDIA/CS2 instructions.
- Evidence and exclusion cards.
- Bounded PresentMon-style CSV import and descriptive analysis.
- Local derived history and collision-safe Markdown report export.

Important limits:

- The executable is **not code-signed or installed**. Windows may show an unknown-publisher warning.
- A full Windows crash or reboot can leave the temporary plan active until you reopen FramePath Lab. This portable build deliberately installs no service, scheduled task, or startup entry.
- High performance can increase idle power, temperature, fan noise, and energy use. Benefit on a Ryzen 7 5800X3D may be zero; measure before and after.
- Direct capture is deliberately disabled pending collector, licensing, lost-event and observer-effect qualification.
- Imported results are baseline-only. They do not establish physical mouse-to-photon latency or justify a causal Keep/Revert decision.
- No registry, driver-profile, monitor, Steam or CS2 settings are automatically changed.
- The Expert tier separately labels benchmark-only power experiments, guided checks, diagnostics, and excluded hypotheses. Security disabling, game-process affinity/EcoQoS, timer/MMCSS/quantum folklore, GPU MSI, and raw NIC/driver registry writes cannot be applied.
- No power plans are created, edited, unhidden, or deleted, and the app never elevates itself.
- No game-process injection, memory access, input automation or packet access is present.

`samples/presentmon-sample.csv` is synthetic parser test data, not performance evidence.

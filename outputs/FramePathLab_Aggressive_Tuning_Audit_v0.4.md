# FramePath Lab aggressive tuning audit — v0.4

Date: 2026-08-26  
Target: legitimate local performance and input-latency improvement for competitive FPS players

## Executive decision

The differentiator should be **deeper measurement and narrower conditional decisions**, not a larger registry pack. An obscure setting is not an advantage unless the app can prove the mechanism is engaged, reproduce a practical benefit on the target PC, detect regressions, and restore the exact prior state.

The Claude expert tier added useful inventory, but it also crossed the established product boundary. v0.4 therefore separates every candidate into default recommendation, A/B experiment, guided action, diagnostic-only, or excluded. Unsupported/security-reducing/game-process changes have no executable plan.

## Stop-ship findings corrected in v0.4

1. **Crash rollback window:** the old engine journalled every mutation as “not attempted,” performed writes, and updated the journal only afterward. A process/OS failure in that window could leave a mutation that recovery skipped. v0.4 durably records write intent before each atomic write and persists read-back immediately after it.
2. **Unverified writes:** a read-back mismatch used to remain partially applied. It now triggers automatic rollback.
3. **Power-plan identity:** processor sub-values are now bound to the exact scheme GUID captured at approval, so changing the active plan cannot make revert modify the wrong plan. Microsoft’s API identifies the scheme explicitly. ([PowerWriteACValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex))
4. **Elevated full-process risk:** expert writes are disabled when the full UI/CLI is elevated. A future machine writer must be a restricted broker that accepts allowlisted action IDs, not journal-supplied registry paths.
5. **False effective-state claims:** HAGS, ReBAR, forced platform timer, raw-input polling quality, scheduler/DPC state, memory profile/channel state, and active network route are now bounded to what their data sources can actually prove.

## Ranked additions

### Tier 1 — build next

| Rank | Addition | Why it can produce a real advantage | Product disposition |
|---:|---|---|---|
| 1 | Repeated paired benchmark engine | Turns tuning into a local decision. Run randomized ABBA blocks, trim a declared warm-up only, preserve all treatment-emergent hitches, and require a practical effect plus a confidence interval before Keep. | Core v0.5 feature |
| 2 | Managed PresentMon capture runner | Lock collector version/hash/arguments, target the correct CS2 PID/swap chain, record lost events and focus/present-mode drift, and ingest modern `DisplayedTime=NA` drops. PresentMon is the primary supported ETW frame-delivery collector. ([PresentMon](https://github.com/GameTechDev/PresentMon)) | Core v0.5 feature |
| 3 | Sustained NVIDIA telemetry synchronized to frames | Sample clocks, utilization, temperature, power, throttle reasons and PCIe state throughout warm-up and measured regions. An idle snapshot cannot diagnose load-time clock or link behavior. NVML is the official management API. ([NVML API](https://docs.nvidia.com/deploy/nvml-api/)) | Diagnostic and benchmark guardrail |
| 4 | Reflex / Boost experiment workflow | NVIDIA says CS2 Reflex Enabled reduces the render queue; On+Boost can reduce latency further at higher power and may slightly reduce FPS. The app should guide the in-game change, verify a new run, and retain Boost only with thermal headroom and a measured win. ([NVIDIA CS2 Reflex](https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/)) | Reflex Enabled guided default; Boost A/B |
| 5 | Two explicit presentation goals | “Absolute latency / tearing allowed” and “smooth low latency” have different sync/cap policies. NVIDIA documents that G-SYNC + V-SYNC + Reflex can cap below refresh while uncapped Reflex/V-SYNC-off may be slightly lower latency with tearing. ([NVIDIA latency guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/)) | Guided profiles, never a universal preset |
| 6 | Pre-game contention sampler | Sample foreground/background CPU time, disk I/O, GPU engine usage where supported, encode activity and Steam transfer state over a short window. Show actual offenders; let the user close them. Do not kill services or “debloat.” | Recommend when measured contention exists |
| 7 | WPR/ETW guided trace package | Capture CPU sampling, context switches, DPC/ISR, disk/file I/O, GPU/presentation and USB/audio providers for a short user-approved trace. WPR is the decision-grade Windows path; a sleep loop and LatencyMon headline are not substitutes. ([Windows Performance Recorder](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder), [DPC/ISR tracing example](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/example-15--measuring-dpc-isr-time)) | Advanced diagnostic |
| 8 | Per-device input collector | Use the raw-input device handle/name, an event-driven message loop, configured-rate provenance from the vendor/device where possible, and enough movement-controlled samples. Do not aggregate multiple mice or infer nominal rate from the same arrival distribution being graded. | Diagnostic until validated against hardware capture |

### Tier 2 — qualified experiments

| Candidate | Required conditions | Exact promotion experiment |
|---|---|---|
| Windows 11 AC power mode | Desktop on AC; documented `PowerGet/SetUserConfiguredACPowerMode`; temperature and clocks logged | Cooled randomized blocks: Balanced/Best performance; 5 repeated runs per arm; burst, 10-minute and 30-minute phases; Keep only if p99/p99.9 or valid latency improves beyond A/A noise without thermal/clock regression. The configured preference can be overridden by system signals. ([get](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode), [set](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode)) |
| HAGS | Supported Windows Settings surface; reboot-separated blocks; current driver recorded | HAGS off/on × Reflex off/on × CPU/GPU-bound scenarios; balanced reboot order; optical/valid PC latency plus WPR and PresentMon. Microsoft describes HAGS as a foundational scheduler change whose impact is system-dependent. ([Microsoft HAGS](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/)) |
| Windowed optimizations | DX10/11 windowed/borderless path; exact present mode recorded | Fullscreen/borderless × optimization on/off × overlay absent/present × single/mixed-refresh displays. Microsoft documents the supported Settings surface and flip-model mechanism. ([Microsoft](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11)) |
| NIC interrupt moderation | One physical default-route Ethernet adapter; vendor property resolved through `Get/Set-NetAdapterAdvancedProperty`; link reset approved | Off/on with local LAN packet timing, game-server RTT/jitter, CPU %, DPC/ISR and frame tails. Interrupt moderation trades response time for lower interrupt/CPU load; Internet transit is normally much larger than the microsecond-scale local effect. ([Microsoft network adapter performance](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/performance-in-network-adapters), [Set-NetAdapterAdvancedProperty](https://learn.microsoft.com/en-us/powershell/module/netadapter/set-netadapteradvancedproperty?view=windowsserver2025-ps)) |
| Core parking/minimum state/boost policy | Exact power scheme bound; AC only; no marginal overclock; sustained thermal telemetry | One factor at a time, randomized repeated runs, cooled blocks, CPU-bound CS2 scenario. Keep only if tails improve and active-core clock/temperature/power do not regress. | 
| AMD Curve Optimizer | Firmware/vendor tool only; board and CPU support verified; user explicitly accepts OC stability work | Per-step WHEA, memory and long mixed-load stability tests before performance A/B. AMD states that longer Curve Optimizer validation increases confidence. ([AMD Curve Optimizer FAQ](https://www.amd.com/content/dam/amd/en/documents/products/software-tools/faq-curve-optimizer.pdf)) |

## Ryzen 7 5800X3D + RTX 3080 + 32 GiB DDR4-3600 CL16

- The 5800X3D is an 8-core/16-thread, single-CCD stacked-cache CPU with 96 MiB L3. There is no second CCD or efficiency-core set for the app to select, so CS2 affinity is not a legitimate optimization for this build. AMD lists up to 4.5 GHz boost, DDR4-3200 official memory support, PCIe 4.0 and a 90 °C maximum operating temperature. ([AMD 5800X3D](https://www.amd.com/en/products/processors/desktops/ryzen/5000-series/amd-ryzen-7-5800x3d.html))
- DDR4-3600 CL16 is above the CPU’s official DDR4-3200 specification. It may be an excellent stable configuration, but the app must call it a memory overclock and check WHEA/stability rather than label it automatically optimal.
- Prefer CS2’s in-game Reflex over NVIDIA Control Panel Ultra Low Latency. NVIDIA says Reflex takes priority when both are enabled. Treat Reflex On+Boost or per-game Prefer maximum performance as measured thermal/power experiments, not universal defaults. ([NVIDIA Reflex guide](https://www.nvidia.com/en-gb/geforce/news/reflex-low-latency-platform/))
- Do not force hidden Resizable BAR profile bits. NVIDIA enables ReBAR by tested game profile because some games regress. BAR1 aperture size alone does not establish CS2 profile engagement. ([NVIDIA ReBAR](https://www.nvidia.com/en-us/geforce/news/geforce-rtx-30-series-resizable-bar-support/), [NVML BAR1 structure](https://docs.nvidia.com/deploy/nvml-api/structnvmlBAR1Memory__t.html))
- The highest-value local work for this PC is likely to be: verify active refresh/presentation; establish Reflex/cap/sync goal; prevent GPU saturation in the worst repeatable scene; find background/thermal contention; then A/B power/Boost policies. The magnitude is unknown until measured.

## Do not include

| Candidate | Decision |
|---|---|
| Memory Integrity/HVCI disable | Exclude: direct security regression. Query effective state only for context. ([Microsoft Memory Integrity](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity)) |
| `GlobalTimerResolutionRequests`, forced HPET/platform clock, `timeBeginPeriod` folklore | Exclude. Microsoft documents timer requests as per-process beginning with Windows 10 2004 and warns higher resolution can reduce overall performance and power efficiency. ([timeBeginPeriod](https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod)) |
| MMCSS Games `GPU Priority`, `SFIO Priority`, High + Priority 6 | Exclude. Microsoft says GPU Priority and SFIO Priority are not used, and High scheduling category forces Priority to 2. ([MMCSS](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service)) |
| `Win32PrioritySeparation=38`, forced process priority/affinity/EcoQoS | Exclude: no decision-grade CS2 evidence; game-process manipulation and anti-cheat/integrity boundary concerns. |
| GPU MSI registry edits | Exclude: unsupported driver mutation with device-start failure risk. Diagnose DPC/ISR through ETW instead. |
| Raw NIC class-registry writes | Exclude. Use documented NetAdapter interfaces, exact vendor property names, link-reset acknowledgement and effective-state verification. |
| Global USB selective-suspend disable | Exclude. Microsoft strongly recommends not disabling USB selective suspend globally. ([Microsoft USB selective suspend](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend)) |
| Shader-cache cleaners | Exclude from normal tuning. Preserve/warm caches; only offer a supported reset as a corruption diagnostic. |
| Debloat/service removal, Defender/VBS/security shutdown, BCD packs, packet manipulation, game config/file patching, memory/process tampering, input automation | Exclude categorically. |

## Benchmark acceptance gate

No candidate earns **Keep** unless all conditions pass:

1. Collector, game, OS, driver and setting versions are recorded.
2. Correct CS2 PID/swap chain, focus, resolution, refresh and present mode remain stable.
3. At least one A/A calibration establishes normal run-to-run noise.
4. A/B order is randomized or balanced (ABBA/BAAB), with at least three and preferably five runs per arm.
5. The scenario is repeatable; warm-up and measured windows are declared before results are viewed.
6. Report per-run medians and paired deltas, confidence interval/effect size, p95/p99/p99.9, displayed drops, valid latency fields, clocks, temperature, power and background contention.
7. Lost ETW events, focus loss, wrong swap chain, reset/crash or precondition drift invalidate the run. Treatment-caused heat, throttling or hitches remain negative results.
8. A practical improvement must exceed A/A noise and no safety, stability, frame-pacing or thermal guardrail may regress.

This is how FramePath Lab can become more aggressive than generic optimizers while remaining credible: it can test more deeply, reject more false positives, and keep only changes that win on the player’s actual machine.

# Counter-Strike 2 local-latency and frame-consistency app

## Evidence-backed research and product scope — 25 August 2026

**Status:** research and scope only; no application has been built.

**Product thesis:** build a local experiment manager, not a one-click “optimizer.” It should discover a machine’s actual state, explain one causal hypothesis at a time, run a controlled A/B test, apply only supported and reversible changes after approval, and say **insufficient evidence** whenever the result is noisy or conditional. It must never promise rank, aim, reaction-time, networking, or competitive advantage.

Valve’s current CS2 guidance calls highest refresh plus G-SYNC, V-SYNC, and Reflex together the usually smoothest and lowest-input-latency combination on compatible NVIDIA systems. NVIDIA’s older generic latency guide says Reflex with uncapped FPS and V-SYNC off can be slightly lower latency when tearing is acceptable. Those comparative claims conflict at the margin and optimize different user objectives; the winner on current CS2 is **insufficient evidence without optical A/B on the target system**. The app should therefore expose **Smooth low latency** and **Absolute latency / tearing allowed** as separate goals. ([Valve CS2 video recommendations](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032), [NVIDIA system-latency guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/))

The app can measure frame delivery and presentation with ETW/PresentMon, and on supported systems it can collect instrumented PC latency. It cannot infer true mouse-to-photon latency from average FPS or software frame telemetry: FrameView’s PC-latency interval excludes the mouse and physical display, whereas LDAT/Reflex Analyzer-class optical hardware includes those parts of the path. ([PresentMon](https://github.com/GameTechDev/PresentMon), [FrameView 1.7 guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-7-user-guide-web-version.pdf), [NVIDIA LDAT](https://developer.nvidia.com/nvidia-latency-display-analysis-tool))

### Claim and evidence rules

- **Strong:** a current primary source documents the mechanism/support and the effect can be reproduced with an appropriate measurement. “Strong” does not make a vendor’s best-case magnitude universal.
- **Moderate:** the mechanism is credible but the size or sign depends materially on hardware, load, driver, presentation path, or vendor-controlled results.
- **Weak:** plausible mechanism with little current CS2-specific reproducible evidence.
- **Disproven:** the claimed mechanism conflicts with current platform documentation, or the intervention is known to create a different problem without demonstrated CS2 benefit.
- **Insufficient evidence:** the correct result when the applicable source, safe control, repeatability, or causal measurement is missing.

Vendor percentage claims are labelled as such. Product recommendations and thresholds below are design inferences, not claims that the cited source has endorsed this app.

YouTube videos, Reddit posts, community Steam guides, generic “optimization” sites, and influencer settings may generate a testable hypothesis, but they do not raise an evidence grade and are not cited as support here.

---

# A. Ranked tweak catalogue

Every entry uses the requested seven-part decision record. The rank reflects expected usefulness, evidence, reach, reversibility, and risk—not a promised effect size.

| Rank | Candidate | Evidence summary | Product decision |
|---:|---|---|---|
| 1 | Highest stable advertised refresh | Strong | Recommend by default when misconfigured |
| 2 | NVIDIA Reflex Enabled | Strong mechanism/support; moderate magnitude | Recommend by default when supported |
| 3 | G-SYNC + V-SYNC + Reflex | Strong support/behaviour; comparative latency moderate | Recommend for **Smooth low latency** goal |
| 4 | AMD Anti-Lag 2, in-game only | Moderate magnitude; strong current integration | Recommend when exposed and supported |
| 5 | VRR / FreeSync / G-SYNC | Strong cadence mechanism; latency magnitude conditional | Recommend for smooth goal; verify visually |
| 6 | Lower a measured GPU-heavy video setting | Strong mechanism; setting-specific | Opt-in experiment |
| 7 | FPS cap/limiter selection | Moderate; no universal rate | Valve profile default; otherwise experiment |
| 8 | V-SYNC off/uncapped or Fast/Enhanced Sync | Strong trade-off for V-SYNC off; weaker sync variants | V-SYNC off for tearing-allowed goal; variants experimental |
| 9 | Preserve/warm shader cache | Strong | Recommend default; exclude routine clearing |
| 10 | Game Mode | Stale platform mechanism; current CS2 benefit insufficient | Leave current/default state; opt-in A/B |
| 11 | Presentation/fullscreen/windowed path | Strong platform mechanism; CS2 conditional | Leave defaults; opt-in A/B |
| 12 | Correct hybrid GPU and scoped driver profile | Strong when wrong adapter; weak blanket overrides | Correct adapter by default; otherwise isolated tests |
| 13 | Measured background/overlay/capture contention | Moderate and conditional | Opt-in only when observed |
| 14 | Windows Best performance power mode | Moderate and machine-dependent | AC-only opt-in experiment |
| 15 | Reflex Boost / maximum-performance clock policy | Moderate | Opt-in experiment with thermal soak |
| 16 | Mouse polling above 1 kHz | Moderate mechanism; end-to-end uncertain | Vendor-supported opt-in experiment |
| 17 | HAGS | Mechanism documented; CS2 result insufficient | Rebooted opt-in experiment |
| 18 | DPC/ISR and audio/USB remediation | Strong diagnosis; weak generic tweak | Expert, trace-triggered experiment |
| 19 | CS2 Streamlined Push to Talk | Strong supported mechanism; current magnitude conditional | Opt-in for push-to-talk users |
| 20 | CS2 packet-loss/jitter buffering | Strong trade-off mechanism; benefit network-dependent | Opt-in only after missed-tick evidence |
| 21 | Driver/firmware update for relevant fix | Moderate and version-specific | Guided; never auto-update |
| 22 | NIC interrupt moderation | Weak CS2 net-benefit evidence | Exclude from v1; research only |
| 23 | Monitor overdrive/low-latency OSD | Moderate, model-specific | Guided experiment only |
| 24 | Launch/config “FPS packs” | Weak/unsupported; current benefit insufficient | Exclude |

The documented in-game, driver, Windows, and monitor controls above are ordinary local configuration paths, not anti-cheat bypasses. That does not amount to permanent Valve certification: the product still needs version-specific smoke testing, and anything involving injection, memory/process tampering, game-file modification beyond supported controls, packet manipulation, or generated gameplay input is a hard exclusion from the shipped product, gameplay client, and every public/secure match. F.22’s separately governed, protocol-agnostic link-impairment study is isolated private-server laboratory instrumentation outside the product—not a user tweak—and is omitted unless legal/game-integrity review approves that boundary. ([Valve VAC policy](https://help.steampowered.com/en/faqs/view/571A-97DA-70E9-FF74), [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/))

## 1. Use the highest stable, manufacturer-advertised refresh mode at the intended resolution

1. **Latency path:** display opportunity and scan-out. A higher active refresh rate shortens the refresh interval; panel processing and pixel response remain separate components of display latency. Valve tells CS2 players to verify and use their monitor’s highest supported refresh, while RTINGS’ optical method shows why input-lag measurements must specify refresh and screen position. ([Valve](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032), [Microsoft refresh-rate settings](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-the-refresh-rate-on-your-monitor-in-windows), [RTINGS methodology](https://www.rtings.com/monitor/tests/inputs/input-lag))
2. **Expected benefit and conditions:** a real benefit when Windows or CS2 is accidentally using a lower mode. It requires a cable, port, GPU, resolution, bit depth, and monitor mode that support the selected rate. Dynamic Refresh Rate can limit some games’ maximum rate; Microsoft advises disabling DRR if that occurs. ([Microsoft](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-the-refresh-rate-on-your-monitor-in-windows))
3. **Evidence:** **Strong** for correct/high refresh; monitor-specific magnitude beyond the interval arithmetic remains conditional.
4. **Downsides/failures:** a different mode can change HDR, bit depth, chroma, VRR range, scaling, overdrive behaviour, power, or link stability. Custom timings, EDID overrides, and display overclocking are out of scope.
5. **Decision:** **Recommend by default**, but only from modes already enumerated by Windows/the driver and only when the current mode is lower without an explicit user reason.
6. **Safe lifecycle:** detect active paths, target names, modes, and virtual-refresh state through Windows’ documented display APIs; snapshot the complete active topology. Before applying, arm a separate signed ephemeral watchdog holding the prior topology; it reverts on 15-second timeout, UI/broker death, or lost confirmation, then exits. Validate an enumerated mode first, apply it temporarily, re-query it, and save only after approval. `QueryDisplayConfig` and `SetDisplayConfig` explicitly support query, validation, temporary application, and restoring the database configuration. ([QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig), [SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig))
7. **Before/after test:** same resolution and graphics state, current Hz versus highest stable Hz, randomized paired blocks. Capture PresentMon displayed cadence and dropped frames; use at least 200 fixed-position optical events for a shipping end-to-end claim. Repeat near the top, middle, and bottom of the screen in lab validation because scan-out position matters.

## 2. Enable NVIDIA Reflex in CS2 on supported GeForce systems

1. **Latency path:** the game/render queue. Reflex is integrated into the engine and schedules CPU work just in time for GPU submission, reducing queued work/back-pressure. ([NVIDIA Reflex SDK](https://developer.nvidia.com/performance-rendering-tools/reflex), [NVIDIA CS2 article](https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/))
2. **Expected benefit and conditions:** largest when the GPU path is saturated; it can be small when the workload is already CPU-bound and has little queue. NVIDIA reports up to 35% in its CS2 configurations; that is a vendor best case, not an expectation for every PC. ([NVIDIA CS2 article](https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/))
3. **Evidence:** **Strong** for support and mechanism; **moderate** for magnitude because the published CS2 numbers are vendor-controlled and configuration-specific.
4. **Downsides/failures:** usually small, but it can slightly affect throughput; “Enabled + Boost” has separate power/thermal costs covered at rank 15. Driver Low Latency Mode is redundant when Reflex is active because NVIDIA says Reflex takes precedence. ([NVIDIA guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/))
5. **Decision:** **Recommend by default** on supported NVIDIA hardware. Do not stack the driver Ultra Low Latency setting.
6. **Safe lifecycle:** detect NVIDIA GPU/driver and ask the user to confirm the CS2 setting. V1 should guide the supported in-game UI rather than modify CS2 files; record the exact prior selection, require CS2 restart when indicated, verify the setting after restart and record the effective latency-marker availability. Revert through the same UI.
7. **Before/after test:** Reflex Off versus Enabled in one deliberately GPU-bound and one CPU-bound local scene; five randomized paired blocks per scene after A/A qualification. Primary: valid FrameView/PresentMon PC latency when exposed; guardrails: p99 frame time, drops, average throughput, clocks, temperature. Optical validation is required for an end-to-end claim.

## 3. Offer Valve’s G-SYNC + V-SYNC + Reflex profile as “Smooth low latency”

1. **Latency path:** queue timing, refresh synchronization, and displayed cadence. Valve explains that Reflex places the sleep before input sampling, G-SYNC follows the effective frame rate, and the three-way profile limits FPS slightly below refresh to avoid tearing and missed-refresh microstutter. ([Valve CS2 video recommendations](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032))
2. **Expected benefit and conditions:** smoother tear-free delivery with low latency when the NVIDIA GPU, compatible display, VRR path, V-SYNC, and Reflex are all actually active. It is not the absolute lowest-latency choice for every user; NVIDIA says uncapped Reflex/V-SYNC-off can be slightly lower if tearing is acceptable. ([NVIDIA guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/))
3. **Evidence:** **Strong** and CS2-specific for support, intended behaviour, and the below-refresh/tear-free profile; **moderate** for comparative latency because Valve’s wording and NVIDIA’s older generic guide differ. The target-system winner is **insufficient evidence** until optical A/B.
4. **Downsides/failures:** the intentional below-refresh cap reduces throughput relative to uncapped play; VRR can show model-specific brightness flicker or visual glitches, and Valve notes G-SYNC can glitch in non-fullscreen-windowed configurations on some systems. ([Valve](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032))
5. **Decision:** **Recommend by default** only when the user selects the smooth/tear-free goal and all prerequisites are detected. Always expose the latency-first alternative.
6. **Safe lifecycle:** inventory active display, G-SYNC capability/configuration, V-SYNC, Reflex, refresh, renderer, and presentation mode. Use CS2’s own recommendation/UI and documented NVIDIA per-game controls; never edit opaque profile blobs. Snapshot every component separately, restart CS2, verify the configured state and that delivered FPS remains below the ceiling, and restore each exact prior value on rollback. Capability, configured state, and actual panel engagement are distinct: `AllowsTearing`, EDID, and FPS cannot alone prove variable refresh. Require a vendor indicator/monitor confirmation or optical validation; otherwise report **configured, engagement unverified**. NVIDIA exposes a documented Driver Settings API, but only documented setting IDs should ever be used. ([NVAPI DRS](https://docs.nvidia.com/nvapi/group__drsapi.html), [Valve setup guidance](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032))
7. **Before/after test:** compare the complete Valve profile with Reflex-on/V-SYNC-off/uncapped in steady, near-ceiling, and fluctuating scenes. Report PC latency, p95/p99 displayed time, drops, tear observations, and optical latency; do not compare average FPS alone.

## 4. Enable AMD Radeon Anti-Lag 2 only through CS2’s developer-integrated control

1. **Latency path:** engine-aware CPU/GPU pacing. AMD distinguishes developer-integrated Anti-Lag 2 from a generic driver-only limiter, and Valve added CS2 support in May 2024. ([AMD Anti-Lag](https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html), [Valve update](https://store.steampowered.com/news/app/730/view/4177730135013203179))
2. **Expected benefit and conditions:** most plausible in GPU-heavy scenes on supported RDNA hardware/current drivers. AMD’s published reductions are vendor measurements tied to listed resolutions and systems; no universal percentage should appear in the product. ([AMD 24.6.1 notes](https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-24-6-1.html))
3. **Evidence:** **Moderate**: current game integration is strong evidence of support, while independent current-build magnitude is insufficient.
4. **Downsides/failures:** unsupported hardware/driver/API combinations may not expose it. Never substitute the retired driver-level Anti-Lag+ path: Valve said the 2023 driver feature detoured engine DLL functions, later added an incompatible-driver check, and reversed affected bans. Current developer-integrated Anti-Lag 2 is a distinct feature. ([Valve 13 October 2023 statement](https://x.com/CounterStrike/status/1712875606776729832), [Valve follow-up](https://steamcommunity.com/games/CSGO/announcements/detail/3717217413231124447))
5. **Decision:** **Recommend by default** only when the current in-game Anti-Lag 2 control and supported hardware/driver are present. **Exclude** legacy Anti-Lag+ and any injected/detoured implementation.
6. **Safe lifecycle:** detect AMD architecture and driver, then use a guided in-game change; do not write driver databases or game files. Record the user-confirmed prior state, restart if required, verify the exposed control and capture-marker availability, and guide exact restoration.
7. **Before/after test:** Off versus On in matched GPU-heavy and CPU-heavy local scenes, randomized blocks. Use frame pacing and any valid instrumented PC-latency field, plus optical hardware for a click-to-photon claim. Reject if p99 pacing or stability regresses even when mean FPS is unchanged.

## 5. Enable VRR when supported, with separate NVIDIA and AMD policy

1. **Latency path:** display scheduling and cadence. Adaptive refresh changes the display refresh timing to follow delivered frames inside its operating range, reducing fixed-refresh tearing/judder; Valve recommends G-SYNC or FreeSync when available. ([Valve](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032), [AMD FreeSync setup](https://www.amd.com/en/resources/support-articles/faqs/DH3-013.html), [VESA Adaptive-Sync CTS](https://vesa.org/wp-content/uploads/2022/05/Adaptive-Sync-Display-CTS-r1.0.pdf))
2. **Expected benefit and conditions:** strongest for consistency when FPS varies inside the display’s VRR range. NVIDIA’s CS2 policy is rank 3; AMD advises a cap or V-SYNC when FPS exceeds the refresh range, but the best CS2 combination needs local testing. ([AMD FreeSync](https://www.amd.com/en/products/graphics/technologies/freesync.html))
3. **Evidence:** **Strong** for tearing/cadence mechanism; **moderate/insufficient** for a universal CS2 latency delta, especially on AMD.
4. **Downsides/failures:** possible brightness flicker, low-frame-rate-compensation transitions, multi-monitor/presentation incompatibilities, or no effect above the ceiling. Monitor OSD and driver state can disagree.
5. **Decision:** **Recommend by default** for the smooth goal after compatibility checks; **offer as an experiment** when visual artefacts or an absolute-latency goal dominate.
6. **Safe lifecycle:** detect active output, EDID capability, advertised range where available, configured driver state, renderer, and presentation mode. NVIDIA changes may use documented per-game APIs; AMD and monitor OSD changes remain guided until a stable public API exists. Record capability and configuration separately; use a vendor indicator/monitor confirmation or optical validation for actual engagement. `AllowsTearing`, delivered FPS, or EDID alone is not proof; otherwise report **configured, engagement unverified**. Roll back component-by-component.
7. **Before/after test:** VRR off/on at steady mid-range, just below/above ceiling, near floor, and rapidly changing FPS. Record p95/p99 displayed time, tearing/flicker observations, drops, and optical latency.

## 6. Reduce GPU-heavy CS2 video settings only when telemetry shows a GPU bottleneck

1. **Latency path:** raw GPU render time and queue pressure. Reducing resolution, antialiasing, or another GPU-heavy effect can reduce render latency when GPU work is the limiter. NVIDIA recommends lowering settings as a latency lever; AMD documents the throughput/quality trade-offs of per-game graphics controls. ([NVIDIA guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/), [AMD graphics settings](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html))
2. **Expected benefit and conditions:** benefit requires sustained GPU limitation or queueing in the representative CS2 scene. In a CPU-bound scene, “all low” may add no useful benefit. Exact CS2 per-setting effects are **insufficient evidence** until measured on the target build and PC.
3. **Evidence:** **Strong** mechanism; **moderate** for a measured one-setting change; **insufficient** for a universal preset.
4. **Downsides/failures:** reduced image clarity and competitive information, changed scaling, aliasing, or no gain. Shadow and visual settings can affect information visibility; Valve has changed CS2’s shadow options over time, so copied presets age poorly. ([Valve June 2024 update](https://store.steampowered.com/news/app/730/view/4182235001971902353))
5. **Decision:** **Offer as an opt-in experiment**. Never recommend “everything Low” and never silently alter resolution.
6. **Safe lifecycle:** read only supported visible values, show screenshots/quality implications, change one setting through CS2’s UI, record the prior value, restart/warm shaders when required, verify in UI and capture metadata, then restore exactly. V1 should not edit `cfg` files.
7. **Before/after test:** one factor at a time in a heavy-smoke/utility scene and a representative steady scene after at least three warm-up passes. Primary: valid PC latency or GPU busy/render time; guardrails: p99 frame time, drops, visibility screenshot, VRAM, temperature, and user rejection.

## 7. Choose an FPS-cap strategy from measurement; do not ship a universal number

1. **Latency path:** simulation/render opportunity, render queue, GPU saturation, power, and VRR ceiling. A cap can prevent GPU saturation and keep delivery inside VRR range, while a cap set too low increases time between render opportunities. NVIDIA and AMD expose per-game frame-rate controls, and Valve’s G-SYNC/V-SYNC/Reflex profile deliberately caps slightly below refresh. ([NVIDIA Control Panel reference](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/3D%20Settings/Manage_3D_Settings_%28reference%29.htm), [AMD profiles](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html), [Valve](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032))
2. **Expected benefit and conditions:** a useful cap may improve tail pacing, power, and latency when uncapped play pins the GPU or crosses a VRR ceiling. The best rate and limiter depend on workload, refresh, sync path, and cap implementation; “refresh minus three” is not a universal law.
3. **Evidence:** **Moderate** for the mechanism; **insufficient evidence** for one number or one limiter on all systems.
4. **Downsides/failures:** a low cap reduces throughput and can increase latency; a cap based on average FPS may collapse in heavy fights; engine and driver limiters can pace differently. CS2 has previously fixed behaviour that occurred only at very high FPS, another reason to validate extremes rather than assume “unlimited” is always benign. ([Valve July 2025 update](https://store.steampowered.com/news/app/730/view/529853754800865283))
5. **Decision:** Valve’s automatic NVIDIA smooth-profile cap is **Recommend by default** with that goal. All other caps are **opt-in experiments**.
6. **Safe lifecycle:** detect refresh/VRR range, current limiter(s), delivered FPS distribution, and GPU saturation. Prevent stacked limiters. V1 guides CS2’s supported control first; documented driver per-game controls are secondary. Record absent/inherited/explicit values distinctly, verify actual delivered-rate distribution, and restore exact prior state.
7. **Before/after test:** compare uncapped, Valve automatic profile, a measured below-ceiling candidate, and a sustainable-heavy-scene candidate. Randomize blocks; include a thermal-soak run. Judge valid PCL/optical latency together with median/p95/p99/p99.9 frame time and drops—not average FPS alone.

## 8. Offer V-SYNC-off/uncapped as “Absolute latency, tearing allowed”; treat Fast/Enhanced Sync separately

1. **Latency path:** removes fixed-refresh wait/back-pressure. NVIDIA documents uncapped Reflex with V-SYNC off as the lowest-latency option when tearing is acceptable; Fast Sync and AMD Enhanced Sync are different policies intended to reduce tearing above refresh with less latency than conventional V-SYNC. ([NVIDIA guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/), [NVIDIA V-SYNC modes](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nv3dENG/Manage_3D_Settings_%28reference%29.htm), [AMD Enhanced Sync](https://www.amd.com/en/products/software/adrenalin/software-enhancedsync.html))
2. **Expected benefit and conditions:** potentially lowest local latency with Reflex and enough FPS, at the cost of tearing. Fast/Enhanced Sync works best only in its intended above-refresh regime; current CS2-specific superiority is **insufficient evidence**.
3. **Evidence:** **Strong** for the V-SYNC-off mechanism/trade-off; **moderate vendor evidence** for its current-CS2 advantage over Valve’s smooth profile; **moderate/weak** for Fast/Enhanced Sync as a CS2 recommendation.
4. **Downsides/failures:** visible tears, uneven cadence, power/heat from uncapped rendering, or sync-mode stutter around the refresh boundary.
5. **Decision:** V-SYNC off is **Recommend by default only for the explicit tearing-allowed goal**. Fast/Enhanced Sync is **opt-in experiment**.
6. **Safe lifecycle:** isolate the change to CS2, detect stacked caps and VRR, preserve driver inheritance and game values separately, verify `AllowsTearing`, present mode, delivered rate, and visual outcome, then restore exact values.
7. **Before/after test:** compare below, near, and well above refresh. Use high-speed/optical capture to score tearing and latency, plus p99 displayed time and drops; reject a nominal latency win if the user’s predeclared visual guardrail fails.

## 9. Preserve and warm the shader cache; do not clear it as routine maintenance

1. **Latency path:** shader compilation stalls in the render path. NVIDIA identifies shader compilation as a common stutter source, says the cache avoids repeat compilation, and notes that driver installation clears it; a too-small cache can evict older shaders. ([NVIDIA shader-cache reference](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nv3dENG/Manage_3D_Settings_%28reference%29.htm))
2. **Expected benefit and conditions:** keeping the vendor default/enabled cache and warming a newly updated game/driver makes repeat-run frame pacing more representative. It does not eliminate every stutter, and the number of passes needed is **insufficient evidence** until measured for the current build.
3. **Evidence:** **Strong** that routine deletion creates cold compilation work; **insufficient evidence** that cache clearing is a general optimization.
4. **Downsides/failures:** cache consumes disk. Clearing it makes the first runs predictably non-comparable and can worsen hitching; an unlimited cache can consume excessive storage.
5. **Decision:** **Recommend by default:** leave it enabled/vendor-default and warm before testing. **Exclude:** scheduled purges. Reset is diagnostic-only for credible corruption.
6. **Safe lifecycle:** detect vendor setting, free space, recent driver/game update, and cold/warm run status. V1 never deletes undocumented cache directories. If a vendor-supported reset is later offered, explain that rollback cannot reconstruct evicted compiled data, require explicit consent, then compare multiple warm runs against a preserved baseline.
7. **Before/after test:** ten consecutive identical runs after game update, driver update, and—in a lab only—supported reset. Plot hitch count and p99/p99.9 versus run number; establish a stabilization rule before using later runs for any other tweak.

## 10. Leave Game Mode at the current Windows/default state; offer a controlled A/B

1. **Latency path:** resource contention and scheduling consistency. Microsoft describes Game Mode as prioritizing game access to resources, with benefit related to the amount and impact of competing activity; its developer APIs are deprecated, so the app should treat the current user setting as a platform policy rather than call old APIs. ([Microsoft Game Mode documentation](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/gamemode/game-mode-portal))
2. **Expected benefit and conditions:** most plausible when meaningful background activity competes with CS2; little change is expected on an already clean system.
3. **Evidence:** **Moderate** for the historical platform mechanism, but the cited public page is from Microsoft’s “previous versions” documentation and its developer APIs are deprecated. Current Windows 11/CS2 benefit is **insufficient evidence**.
4. **Downsides/failures:** a specific driver/app combination could regress, and background applications may receive fewer resources. Game Mode is not a substitute for diagnosing an actual offender.
5. **Decision:** **Leave the user’s current/Windows-default state. Offer an opt-in A/B experiment.** Do not turn it on merely because the scan found it off.
6. **Safe lifecycle:** read state, deep-link the documented `ms-settings:gaming-gamemode` page, record prior state, ask the user to toggle, restart CS2, verify the state and foreground focus, and guide restoration. Microsoft documents the Settings URI. ([Windows Settings URI scheme](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings))
7. **Before/after test:** On/off with a clean baseline and a separately defined realistic background workload. Randomized blocks; compare p99 frame/displayed time, hitch count, CPU wait, and process CPU/disk activity. Do not manufacture background load during a live match.

## 11. Leave Fullscreen Optimizations at default; benchmark actual fullscreen/borderless presentation paths

1. **Latency path:** DXGI presentation, Desktop Window Manager composition, independent/direct flip, and overlays. Windows 11’s windowed-game optimization moves eligible DX10/11 windowed/borderless games from legacy blt to flip model, which Microsoft says reduces frame latency and enables VRR; modern flip paths can become effectively equivalent to exclusive fullscreen. ([Windows 11 windowed optimizations](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11), [DXGI flip model](https://devblogs.microsoft.com/directx/dxgi-flip-model/), [Fullscreen Optimizations](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/))
2. **Expected benefit and conditions:** enabling windowed optimization can help an eligible DX10/11 borderless path still using blt. The winning CS2 mode depends on renderer, OS build, GPU routing, multi-monitor state, VRR, and overlays; the menu label alone is not evidence.
3. **Evidence:** **Strong** platform mechanism; **moderate** CS2 applicability; no universal fullscreen winner.
4. **Downsides/failures:** overlays or another top-level window can break independent flip; borderless and exclusive have different Alt-Tab/compatibility behaviour; Auto HDR can couple to the Windows setting. ([Microsoft](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11))
5. **Decision:** **Leave Windows defaults. Offer an opt-in A/B experiment. Exclude “Disable fullscreen optimizations globally.”**
6. **Safe lifecycle:** detect OS build, CS2 renderer, selected mode, monitor topology, per-app Windows option, and actual PresentMon `PresentMode`/tearing rather than assuming. Use the documented per-app Settings control or a guided UI; snapshot coupled Auto HDR/graphics-preference state and restore it exactly.
7. **Before/after test:** exclusive/fullscreen versus borderless plus windowed optimization, overlays absent and then present, on single- and multi-monitor configurations where applicable. Compare actual present-mode distribution, PC/optical latency, p99 displayed time, drops, and Alt-Tab stability.

## 12. Select the high-performance GPU on hybrid systems; keep driver changes per-game and attributable

1. **Latency path:** render-device choice, copy/presentation path, and GPU performance state. Windows exposes per-app high-performance GPU selection on multi-GPU PCs; NVIDIA and AMD document application profiles so a CS2 experiment need not alter global behaviour. ([Windows graphics preference](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11), [NVIDIA 3D settings](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/3D%20Settings/Manage_3D_Settings_%28reference%29.htm), [AMD application profiles](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html))
2. **Expected benefit and conditions:** high-performance selection can materially help if CS2 is mistakenly using an integrated/low-power GPU. Otherwise, “global esports profiles” have no established advantage.
3. **Evidence:** **Strong** for correcting the wrong adapter; **weak/insufficient** for generic blanket driver overrides.
4. **Downsides/failures:** higher laptop power/heat, a changed display-copy path, profile conflicts, reduced image quality, and hard-to-attribute bundles. AMD says default settings give most users the best image/performance balance; Radeon Chill dynamically changes frame rate and is not interoperable with Anti-Lag, so it is not a drop-in CS2 latency control. ([AMD profiles](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html), [AMD Anti-Lag/Chill configuration](https://www.amd.com/en/resources/support-articles/faqs/DH3-033.html))
5. **Decision:** **Recommend by default** when the measured adapter is wrong. **Offer one documented per-game setting at a time. Exclude global presets, HYPR-RX bundles, Radeon Chill/Boost/RSR as latency defaults, and AFMF/frame generation.** A frame limiter can still be tested separately at rank 7.
6. **Safe lifecycle:** inventory physical/render/display adapters, profile inheritance, and current per-app Windows preference. For NVIDIA, use only documented NVAPI DRS settings and save/restore the exact location/value; for AMD or unsupported controls, guide its UI. Verify the actual adapter in telemetry. Never edit opaque registry/profile blobs.
7. **Before/after test:** wrong versus high-performance adapter only when both paths work, then isolated driver-setting A/B tests. Compare presentation mode, copy/display latency, p99 frame time, power, thermals, and stability over a 20-minute soak.

## 13. Remove only measured background, overlay, recording, or download contention

1. **Latency path:** CPU scheduling, GPU composition/capture, disk/network I/O, memory pressure, and injected overlay presentation. Microsoft documents that background activity consumes resources; its fullscreen-optimization article notes that overlays can affect the presentation path. FrameView 1.9 additionally warns its newer overlay can force a game out of independent flip when multiplane overlay support is unavailable. ([Microsoft background activity](https://support.microsoft.com/en-US/Windows/Experience/Performance-Optimization/manage-background-activity-for-apps-in-windows), [Microsoft FSO](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/), [FrameView release notes](https://www.nvidia.com/en-au/geforce/technologies/frameview/release-notes/))
2. **Expected benefit and conditions:** only when the scan or A/A run observes a process causing CPU, GPU, disk, capture, overlay, update, or thermal contention. A dormant app is not a performance problem merely because it exists.
3. **Evidence:** **Moderate** and condition-specific. Generic service removal is **unsupported**.
4. **Downsides/failures:** closing software can lose unsaved work, communication, accessibility, capture, purchases, or security functions. Steam documents the Overlay as providing in-game features, so disabling it has functional costs. ([Steam Overlay support](https://help.steampowered.com/en/faqs/view/3978-072C-18DF-FBF9))
5. **Decision:** **Offer as an opt-in experiment only for observed contenders. Exclude automatic killing, uninstalling, service disabling, and “debloat.”**
6. **Safe lifecycle:** show per-process evidence and owner/publisher; never terminate automatically. Let the user close or pause one app through its own UI, record its running/capture/overlay state, verify it is absent during the measured window, and restore by relaunching or re-enabling. Background recording is an independent experiment, not a default assumption; Microsoft confirms it is a continuous capture feature when enabled. ([Microsoft Game Capture](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/xappcapture/functions/xappcapturerecorddiagnosticclip))
7. **Before/after test:** sequentially isolate each identified overlay/recorder/downloader with headless capture only. Measure p99/p99.9, hitch correlation, present mode, lost frames, and resource traces. A clean-boot procedure is diagnostic evidence only, not a permanent configuration. ([Microsoft clean boot](https://support.microsoft.com/en-US/Windows/Experience/Startup-Boot/how-to-perform-a-clean-boot-in-windows))

## 14. Test Windows “Best performance” power mode only on AC and only when clocks are part of the problem

1. **Latency path:** CPU frequency/ramp behaviour and power budgets. Microsoft says Best performance can help the CPU run at higher performance, while increasing power use, temperature, and laptop battery drain. ([Microsoft performance guidance](https://support.microsoft.com/en-US/Windows/Experience/Performance-Optimization/tips-to-improve-pc-performance-in-windows))
2. **Expected benefit and conditions:** plausible when telemetry shows clock-ramp or power-policy stalls and sufficient thermal headroom. It may do nothing on a desktop already boosting correctly, or worsen sustained performance if heat causes throttling.
3. **Evidence:** **Moderate** and machine-dependent; “Ultimate Performance always lowers CS2 latency” has **insufficient evidence**.
4. **Downsides/failures:** heat, fan noise, energy cost, battery drain, and possible thermal throttling; hidden processor-state edits can be unstable or counterproductive.
5. **Decision:** **Offer as an opt-in experiment**, AC-only by default. Exclude hidden-plan imports and minimum-CPU-state registry recipes.
6. **Safe lifecycle:** detect AC/battery, the active scheme GUID, modern Windows power mode, clocks, and power/thermal limits. Use Windows 11’s documented `PowerGetUserConfiguredACPowerMode`/`PowerSetUserConfiguredACPowerMode` APIs for the modern AC mode; use `powercfg /setactive` only for a separately labelled power-scheme experiment. Do not conflate the dropdown with a scheme. Record both states, verify sensor response, and restore the exact prior GUID/mode. ([Power-mode read API](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powergetuserconfiguredacpowermode), [write API](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetuserconfiguredacpowermode), [`powercfg`](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options))
7. **Before/after test:** Balanced versus Best performance after equal warm-up, short burst and at least 20-minute soak. Primary: PC/optical latency or CPU frame time; guardrails: p99, temperature, throttling, fan/power, and battery state.

## 15. Test Reflex Boost or NVIDIA “Prefer maximum performance” only when transient clocks are implicated

1. **Latency path:** GPU clock residency. NVIDIA says Reflex Boost keeps GPU clocks higher and can shave latency when the GPU is underutilized; its CS2 page says Boost uses more power and can slightly reduce FPS. ([NVIDIA Reflex](https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/), [NVIDIA CS2](https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/))
2. **Expected benefit and conditions:** potentially small when GPU work arrives in bursts at low utilization and clocks down between frames; little reason to expect a gain when clocks are already stable.
3. **Evidence:** **Moderate** mechanism/vendor evidence; target-PC magnitude is uncertain.
4. **Downsides/failures:** higher power, temperature, noise, battery drain, lower FPS, or worse sustained pacing after thermal soak.
5. **Decision:** **Offer as an opt-in experiment**, per-game and preferably mains-powered. Do not stack Boost and unrelated power flags without a separate bundle test.
6. **Safe lifecycle:** detect power source, clock residency, utilization, thermal headroom, Reflex state, and existing profile. Prefer the in-game Boost option; if a driver policy is tested, use documented per-game NVAPI, preserving inherited/explicit state. Verify clocks and thermals, then exact revert.
7. **Before/after test:** Enabled versus Enabled + Boost, plus normal versus maximum-performance policy only if needed. Include bursty low-load and sustained high-load scenes, then a 20-minute soak. Keep only if valid latency improves without a predeclared p99/thermal/power regression.

## 16. Preserve the vendor-supported mouse default; benchmark alternative polling rates rather than assuming “higher is always better”

1. **Latency path:** device report interval and Windows raw-input CPU/DPC load. A 1 kHz mouse reports at up to 1 ms intervals while an 8 kHz mode can report at 0.125 ms intervals, but that interval reduction is not a guaranteed end-to-end reduction. Microsoft changed Windows 11 input processing after finding high-report-rate mice plus background raw-input listeners could create excess processing/stutter. ([Razer polling-rate explanation](https://www.razer.com/eu-en/technology/razer-hyperpolling), [Microsoft performance engineering](https://blogs.windows.com/windowsdeveloper/2023/05/26/delivering-delightful-performance-for-more-than-one-billion-users-worldwide/), [Windows 11 fix notes](https://blogs.windows.com/windows-insider/2023/06/20/releasing-windows-11-build-22621-1926-to-the-release-preview-channel/))
2. **Expected benefit and conditions:** higher rates can reduce report quantization on supported mice, receivers, ports, firmware, Windows builds, and CPUs; they can worsen frame consistency on constrained systems or with multiple listeners.
3. **Evidence:** **Moderate** for the interval and system interaction; **insufficient evidence** for one universal rate.
4. **Downsides/failures:** more CPU/DPC work, stutter, battery drain on wireless devices, compatibility problems, or no optical benefit. Microsoft strongly recommends not disabling USB selective suspend globally. ([USB selective suspend](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend))
5. **Decision:** **Leave the current/vendor default rate unchanged; offer other natively supported rates as an opt-in experiment. Exclude USB overclock/filter drivers and global suspend disabling.**
6. **Safe lifecycle:** identify HID device/firmware, sample observed report intervals without injecting input, enumerate USB topology and background raw-input listeners where feasible. Guide the vendor utility; record the prior supported rate, verify sampled distribution, and guide restoration. Never install an unsigned/filter driver.
7. **Before/after test:** include the current rate and 1/2/4/8 kHz where natively supported, in low- and high-CPU-load scenes with the same port and sensitivity. Use a repeatable physical motion actuator plus optical motion-to-photon capture for movement polling; button click-to-photon is a separate button-path test. Collect at least 200 valid events per cell plus p99 frame time and a short WPR DPC/ISR trace; reject a lower median if tail pacing or USB stability worsens.

## 17. Treat HAGS as a rebooted A/B experiment, not a default latency switch

1. **Latency path:** Windows GPU scheduling. Microsoft says Hardware-Accelerated GPU Scheduling offloads most scheduling to dedicated GPU hardware and expected the transition to be largely transparent, without significant changes; support/defaults depend on hardware and driver. ([Microsoft DirectX blog](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/), [WDDM HAGS capabilities](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmdt/ns-d3dkmdt-d3dkmt_wddm_2_7_caps))
2. **Expected benefit and conditions:** sign and size are system/driver/workload-dependent. There is **insufficient evidence** for a universal current-CS2 recommendation.
3. **Evidence:** **Moderate** for mechanism, **weak/insufficient** for a general latency benefit.
4. **Downsides/failures:** reboot, driver-specific regressions, capture-metric ambiguity. PresentMon warns HAGS makes several GPU-execution fields less accurate—about 0.5 ms in its example, workload/GPU dependent—so an apparent GPU-time change can be measurement bias. ([PresentMon HAGS caveat](https://github.com/GameTechDev/PresentMon#tracking-gpu-work-with-hardware-accelerated-gpu-scheduling-enabled))
5. **Decision:** **Offer as an opt-in experiment**; no recommendation until that PC passes paired reboot blocks and hardware latency/independent metrics agree.
6. **Safe lifecycle:** detect supported/enabled/default capability bits, current setting, driver, and reboot state. Guide the documented Windows Graphics page; persist a reboot-resume transaction, verify after boot, and guide exact restoration. Do not toggle undocumented registry values directly.
7. **Before/after test:** balanced reboot blocks, HAGS off/on, CPU- and GPU-bound scenes. Use optical/FrameView latency and frame delivery as primaries; qualify affected PresentMon GPU fields and compare with WPR/GPUView. Match boot/warm-up/thermal state.

## 18. Use DPC/ISR and audio/USB analysis only to diagnose a time-correlated fault

1. **Latency path:** a long or excessive driver ISR/DPC can delay ordinary CPU work; audio-buffer underruns can cause audible glitches. Microsoft’s WPA DPC/ISR tables expose module/function and duration, and WPR has GPU, desktop-composition, and audio-glitch profiles. ([Microsoft CPU/DPC analysis](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/cpu-analysis), [WPR built-in profiles](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/built-in-recording-profiles))
2. **Expected benefit and conditions:** remediation can help only when an actual hitch/audio failure is time-correlated to a named driver/device and a supported update, port change, or device configuration removes it. Separate USB controllers or larger audio buffers have **insufficient evidence** as generic CS2 tweaks.
3. **Evidence:** **Strong** diagnostic method; **weak** generic optimization. LatencyMon’s own documentation targets real-time audio and says its thresholds are arbitrary/buffer-dependent, so its green/red result is not a gaming-latency score. ([LatencyMon usage](https://www.resplendence.com/latencymon_using), [LatencyMon technical information](https://www.resplendence.com/latencymon_technical))
4. **Downsides/failures:** disabling a device can remove audio/network/input; larger audio buffers add audio latency; traces add overhead and may include private process context. A coincident spike is not proof of causality.
5. **Decision:** **Offer expert diagnosis only after a reproduced fault. Never recommend from LatencyMon alone.**
6. **Safe lifecycle:** first correlate PresentMon hitch timestamps with a short WPR trace; resolve module/function in WPA; offer only vendor update, supported buffer setting, direct-port test, or device isolation one at a time. Record device/driver/port/buffer state, verify the fault is gone, and restore on non-improvement. Never blindly disable Wi-Fi, audio, Bluetooth, or USB power management.
7. **Before/after test:** repeat the exact hitch/audio scenario with the suspected device/state A/B; compare hitch-window p99/p99.9, DPC/ISR duration by module, lost audio buffers, and optical latency if relevant. Require repeatable temporal correlation and a confirmation run.

## 19. Offer CS2 “Streamlined Push to Talk” only to users who use push-to-talk

1. **Latency path:** first activation of the in-game voice path and its associated frame-time hitch, not the ordinary mouse-to-photon path; Valve does not document which lower-level subsystem causes the remaining hitch. Its 11 June 2024 CS2 notes say in-game voice had caused hitching and that enabling **Streamlined Push to Talk** avoids the hitch incurred the first time the player uses push-to-talk. ([Valve release notes](https://steamcommunity.com/ogg/730/announcements/detail/4191242835348964245))
2. **Expected benefit and conditions:** a narrower first-use hitch reduction when the current build exposes the option, the user actually uses in-game push-to-talk, and the hitch is present with that audio device/driver. Valve’s note supports no steady-state FPS or general click-latency claim; those benefits have **insufficient evidence**.
3. **Evidence:** **Strong** for supported setting and intended CS2 mechanism; **moderate/insufficient** for the current magnitude across audio stacks because Valve did not publish a benchmark and the original note predates the research date.
4. **Downsides/failures:** exact device-initialization lifetime, power cost, and driver-specific effects are not documented; treat them as unknowns to measure. A voice-device conflict, audio glitch, or idle power regression is possible, while non-voice users receive no benefit. This is an ordinary supported game setting, not permission to record voice content.
5. **Decision:** **Offer as an opt-in experiment** to push-to-talk users or after a reproduced first-use voice hitch. Leave it alone otherwise.
6. **Safe lifecycle:** ask whether the user uses push-to-talk and have them confirm the current in-game setting; do not read CS2 config files or capture microphone content. Guide the supported Audio UI, record the exact prior selection, restart CS2 for each cold-arm test, verify by user confirmation, and revert through the same UI.
7. **Before/after test:** one human-operated first push-to-talk event after each fresh CS2 launch in a private/local test context, with identical voice endpoint and scene. Use randomized cold-launch pairs and measure the surrounding PresentMon frame-time excursion plus a short privacy-reviewed WPR audio-glitch/DPC trace; separately check idle CPU/power and voice function. Never generate voice input or automate a live match.

## 20. Use CS2 packet-loss/jitter buffering only when missed ticks justify its added delay

1. **Latency path:** network receive/command timing, not local render or display latency. Valve’s Source 2 Telemetry FAQ documents that the **Buffering to smooth over packet loss/jitter** setting increases receive margin by one or more ticks; the extra margin can prevent a late packet from becoming a missed tick, but explicitly adds latency. ([Valve Source 2 Telemetry FAQ](https://help.steampowered.com/en/faqs/view/5E6F-5B36-5485-F6B9), [Valve network telemetry update](https://store.steampowered.com/news/app/730/view/4472731215261073715))
2. **Expected benefit and conditions:** potentially fewer loss/jitter-driven missed ticks or visible network hitches on an unstable path. A stable connection with no missed ticks has no established upside and still pays the configured delay.
3. **Evidence:** **Strong** for the supported mechanism and explicit latency/robustness trade-off; user-specific net benefit is **conditional** and a repeatable threshold is **insufficient evidence**.
4. **Downsides/failures:** one or more ticks’ worth of additional receive margin, corresponding to the chosen increase, can mask an ISP/router/server problem and mislead users into treating a network workaround as lower input latency. It should not improve local FPS, DPC latency, or scan-out. Conditions can change by server, route, household load, and time.
5. **Decision:** **Offer as an opt-in experiment only after CS2’s own telemetry reports repeated ticks missed due to loss/jitter. Never recommend it by default for a clean link.** It belongs to a separate “network consistency” category, not a latency score.
6. **Safe lifecycle:** accept only user confirmation or manual import of high-level CS2 HUD values and the current supported in-game selection. Guide the Game UI and display its current labels verbatim; the UI’s packet-labelled choices and Valve’s documented tick-worth timing effect are related but not interchangeable units. Store the exact prior choice, re-confirm the effective choice and HUD outcome, and guide restoration. No packet capture, inspection, shaping, registry changes, game-file writes, screen scraping, or automatic matchmaking/server selection.
7. **Before/after test:** compare the baseline choice with the first supported buffering increment; consider the second only if the first does not control repeated missed ticks. Use matched user-operated sessions on the same server/route/time window where practical and report ping, raw loss/jitter, missed-tick rate, visible hitches, and user-rated consistency. Ping is network RTT and will not measure the added engine receive margin. Because internet conditions are not controlled, the consumer app must label a non-replicated result **observational/insufficient evidence**, never causal.

## 21. Keep a known-good supported driver/firmware path; update for a relevant fix, not because “latest is fastest”

1. **Latency path:** game profiles, shader compiler, presentation, input firmware, device/driver defects, and stability. Valve and GPU vendors ship fixes that can affect CS2, while a new graphics driver also clears NVIDIA’s shader cache. ([Valve CS2 announcements](https://steamcommunity.com/app/730/announcements), [NVIDIA shader cache](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nv3dENG/Manage_3D_Settings_%28reference%29.htm))
2. **Expected benefit and conditions:** update when the vendor release notes identify a relevant CS2/security/stability fix or the installed version is unsupported. Performance superiority of every newest driver is **insufficient evidence**.
3. **Evidence:** **Moderate**, release-specific; security support is a separate valid reason from performance.
4. **Downsides/failures:** regression, new known issue, reboot, shader-cache cold runs, lost/customized profiles, or failed firmware flash.
5. **Decision:** **Guided recommendation only. Exclude automatic driver/firmware installation, DDU, and rollback to insecure/unsupported releases.**
6. **Safe lifecycle:** inventory exact signed driver/firmware versions and show primary release notes/known issues. The app never installs them in v1. After a user-managed update, mark prior benchmark pairs incomparable, re-scan settings, warm shaders, and retain the old experiment record rather than overwriting it.
7. **Before/after test:** only after a deliberate update: A/A baseline on the old version, update/reboot/warm-cache protocol, then matched runs on the new version. Compare frame/latency/stability and record that driver version is inseparable from any reset profile/cache effects unless separately controlled.

## 22. Do not tune the network stack by registry; study NIC interrupt moderation only in a controlled research mode

1. **Latency path:** NIC interrupt batching can add local packet-processing delay; RSS distributes receive work across CPUs. Microsoft says interrupt moderation waits for packets or a timeout and therefore increases individual packet round-trip time, while reducing interrupt/CPU cost. ([Microsoft interrupt moderation](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/interrupt-moderation), [Microsoft RSS](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/introduction-to-receive-side-scaling))
2. **Expected benefit and conditions:** a small local-network latency reduction is plausible when NIC batching is the measured limiter, but network transit/server time is a different path from local input-to-photon. There is **insufficient evidence** that disabling moderation improves current CS2 outcomes on typical client PCs.
3. **Evidence:** **Weak** for CS2; platform mechanism is documented but the practical benefit/risk balance is not.
4. **Downsides/failures:** higher interrupt/CPU load, worse frame pacing, throughput/energy regression, driver instability, and distracting users from ISP/routing/jitter/loss causes.
5. **Decision:** **Exclude from v1 recommendations. Offer only a lab experiment after open research succeeds.** Leave RSS/default offloads intact.
6. **Safe lifecycle:** read adapter/driver/link state and official CS2 loss/jitter telemetry; do not capture or modify packets. A future experiment may use only a documented per-adapter UI/property, save the exact prior enum, verify it, and revert. No registry, Nagle/TCPACK, MTU, QoS, DNS, “gaming packet priority,” or packet shaping changes.
7. **Before/after test:** controlled local server plus a separately observed real route; randomize moderation on/off, measure packet RTT/jitter/loss, CPU/DPC cost, p99 frame time, and CS2’s own telemetry. Valve explains that its HUD distinguishes loss/jitter-driven missed ticks; this must remain separate from frame latency. ([Valve network telemetry update](https://store.steampowered.com/news/app/730/view/4472731215261073715))

## 23. Guide monitor overdrive/low-latency OSD settings, but do not automate them

1. **Latency path:** panel processing and pixel transition. Optical display latency includes scan-out and pixel response; monitor input lag and response time are distinct measurements. ([RTINGS input-lag methodology](https://www.rtings.com/monitor/tests/inputs/input-lag), [NVIDIA latency guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/))
2. **Expected benefit and conditions:** a suitable overdrive/low-latency mode may reduce transition/processing time on that exact monitor, refresh, and VRR state.
3. **Evidence:** **Moderate**, highly monitor-specific; OSD labels such as “1 ms” are not measurements.
4. **Downsides/failures:** overshoot/inverse ghosting, brightness loss, disabled VRR, refresh restrictions, or worse behaviour at lower VRR rates.
5. **Decision:** **Offer as a guided opt-in experiment**, outside automatic v1 control.
6. **Safe lifecycle:** identify monitor model without retaining serial number, link its official manual when available, ask the user to record the current OSD value/photo, change it manually, and confirm restoration. Do not use DDC/CI writes until model-specific safety testing exists.
7. **Before/after test:** fixed top/middle/bottom optical sensor positions, several refresh/VRR rates, panel warmed for a fixed time; measure click-to-photon, transition overshoot/high-speed footage, and visual acceptability.

## 24. Use supported in-game controls; exclude legacy launch-option and config “FPS packs”

1. **Latency path claimed:** enthusiasts claim process priority, thread count, renderer, input, preload, or tick behaviour. Valve documents launch settings mainly for video/display diagnosis and has removed development-only command-line options; there is no current primary evidence for universal CS2 latency gains from `-high`, `-threads`, `-tickrate`, `-nojoy`, preload/DX9 flags, or copied CS:GO configs. ([Steam diagnostic launch settings](https://help.steampowered.com/en/faqs/view/2542-790F-14F8-D66A), [Valve October 2024 update](https://store.steampowered.com/news/app/730/view/4674264042199559950))
2. **Expected benefit and conditions:** none established. A renderer change such as `-vulkan` may be useful for compatibility on a particular system, but a universal performance benefit is **insufficient evidence** and each renderer has a separate shader/presentation path.
3. **Evidence:** **weak/insufficient evidence** for the listed launch/config claims as a bundle. `-high` additionally conflicts with Windows’ documented priority risks in section B; the remaining flags are excluded for absent current support/benefit evidence, not because every flag has been individually disproven. Renderer choice remains research-only.
4. **Downsides/failures:** no-op flags, crashes, altered cache/presentation behaviour, ignored updates, Steam Cloud conflicts, or lost user settings. Steam Cloud synchronizes configured files before/after sessions, so machine-specific file edits can also be overwritten or propagated. ([Steam Cloud](https://partner.steamgames.com/doc/features/cloud?l=english))
5. **Decision:** **Exclude performance launch-option packs, autoexec packs, undocumented CVars, and direct config writes from v1.** Do not remove user convenience/diagnostic flags silently.
6. **Safe lifecycle:** scan launch options read-only, classify only against a versioned primary-source allow/deny list, show an exact diff, and offer a user-approved clean baseline with collision-safe backup. Supported CS2 video/latency controls are changed manually in-game. Renderer experiments stay in the lab and preserve separate caches.
7. **Before/after test:** no routine product test because unsupported flags should not be legitimized. For an explicitly documented renderer experiment, use separate warm-up series, identical presentation/sync state, randomized matched runs, and stability checks; promote only after current Valve documentation and reproducible benefit exist.

---

# B. “Do not include” snake-oil and risk register

The following are hard product exclusions, not hidden “advanced” switches. A reversible UI does not make an unsupported or unsafe intervention appropriate.

| Excluded intervention | Evidence assessment and failure mode | Product rule |
|---|---|---|
| **Anti-cheat bypass, VAC probing/evasion, DLL injection, hooks, proxy DLLs, game-memory reads/writes, executable/package modification, exploits, packet interception/replay/modification, synthetic aim/movement/clicks, macros, or online benchmark automation** | **Unsafe / game-integrity risk.** Valve’s Trusted Mode blocks third-party files interacting with CS2, its VAC policy distinguishes ordinary hardware/drivers from cheating modifications, and the Steam Subscriber Agreement prohibits cheats, tampering, and unauthorized automation. ([Valve Trusted Mode](https://help.steampowered.com/en/faqs/view/09A0-4879-4353-EF95), [VAC](https://help.steampowered.com/en/faqs/view/571A-97DA-70E9-FF74), [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/)) | No write-capable or virtual-memory CS2 process access; target only by external ETW process identity. No injection, module loading, game input generation, packet access, or anti-cheat workarounds. Stop if observation is blocked. Never claim Valve certification. |
| **Legacy AMD Anti-Lag+ or any driver path that detours game functions** | **Known historical failure.** Valve said the 2023 feature detoured engine DLL functions, documented an incompatible-driver/VAC event, and later reversed affected bans. This is not evidence against current developer-integrated Anti-Lag 2; it is evidence for prohibiting opaque code-detour “optimizations.” ([Valve 13 October 2023 statement](https://x.com/CounterStrike/status/1712875606776729832), [Valve follow-up](https://steamcommunity.com/games/CSGO/announcements/detail/3717217413231124447)) | Accept only a current game-exposed Anti-Lag 2 control on supported hardware/driver. |
| **Disable Defender, add CS2/app exclusions, disable firewall, Core Isolation/Memory Integrity/HVCI/VBS, Secure Boot, vulnerable-driver blocklists, exploit mitigations, Windows Update, driver signing, or integrity checks** | **Security regression; no qualifying CS2 evidence.** Microsoft says Defender exclusions reduce protection, recommends keeping Windows Firewall enabled, describes Memory Integrity as kernel protection, and describes Secure Boot as startup trust protection. ([Defender exclusions](https://support.microsoft.com/en-us/windows/virus-and-threat-protection-in-the-windows-security-app-1362f4cd-d71a-b52a-0b66-c2820032b65e), [Windows Firewall](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/), [Device Security](https://support.microsoft.com/en-US/Windows/Security/Windows-Security/device-security-in-the-windows-security-app), [Secure Boot](https://support.microsoft.com/en-us/windows/security/devicesecurity/windows-11-and-secure-boot)) | Never recommend, apply, or condition benchmarking on weakened security. A security state is a recorded guardrail, not a performance lever. |
| **Kernel telemetry through old WinRing0 or another vulnerable helper driver** | **Unsafe.** Microsoft identifies WinRing0 as a vulnerable driver used by several hardware utilities. ([Microsoft vulnerable-driver alert](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/microsoft-defender-antivirus-alert-vulnerabledriver-winnt-winring0)) | V1 has no custom/permanent kernel driver. Use ETW and documented vendor/user-mode telemetry. Pin, hash, sign, and review every bundled binary. |
| **Force HPET or timer modes with `useplatformclock`, `useplatformtick`, `disabledynamictick`, or `tscsyncpolicy`** | **Disproven as a generic optimization.** Microsoft labels these BCDEdit switches as debugging controls. Windows/QPC already selects an appropriate hardware counter. ([BCDEdit debug options](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set), [QPC guidance](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)) | Never modify boot timer policy. Report and offer restoration guidance if a non-default debug flag is detected, but do not silently change it. |
| **TimerResolution helpers or forcing 0.5/1 ms timer resolution** | **Unsupported / insufficient CS2 evidence, with documented cost risk.** Since Windows 10 2004, `timeBeginPeriod` primarily affects the calling process; Microsoft warns higher resolution can increase scheduling activity, reduce performance, and prevent power saving, and it does not improve QPC accuracy. That documented API does not establish what every utility using an undocumented native API does, so the exclusion is not based on claiming all helpers are no-ops. ([Microsoft `timeBeginPeriod`](https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod)) | Do not ship a timer helper, change another process, or advertise “lower timer = lower input lag.” Collector timing uses QPC without changing resolution. |
| **Realtime/High priority, `-high`, CPU affinity, “P-core only,” SMT/core parking, thread-count or scheduler scripts** | **Disproven/unsafe generic advice.** Microsoft warns High/Realtime can starve the system—including mouse input—and generally advises avoiding affinity because it can interfere with scheduler decisions and parallel work. ([Priority classes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass), [Scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities), [Affinity guidance](https://learn.microsoft.com/en-us/windows/win32/procthread/multiple-processors)) | No process-priority or affinity mutation. The benchmark worker itself remains ordinary priority. |
| **MMCSS `SystemResponsiveness`, `GPU Priority`, `SFIO Priority`, `Clock Rate`, `NetworkThrottlingIndex`, or “GPU scheduling priority” registry packs** | **Mixed documentation; insufficient CS2 benefit.** Microsoft documents that MMCSS GPU and SFIO priority fields are not used and that old `Clock Rate` behaviour changed; `SystemResponsiveness` is a real global policy, not a no-op. There is no current primary evidence that changing it, `NetworkThrottlingIndex`, or related global values produces a net CS2 latency benefit. ([MMCSS](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service)) | Direct registry recipes are never part of the catalogue; this is an evidence/scope exclusion, not a claim that every listed value is nonfunctional. |
| **Generic MSI-mode/IRQ-affinity/interrupt-priority registry changes** | **Weak and driver-owned.** Microsoft’s default interrupt-affinity policy is generally appropriate; MSI enablement belongs in a device driver’s INF, not a game optimizer. ([Interrupt affinity](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/interrupt-affinity-and-priority), [MSI enablement](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/enabling-message-signaled-interrupts-in-the-registry)) | No registry forcing, device-manager batch mutation, or IRQ “optimization.” Diagnose a signed driver with WPR instead. |
| **Global USB selective-suspend disable or unsigned mouse/USB polling filter** | **Disproven generic remedy / kernel risk.** Microsoft strongly recommends keeping selective suspend enabled. Unsigned/filter drivers add kernel, stability, and anti-cheat exposure for an unproven, rate-dependent report-interval change. ([USB selective suspend](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/usb-selective-suspend), [Windows driver policy](https://support.microsoft.com/en-US/Windows/Hardware/Drivers/the-windows-driver-policy)) | Vendor-supported firmware/profile rates only; no filter driver or global USB power mutation. |
| **Nagle/TCP ACK registry edits, `TCPNoDelay`, MTU/jumbo frames, DNS, QoS, offload/RSS disable, packet priority/shaping, or “gaming network” scripts** | **Weak/irrelevant to the local path and potentially harmful.** `TCP_NODELAY` is a per-socket application option, RSS distributes receive processing, and interrupt/throughput trade-offs are adapter-specific. ([TCP socket options](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-tcp-socket-options), [RSS](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/introduction-to-receive-side-scaling)) | Network is read-only telemetry in v1. Never inspect or alter CS2 packets. No network registry writes. |
| **Service-removal/debloat scripts, pagefile disable, standby-list purges, memory cleaners, app-package removal, or killing Explorer** | **Disproven/unsafe generalization.** The pagefile extends the commit limit and exhausted commit can cause freezes/crashes; standby memory is reusable; Microsoft presents clean boot as temporary fault isolation, not a permanent optimized state. ([Pagefile](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/introduction-to-the-page-file), [Standby memory](https://learn.microsoft.com/en-us/windows-hardware/test/assessments/results-for-the-memory-footprint-assessment), [Clean boot](https://support.microsoft.com/en-US/Windows/Experience/Startup-Boot/how-to-perform-a-clean-boot-in-windows)) | No uninstall/removal, service disable, memory purge, or pagefile change. Show measured active contention and let the user close a normal app gracefully. |
| **Global Fullscreen Optimization or MPO registry disable** | **Contradicted by modern presentation guidance.** Microsoft reports FSO is generally equal to or better than old exclusive fullscreen and documents flip-model benefits. Configuration-specific regressions are possible, so only measured per-app troubleshooting is valid. ([Microsoft FSO](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/), [DXGI flip model](https://devblogs.microsoft.com/directx/dxgi-flip-model/)) | No global toggle and no undocumented MPO registry value. Inspect actual `PresentMode`; use supported per-app controls only. |
| **NVIDIA Low Latency Mode stacked with Reflex** | **Redundant.** NVIDIA says Reflex is more effective and overrides the driver Ultra Low Latency function when both are enabled. ([NVIDIA guide](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/)) | Report the redundant override and prefer application/default state; do not sell it as an additive tweak. |
| **Periodic shader/cache/temp deletion** | **Directionally harmful to warm-frame consistency.** Driver shader cache exists to avoid repeated compilation and driver installation already clears NVIDIA’s cache. ([NVIDIA shader cache](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nv3dENG/Manage_3D_Settings_%28reference%29.htm)) | No cache cleaner. A supported reset is a separately labelled corruption diagnostic, never a benchmark precondition. |
| **Legacy CS:GO launch flags, autoexec packs, undocumented CVars, copied configs, game-file “optimization,” `-vulkan` as a universal boost** | **Weak/insufficient.** No current Valve source establishes those universal benefits; settings and engine behaviour change over time. | Read-only warning and a user-approved clean baseline only. No direct CS2 file writes in v1. Renderer research remains isolated. |
| **Global driver “esports” presets, opaque NVIDIA/AMD profile-database editing, HYPR-RX bundles, AFMF/frame generation, forced AA/texture overrides** | **Unattributable or insufficient.** AMD documents separate per-game controls and says defaults suit most users; a generated displayed-frame count is not evidence of reduced genuine input-to-photon latency. ([AMD profiles](https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html)) | Only documented per-game settings, one factor at a time. AFMF/frame generation is excluded from latency recommendations unless future CS2-specific optical evidence supports it. |
| **GPU/CPU overclock, undervolt, custom power limit, forced P-state, BIOS/firmware mutation, custom refresh/EDID, DDU or automatic driver rollback** | **Stability/security/warranty risk; insufficient scope evidence.** AMD itself labels tuning an advanced control and exposes stress-testing/power-limit implications. ([AMD tuning](https://www.amd.com/en/resources/support-articles/faqs/DH3-020.html)) | Inventory only. No tuning, flashing, custom modes, BIOS change, DDU, or unattended driver operation. |
| **LatencyMon green/red result, one “best” run, average FPS alone, subjective feel, or synthetic benchmark as proof** | **Invalid inference.** LatencyMon targets real-time audio and uses arbitrary/buffer-dependent thresholds; software FPS/latency metrics omit parts of the physical input-to-photon path. ([LatencyMon](https://www.resplendence.com/latencymon_using), [FrameView guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-7-user-guide-web-version.pdf)) | No recommendation gate from any of these. Preserve all valid runs, publish metric formulas, uncertainty, and guardrails. |

### Non-negotiable implementation boundary

V1 performs **no injection, overlay inside CS2, DLL/module loading, game-memory access, game executable/package writes, packet capture/manipulation, synthetic input, custom kernel driver, persistent elevated service, security weakening, or unsupported registry mutation**. Observation is ETW-based and external. If a documented control has no stable supported API, the app explains and guides the user rather than reverse-engineering it.

---

# C. Minimum viable product scope for v1

## C.1 Product promise

> “Measure whether a small set of supported local settings improves frame delivery or supported PC-latency metrics on this PC, in this CS2 build and scenario; preserve the original state and make rollback straightforward.”

It must not say “zero latency,” “optimized Windows,” “more responsive” without a qualified result, “pro settings,” “VAC safe forever,” “guaranteed FPS,” or “competitive advantage.” A passed test is versioned evidence for that system/scenario, not a permanent truth.

## C.2 Supported platform boundary

- Target a current, fully updated Windows 11 release for v1. Older Windows can receive a read-only compatibility report, but supporting multiple presentation/settings implementations would dilute validation.
- Standard user for scanning/explanation. Elevation is requested only for a specific, approved ETW capture or documented system operation.
- No account required, local-only by default, no persistent background component.
- CS2 must be a normal Steam installation in Trusted Mode. The app must refuse a benchmark when launch options include `-insecure`, CS2/Steam reports a Trusted Mode or anti-cheat conflict, or the requested workflow includes injection, packet handling, or generated gameplay input; it must not inspect game memory or attempt a workaround. ([Valve Trusted Mode](https://help.steampowered.com/en/faqs/view/09A0-4879-4353-EF95), [VAC](https://help.steampowered.com/en/faqs/view/571A-97DA-70E9-FF74))

## C.3 V1 capabilities

### 1. Read-only suitability scan

Collect only the state required to evaluate the catalogue:

- Windows edition/build/update and reboot-pending state; Game Mode, HAGS capability/state, windowed optimization, power source/mode, security guardrails.
- CPU, physical/logical topology for description only, RAM pressure, GPU(s), active render adapter, signed driver/version, utilization, clocks, temperature, power/thermal-limit flags when available.
- Active display path, manufacturer/model without serial, connector, active/native resolution and refresh, enumerated modes, DRR/VRR capability/state where reliably queryable, HDR, monitor count.
- CS2/Steam/game build and launch options read-only; selected renderer, window mode, resolution, refresh, cap, V-SYNC, Reflex/Anti-Lag 2, Streamlined Push to Talk, packet-loss/jitter buffering, and damage-prediction controls only by explicit user confirmation/manual import or a future validated read-only supported interface. Do not inspect memory, scrape the game window, or ingest arbitrary config contents.
- Mouse device and vendor-supported polling choices; observed report-interval distribution only during an explicit input test; USB topology on demand.
- Audio endpoints and drivers; DPC/ISR only in a short diagnostic capture after a reproducible problem.
- Active processes and resource engines during the preflight; identify capture/overlay/download activity without reading window content or terminating anything.
- Network interface/link plus user-confirmed/manual import of CS2’s high-level ping/jitter/loss HUD as a separate observational category; no screen scraping or packet data.

### 2. Two honest goal profiles

- **Smooth low latency:** highest stable refresh; VRR; Valve’s G-SYNC + V-SYNC + Reflex combination where supported; an AMD FreeSync/cap combination remains benchmark-guided.
- **Absolute latency / tearing allowed:** Reflex or Anti-Lag 2 where supported; V-SYNC off; uncapped or a measured non-saturating cap; the user explicitly accepts tearing, power, and noise trade-offs.

Neither profile is an “Apply all” preset. It is a set of individually explained candidate experiments.

Voice-hitch and packet-loss/jitter cards remain separate diagnostics. They must never be silently folded into either latency profile: Streamlined Push to Talk affects a first-use voice hitch, while network buffering deliberately trades added network delay for tolerance of late packets.

### 3. Evidence/versioned recommendation cards

Each card is immutable, signed product data containing:

- tweak ID and evidence revision date;
- causal latency path and applicability predicate;
- evidence grade, primary URLs, vendor-measured versus independent status;
- expected direction and conditions—not a promised magnitude;
- conflicts, feature losses, security/anti-cheat status, privilege/restart need;
- exact detect/backup/apply/verify/revert implementation version;
- experiment scenario, primary metric, guardrails, and invalidation rules.

The engine may output **suitable to test**, **already correctly configured**, **not applicable**, **unsafe/excluded**, or **insufficient evidence**. It must not turn a scan into an unmeasured “optimized” score.

### 4. Safe changes allowed in v1

Automatic operations are deliberately narrow:

- temporary selection of a Windows-enumerated display mode, using `SetDisplayConfig` validation plus timed automatic rollback;
- a documented Windows 11 power-mode API change—or, separately, an explicit `powercfg` scheme change—for an approved time-bounded experiment, recording and restoring both exact prior states;
- a documented NVIDIA per-game setting only if its public NVAPI ID/semantics and restore behaviour pass product validation;
- graceful relaunch of the product’s own worker and restoration of the product’s own state.

Everything else is **guided** in v1: CS2 video/Reflex/Anti-Lag/cap/voice/network-buffer controls, AMD Software, monitor OSD, Windows Graphics/Game Mode/HAGS, mouse vendor utility, and closing a user-selected background app. The app records the stated before/after and verifies the externally observable result. Guided application is preferable to writing an undocumented registry/blob/config simply to claim automation.

### 5. Experiment and report workflow

- A/A noise qualification before an A/B recommendation.
- Randomized paired crossover, one factor at a time, fixed capture window, warm-cache and thermal gates.
- Headless external ETW/PresentMon capture; optional sequential FrameView import; optional lab hardware result import.
- Run-level statistics with uncertainty, every valid pair visible, invalid runs retained with reasons.
- Local result report: current state, exact change, versions, raw-file hashes, primary/guardrail metrics, power/thermal cost, decision, and rollback verification.

## C.4 Explicitly deferred beyond v1

- Automated search across many graphics settings or cap values.
- Automatic writes to CS2 configs, Steam Cloud data, launch options, AMD profile storage, monitor DDC/CI, BIOS/firmware, driver installation, cache deletion, or network/audio/device-manager settings.
- Vulkan-versus-DX11 recommendation, automatic monitor overdrive, NIC moderation, audio/USB topology tuning, HAGS recommendation, or high-polling default until the experiments in section F succeed.
- Always-on telemetry, a cloud leaderboard, crowdsourced “best settings,” remote control, live-match automation, or a global “latency score.”

## C.5 Release gates

V1 is not ready to ship until it passes:

1. collector observer-effect and lost-event validation;
2. A/A false-positive calibration across representative hardware;
3. crash/reboot/partial-write rollback fault injection;
4. settings-API semantic and version-compatibility tests;
5. privacy/egress audit with analytics off/on;
6. signed-binary and dependency review, including verification that no vulnerable kernel driver is present;
7. current CS2/VAC smoke testing of the ETW-only workflow—compatibility evidence for that version, never a permanent safety guarantee.

---

# D. Telemetry and benchmark architecture

## D.1 Measurement hierarchy and honest claim boundary

| Instrument | What it legitimately establishes | V1 role | Important limitations |
|---|---|---|---|
| **Headless PresentMon/ETW** | Per-frame CPU, GPU, and display durations/latencies; presented/displayed cadence; dropped frames; `PresentMode`; runtime; sync/tearing state; selected hardware telemetry. PresentMon supports DirectX, OpenGL, and Vulkan and collects graphics events through ETW. ([PresentMon](https://github.com/GameTechDev/PresentMon), [metric definitions](https://github.com/GameTechDev/PresentMon/blob/main/README-CaptureApplication.md)) | **Primary collector** | Not an optical panel measurement. OpenGL/Vulkan have less instrumentation for some fields. With HAGS, PresentMon documents bias in `msUntilRenderStart`, `msUntilRenderComplete`, `msGPUActive`, and `msGPUVideoActive`; record HAGS and qualify/suppress those comparisons. ([PresentMon caveats](https://github.com/GameTechDev/PresentMon#tracking-gpu-work-with-hardware-accelerated-gpu-scheduling-enabled)) |
| **NVIDIA FrameView** | An alternate NVIDIA workflow and, in instrumented CS2 configurations, PC latency from OS-received input through the completed frame sent to the display. FrameView leverages PresentMon for frame analytics, so it is interoperability/vendor-marker evidence—not independent physical validation. ([FrameView guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-7-user-guide-web-version.pdf), [NVIDIA CS2 measurement](https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/)) | Optional sequential interoperability/import | PC latency excludes the mouse and physical monitor. Missing/`N/A` remains missing. Do not run beside PresentMon, and do not show any overlay during the measured region. |
| **LDAT or Reflex Analyzer-class optical setup** | Physical click/motion-to-photon, including peripheral, PC, scan-out, panel processing, and pixel response. ([NVIDIA LDAT](https://developer.nvidia.com/nvidia-latency-display-analysis-tool), [Reflex Analyzer setup](https://www.nvidia.com/en-us/geforce/news/reflex-latency-analyzer-360hz-g-sync-monitors/)) | Gold-standard lab validation; later expert accessory | Requires controlled actuator/mouse, stable luminance target, sensor position, panel warm-up, and enough randomized events. Sensor placement changes the result because displays scan across the panel. |
| **WPR/WPA/GPUView** | Causal diagnosis of CPU scheduling, context switches, DPC/ISR, disk/file activity, audio glitches, DWM, and GPU/flip queues. ([WPR](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder), [WPA CPU/DPC analysis](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/cpu-analysis), [GPUView](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/using-gpuview)) | Short expert/support trace after a reproduced fault | Heavier, potentially large, and privacy-sensitive. It diagnoses a regression; it is not a single latency score. |
| **CapFrameX** | Useful PresentMon-based capture analysis, comparison, export, and cross-checking. ([CapFrameX repository/manual](https://github.com/CXWorld/CapFrameX)) | CSV/JSON import/export/reference, not embedded v1 core | Not independent evidence from PresentMon; metric formulas and beta fields must not be silently mixed. |
| **LatencyMon** | A first-pass lead for real-time-audio suitability, ISR/DPC, and hard-pagefault investigation. ([LatencyMon](https://www.resplendence.com/latencymon), [technical information](https://www.resplendence.com/latencymon_technical)) | No automated role; optional support note | It targets audio, affects the measured system itself, and its headline thresholds are arbitrary/buffer-dependent. It cannot establish CS2 input-to-photon or frame pacing. |

The UI must keep these semantics separate:

- **CPU frame time/cadence:** application frame-start spacing, not photon time.
- **Displayed time/cadence:** how long Windows reports a frame/display change, not panel pixel response.
- **CPU/GPU busy, wait, and latency:** work and queue-location evidence, subject to collector/platform caveats.
- **Software display latency:** presentation-to-display-event interval, not optical response.
- **Instrumented PC latency:** supported in-game marker path, excluding mouse and monitor unless the tool explicitly says otherwise.
- **Optical system latency:** click/motion-to-visible luminance change under a documented physical method.

Do not publish an unlabeled “latency” number. Do not use “1% low” without a formula/version: FrameView defines one form as the average over the slowest 1% of frames, while percentile-style metrics answer a different question. P99 frame time is preferable as the primary tail statistic, but its quantile estimator, sample inclusion, frame-type handling, and capture window must also be schema-versioned. ([FrameView guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-7-user-guide-web-version.pdf))

## D.2 Process and privilege architecture

```text
standard-user UI/scanner
        |
        +-- signed, ephemeral headless benchmark worker
        |      ETW/PresentMon only; targets cs2.exe externally; exits after capture
        |
        +-- signed, ephemeral display watchdog
        |      owns prior topology; reverts on timeout/parent failure; then exits
        |
        +-- optional short WPR diagnostic worker
        |      separate user-approved support mode
        |
        +-- UAC-on-demand change broker
               allowlisted tweak ID + typed arguments only; exits after verify/revert
```

- **No persistent privileged service.** The display watchdog exists only for an active mode confirmation. The broker accepts no command string, script, path wildcard, registry expression, or arbitrary executable.
- **No overlay, injection, global input hook, packet layer, or CS2 memory handle.** Ordinary frame runs leave input tracking off.
- PresentMon notes that ETW collection requires appropriate permission and otherwise suggests administrator/Performance Log Users membership. V1 should use an ephemeral elevated worker when needed, not permanently broaden group membership. ([PresentMon access requirements](https://github.com/GameTechDev/PresentMon#user-access-denied))
- Pin the collector release and dependencies, verify signatures and SHA-256 at launch, record the exact binary hash in every run, and update only through a signed release process.
- Preflight another ETW/capture tool. PresentMon, FrameView, CapFrameX, recording overlays, and WPR do not run concurrently during an ordinary measured region.

## D.3 Durable local data model

The local store contains immutable raw artifacts plus versioned derived records:

- `EnvironmentSnapshot`
- `ScenarioDefinition`
- `CollectorProvenance`
- `CaptureRun`
- `MetricDefinition`
- `Experiment`
- `ChangeTransaction`
- `Decision`
- `SupportBundleManifest`

Every run records raw-file hash, collector/version/hash/arguments, metric schema, QPC range, A/B state, pair/block/boot/order IDs, target PID and swap chain, event/buffer loss, present-mode distribution, environment fingerprint, and immutable invalidation reason. Derived results must be reproducible from raw data; a later formula creates a new result version and never overwrites the old one.

ETW is designed as efficient tracing, but Microsoft documents that events can be lost when buffers, storage, or consumers cannot keep up. Collector health and event loss are therefore validity fields, not debug trivia; any affected run is invalidated and retained. ([Microsoft ETW overview](https://learn.microsoft.com/en-us/windows/win32/etw/about-event-tracing))

## D.4 Exact default benchmark protocol

This is the v1 protocol to validate through A/A research. Its run counts are pragmatic starting values, **not proof that five blocks are universally sufficient**.

### Step 1 — pre-register the one-variable experiment

Before treatment data is visible, record:

- candidate and causal path;
- A and B exact states;
- primary metric and expected direction;
- smallest practically worthwhile effect, derived from product A/A calibration for this metric/scenario—not a universal internet threshold;
- frame/FPS/stability/power/thermal/visual/security guardrails;
- restart, warm-up, invalidation, and decision rules.

If several controls must change together—such as Valve’s three-part NVIDIA profile—label it a **bundle effect**. Do not attribute the result to one component.

### Step 2 — freeze and fingerprint the environment

Record OS/CS2/driver/collector builds; CPU/GPU; display identity without serial; resolution/refresh/VRR/V-SYNC/cap/latency-control state; renderer/present mode; if exposed, Streamlined Push to Talk, packet-loss/jitter buffering, and all damage-prediction toggles; AC/battery; graphics-state hash; connected input/audio devices; clocks/temperature/power; and background CPU/GPU/disk/network activity.

For an optical shooting test, freeze damage prediction because Valve documents that it can play damage audio/visual effects immediately before server confirmation, can occasionally be wrong, and is inactive at high ping. **Measurement-design inference:** changing its configured or effective state can move the visible endpoint without changing when the server confirms the outcome, confounding what the optical result means. Prefer a fixed local luminance target unrelated to hit confirmation; if a shot-feedback endpoint is unavoidable, record every prediction toggle, ping/server context, and whether prediction is effectively available, then keep them identical across arms. ([Valve damage-prediction update](https://steamcommunity.com/ogg/730/announcements/detail/4458095069430284353))

Abort the pair on game/OS/driver update, display-mode drift, Steam download, Windows Update activity, uncontrolled pre-arm thermal/power drift, wrong swap chain, focus loss, or collector/event-loss failure. Ask the user to resolve noise; never kill services automatically. If a treatment itself triggers throttling or a power-limit transition while its matched control does not, retain that run as a valid adverse treatment outcome and guardrail failure.

### Step 3 — use two separate scenarios

1. **Frame-consistency scenario:** a user-selected CS2 replay/demo or exact manually repeated local-practice route, with a fixed capture window. Valve notes that demo playback can differ from what the player originally saw because CPU/GPU work is pipelined; a demo is therefore a repeatable playback workload, not proof of original match latency or networking. ([Valve TrueView](https://steamcommunity.com/ogg/730/announcements/detail/578276333072678919))
2. **Interactive latency scenario:** a fixed local scene with valid game latency markers where supported. Strong end-to-end claims require LDAT/Reflex Analyzer-class hardware. The app never generates gameplay input; a human or lab actuator supplies the event.

Uncontrolled public matches are observational only and cannot decide a local tweak because scene, server, players, network, and input differ.

### Step 4 — stabilize cache and temperature

- Do not use the first launch after a game/driver update as a measured run.
- Run at least three unmeasured identical rehearsals and a five-minute minimum warm-up; extend until a predeclared temperature/clock stationarity gate passes.
- Never clear shader/driver caches between ordinary A/B arms. Cold-cache behaviour is its own experiment.

### Step 5 — establish A/A repeatability

Run five identical control captures. Each capture is 70 seconds; analyze the fixed middle 60 seconds, never a hand-selected smooth interval. If product-calibrated repeatability fails, stop with **baseline too noisy**. Do not proceed merely because one control run looks good.

### Step 6 — randomized paired crossover

Run five paired blocks, randomly ordering each as A→B or B→A and balancing orders so counts differ by no more than one. For each arm:

1. apply and semantically verify state;
2. restart CS2 if required;
3. repeat the same unmeasured rehearsal/warm gate;
4. capture 70 seconds headlessly;
5. analyze the fixed middle 60 seconds.

Reboot-required experiments use boot as a block and matched rebooted controls. NIST’s experimental-design guidance supports randomization, blocking nuisance variables, and paired comparisons. ([Randomized designs](https://www.itl.nist.gov/div898/handbook/pri/section3/pri331.htm), [randomized blocking](https://www.itl.nist.gov/div898/handbook/pri/section3/pri332.htm), [paired observations](https://www.itl.nist.gov/div898/handbook/prc/section3/prc311.htm))

For an optical lab claim, collect at least 200 valid events per condition, distributed across randomized blocks, using the same actuator, mouse mode, luminance target, sensor position, panel warm-up, and ambient conditions. The number is a conservative product protocol subject to formal power/A/A calibration, not a magic sufficiency threshold.

### Step 7 — retain and invalidate mechanically

Predeclared invalidation reasons are: lost ETW events, wrong process/swap chain, focus loss/Alt-Tab, wrong resolution/refresh/present mode, crash/reset, update, collector failure, uncontrolled precondition drift, scene deviation, or missing markers required by that experiment. Treatment-emergent throttling/power limiting, heat, hitching, or instability is retained as a negative result rather than discarded. Keep every raw file and reason; never remove post-hoc “bad” frames or outliers to make a candidate win.

### Step 8 — calculate per-run metrics

- mean FPS plus mean/median frame time;
- p95, p99, and p99.9 CPU frame time;
- p95/p99 displayed time and time above predeclared frame budgets;
- dropped/not-displayed proportion and explicitly versioned hitch count;
- present-mode/runtime/tearing distributions;
- CPU busy/wait and GPU busy/wait/latency, qualified for renderer/HAGS;
- utilization, clocks, temperatures, power, and limit flags;
- PC latency only when valid, leaving unavailable data `NA`;
- network ping/jitter/loss only in its separate observational track;
- optical median/p95/system-latency distribution in hardware tests.

Frames inside one capture are serially related; they are not thousands of independent treatment replicates. Compute paired **run-level** deltas and show every pair, mean and median paired effect, a 95% interval, and order/boot effects. Do not manufacture narrow confidence by resampling individual frames as independent observations.

### Step 9 — decide conservatively

- **Keep:** primary metric improves beyond the predeclared practical threshold; uncertainty is narrow enough; the confirmation succeeds; state is verified; and no tail/FPS/drop/stability/power/thermal/visual/security guardrail materially regresses.
- **Revert:** primary metric or a safety guardrail worsens; apply/verification fails; CS2/driver is unstable; or current state differs from approved state.
- **Inconclusive / insufficient evidence:** interval overlaps the practical threshold; baseline is noisy; effect changes sign; the preregistered required primary metric is missing (including PCL only for a PCL-primary experiment); or collector/environment validity fails. Missing PCL does not invalidate a frame-pacing-only experiment. Default action is rollback or no recommendation.

Confirm the causal effect with at least three fresh randomized paired A/B blocks, not chosen-state-only captures. Additional chosen-state runs may confirm stability, and higher-risk changes also get matched post-rollback checks to confirm reversibility. NIST recommends confirmation runs and warns that examining many comparisons changes overall confidence, so multi-tweak screening is labelled exploratory and winners require an independent confirmation. ([NIST confirmation](https://www.itl.nist.gov/div898/handbook/pri/section4/pri46.htm), [multiple comparisons](https://www.itl.nist.gov/div898/handbook/prc/section4/prc47.htm))

## D.5 Reversible change journal

Each allowlisted tweak implements `detect`, `backup`, `apply`, `verify`, and `revert`. A transaction records:

- transaction UUID, app/evidence/implementation versions;
- exact typed before-state, including **value absent/inherited**;
- intended after-state and target identity;
- backup path/hash/size and file ACL/owner where a supported file is ever involved;
- each operation/result/exit code;
- reboot-pending/resume state;
- verified current state;
- terminal state: `kept`, `reverted`, `rollback-conflict`, or `failed-safe`.

Safe sequence:

1. read and validate target;
2. create an immutable, collision-safe backup if applicable;
3. persist and flush a `prepared` write-ahead record;
4. apply one allowlisted mutation;
5. re-read and semantically verify;
6. mark `applied`;
7. benchmark and decide;
8. mark `kept`, or compare-and-swap rollback and verify `reverted`.

Rollback restores only if the current value still equals what this transaction set. If the user, game, driver, or Windows changed it afterward, report a conflict and do not overwrite newer state. Crash/reboot recovery resumes from the durable journal. A restore point can be defense-in-depth; it is never the sole backup.

## D.6 Privacy and telemetry separation

“Local benchmark telemetry” and “product analytics” are different products:

- Raw runs and results remain local by default; no account and no upload.
- Product analytics are off by default. If enabled, show the exact JSON before consent and upload only aggregated, schema-versioned metrics with a rotating random installation ID.
- Never upload raw ETL, per-frame data, command lines, file paths, process lists, IP/packet data, Steam ID, monitor/EDID serial, registry dump, or config contents.
- A raw support trace requires a second, explicit consent, local manifest/redaction preview, size display, retention/delete control, and warning. Microsoft trace-processing examples show ETLs can contain process command lines and other sensitive context. ([Microsoft trace-processing quickstart](https://learn.microsoft.com/en-us/windows/apps/trace-processing/quickstart))
- No packet capture in v1. Microsoft notes network traces may retain personal packet data. ([Microsoft `netsh trace`](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-trace))
- With analytics disabled, a network-egress test must show zero product telemetry traffic.

---

# E. Safe UX flow

```text
SCAN (read-only)
   -> EXPLAIN (causal path, evidence, cost, exact scope)
      -> BENCHMARK (A/A baseline and suitability)
         -> USER APPROVAL (one exact experiment)
            -> APPLY (journal first; supported API or guided UI)
               -> VERIFY (state, mechanism, paired result, guardrails)
                  -> KEEP or ROLLBACK (exact prior state; verify again)
```

## E.1 Scan — no mutation and normally no elevation

- Show system readiness, collector conflicts, current refresh/presentation, GPU/driver, sync/latency controls, power, polling, active contention, and benchmark noise risks.
- Separate **local input/render/display**, **network**, and **audio** paths visually. A low ping is not low input-to-photon; a LatencyMon warning is not a frame-latency diagnosis.
- Results are facts or bounded observations: “240 Hz mode available but 144 Hz active,” “GPU busy during 98% of this capture,” “rolling capture encoder active,” or “state could not be verified.” Avoid a percentage “optimization score.”
- Unsafe detected state receives restore-to-supported guidance, never a performance reward.

## E.2 Explain — recommendation card before any benchmark or change

Every card shows, without expanding an “advanced” panel:

1. what part of the latency path could change;
2. current state and suitability predicate;
3. expected direction and the conditions required;
4. evidence grade, last-reviewed date, direct primary sources, and whether numbers are vendor-produced;
5. all likely FPS, frame-pacing, visual, stability, feature, power, battery, security, privacy, and anti-cheat consequences;
6. exact objects/settings affected and whether this is automatic or guided;
7. privilege, restart/reboot, benchmark time, success/guardrail criteria, and rollback plan.

The primary action is **Test on this PC**, not **Optimize**. “Insufficient evidence” is a normal card state.

## E.3 Benchmark — qualify the baseline before requesting mutation approval

- Run preflight and five-run A/A qualification using section D.4.
- Show run order, fixed scenario, estimated time, cache/temperature status, and why a run was invalidated.
- If noise is too high, stop and explain the observed interferer or ask for a quieter later session; do not cherry-pick.
- A baseline report is useful even if the user chooses not to change anything.

## E.4 User approval — one explicit experiment

- Show an exact before → proposed-after diff, including every member of a bundle.
- No “Apply all,” default-checked risk options, or buried feature losses.
- The user separately accepts visual tearing, higher power/heat, feature loss, restart/reboot, and any support-trace privacy cost when applicable.
- For a manual vendor/game/monitor control, display the official path and wait for the user to confirm the observed result. The app does not pretend it made the change.
- UAC occurs only after this approval and names the one allowlisted operation.

## E.5 Apply — durable preparation first

- Acquire a per-target transaction lock; re-read state to detect drift since explanation.
- Persist the exact pre-state and `prepared` journal before mutation.
- Apply one setting through the documented API, or guide one UI change.
- Display-mode changes are temporary until the user confirms visibility within 15 seconds; an independent ephemeral watchdog—not the UI that may lose display—owns timeout/crash rollback.
- Reboot-required experiments persist a visible resume/rollback marker and do not mix with non-reboot factors.
- On error, crash, lost display, game launch failure, or CS2/anti-cheat incompatibility: fail closed and restore when safe.

## E.6 Verify — state first, benefit second

1. **State verification:** re-read the setting and observe its effect—active refresh, render adapter, present mode, delivered cap, polling distribution, process activity—not merely an API success code.
2. **Paired benchmark:** run the randomized A/B protocol; do not expose a provisional “winner” mid-test.
3. **Guardrail evaluation:** frame tails/drops, stability, thermal/power, visual/feature, security, collector health, and user acceptance.
4. **Result language:**
   - “Measured improvement on this PC, build, and scenario.”
   - “Measured regression.”
   - “No practically resolved difference / insufficient evidence.”

No result uses “faster aim,” “better hit registration,” or “competitive advantage.”

## E.7 Keep or rollback — restoration is a first-class result

- **Automatic rollback:** failed apply/verify, display confirmation timeout, crash/driver reset, guardrail failure, or measured regression.
- **Default rollback offer:** inconclusive result. The user may keep a supported harmless state, but the UI must say it is not evidence-backed.
- **Compare-and-swap safety:** if external state changed after apply, stop with a rollback conflict; never overwrite the newer value.
- Re-read and display the restored state, then optionally run three confirmation captures for higher-risk changes.
- History remains auditable: evidence version, prior/after values, run files/hashes, decision, and verified rollback. Backups never overwrite an earlier backup.

---

# F. Open research questions and exact pre-recommendation experiments

These experiments are prerequisites for promoting uncertain items. “Pass” means reproducible direction and practical size without a guardrail regression; it does not mean every future system receives the recommendation.

## F.0 Common laboratory controls

- Freeze and archive exact Windows, CS2, driver, firmware, collector, and evidence versions. Re-run affected studies after a material game/driver/OS change.
- Cover at least: mainstream and high-end CPUs; hybrid and non-hybrid scheduling topologies; supported NVIDIA and AMD generations; desktop and thermally constrained laptop; single- and multi-monitor; 144, 240, and 360 Hz or higher displays; VRR and fixed-refresh paths.
- Use the D.4 protocol unless a row specifies more: five A/A runs, five randomized paired blocks, 70-second captures with fixed middle 60 seconds, three confirmation runs, immutable invalidations, and run-level paired analysis.
- Optical studies use at least 200 valid events per condition across randomized blocks and fixed actuator/target/sensor position. Publish the complete distribution and capture video/fixture metadata.
- Never automate input in online play. Use local practice, a user-operated fixed scene, or external lab actuation. A demo is a repeatable render workload, not evidence about live network or the original match’s rendered timing. ([Valve TrueView caveat](https://steamcommunity.com/ogg/730/announcements/detail/578276333072678919))
- The consumer app/client never intercepts, alters, delays, drops, replays, or inspects packets. F.22’s controlled-impairment cell is a protocol-agnostic external appliance on an isolated, user-owned private-server laboratory only; it is research instrumentation outside the product, never matchmaking, and never modifies payload content. Require legal/game-integrity review before the study; if that boundary is not approved, omit induced impairment and leave the causal threshold **insufficient evidence**.

## F.1 A/A noise, practical thresholds, and false-positive rate

**Question:** How many runs are needed, and what effect is distinguishable from ordinary drift for each metric/scenario?

**Experiment:** collect at least 20 unchanged runs per scenario across three boots and three days on every reference-system class. Repeat the complete “change” workflow with A and B intentionally identical. Model within-run serial dependence, between-run/boot/day variance, order effects, thermal drift, and false keep/revert rates for p99/p99.9, drops, time-over-budget, valid PCL, and optical latency.

**Promotion gate:** choose metric-specific minimum run counts, repeatability gates, and smallest practical-effect defaults that keep the preregistered false recommendation rate. Until this exists, universal numerical thresholds are **insufficient evidence**.

## F.2 Collector observer effect and tool agreement

**Question:** Does the collector or overlay alter the quantity being measured?

**Experiment:** randomize headless PresentMon off/on, FrameView off/on, FrameView overlay off/on, and CapFrameX capture in sequential—not concurrent—cells, in CPU- and GPU-bound scenes. Use an external optical rig for latency and high-speed/external frame observation, plus collector lost-event counts. Separately compare PresentMon, FrameView, and CapFrameX CSV definitions on identical sequential workloads. FrameView and CapFrameX both leverage PresentMon for frame analytics, so agreement is implementation/interoperability validation, not independent physical evidence. ([PresentMon](https://github.com/GameTechDev/PresentMon), [CapFrameX](https://github.com/CXWorld/CapFrameX), [FrameView](https://www.nvidia.com/en-gb/geforce/technologies/frameview/))

**Promotion gate:** ship only a headless configuration whose 95% interval lies inside the predeclared acceptable observer-effect budget and whose lost-event rate is zero for valid runs. Disable overlays during measured regions.

## F.3 Software PC-latency versus optical input-to-photon

**Question:** Which PresentMon/FrameView latency fields track a real end-to-end change in CS2, and where do they fail?

**Experiment:** factors: Reflex/Anti-Lag state, CPU/GPU bottleneck, cap regime, V-SYNC/VRR, fullscreen/borderless, refresh tier, and GPU vendor. Freeze every damage-prediction toggle and, because Valve says prediction is inactive at high ping, also freeze ping/server context or use a luminance endpoint unrelated to hit confirmation. Collect sequential PresentMon and FrameView runs plus simultaneous external LDAT/Reflex Analyzer-class optical events. Compare condition-level medians/p95 and paired deltas, not raw frame/event timestamps across incompatible tools.

**Promotion gate:** define validated agreement bounds by metric/tool/configuration. Until then, software PCL is informational and any end-to-end wording is **prohibited**. FrameView documents that PC latency excludes mouse and display. ([FrameView guide](https://images.nvidia.com/content/geforce/technologies/frameview/frameview-1-7-user-guide-web-version.pdf), [LDAT](https://developer.nvidia.com/nvidia-latency-display-analysis-tool))

## F.4 NVIDIA Reflex, Boost, and the two sync goals

**Question:** On current builds, when does Reflex Enabled help; when does Boost help enough to justify power; and how does Valve’s smooth profile compare with tearing-allowed uncapped play?

**Experiment:** factorial: Reflex Off/On/On+Boost × GPU-bound/CPU-bound × G-SYNC+V-SYNC smooth profile/V-SYNC-off uncapped/measured cap × 144/240/360+ Hz. Use compatible NVIDIA systems spanning supported performance tiers. Measure optical and valid PC latency, present/displayed cadence, tears, throughput, clocks, temperature, power, and a 20-minute soak.

**Promotion gate:** Reflex Enabled may remain default if direction is robust and no guardrail regresses. Boost is recommended only by a suitability predicate tied to a measured latency win and thermal/power headroom. Preserve both user-goal profiles because Valve and NVIDIA document their different trade-offs. ([Valve](https://help.steampowered.com/en/faqs/view/418E-7A04-B0DA-9032), [NVIDIA](https://www.nvidia.com/en-au/geforce/guides/system-latency-optimization-guide/))

## F.5 AMD Anti-Lag 2, FreeSync, cap, and Enhanced Sync

**Question:** What is the current developer-integrated Anti-Lag 2 effect, and which AMD tear-free policy balances latency and pacing?

**Experiment:** supported RDNA systems/current production drivers; Anti-Lag 2 Off/On × CPU/GPU bottleneck × FreeSync off/on × uncapped/in-game cap/driver cap × V-SYNC/Enhanced Sync states, pruned to vendor-supported combinations. Test mid-range, floor, ceiling, and rapidly varying FPS. Record optical latency, valid markers, present mode, p99 displayed time, flicker, drops, temperature, and power.

**Promotion gate:** promote only current in-game Anti-Lag 2. Define an AMD smooth-profile recommendation only if replicated across displays/VRR ranges; otherwise keep FreeSync/cap/sync as guided experiments. Never enable the historical code-detouring Anti-Lag+ path. ([AMD Anti-Lag](https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html), [AMD FreeSync](https://www.amd.com/en/products/graphics/technologies/freesync.html), [AMD Enhanced Sync](https://www.amd.com/en/products/software/adrenalin/software-enhancedsync.html))

## F.6 FPS limiter choice and cap-selection algorithm

**Question:** Does CS2’s limiter, NVIDIA/AMD driver limiter, or uncapped delivery win at an identical effective rate, and how much VRR headroom is needed?

**Experiment:** compare CS2’s supported cap, documented driver limiter, Valve automatic NVIDIA cap, and uncapped, first matching their measured delivered-rate distributions. Run steady, smoke/utility-heavy, and worst-repeatable load; test several distances below VRR ceiling rather than assuming “minus three.” Include cold-clock and 20-minute thermal states.

**Promotion gate:** select a limiter/rate only from measured PCL/optical latency and p99/p99.9/drops under the heavy scene. No universal rate until results replicate by refresh/GPU class and the cap-selection algorithm passes A/A/holdout systems.

## F.7 HAGS × game-integrated latency controls

**Question:** Is HAGS beneficial, neutral, or harmful, and can PresentMon measurement bias be separated from a real effect?

**Experiment:** balanced randomized reboot blocks: HAGS off/on × Reflex or Anti-Lag off/on × CPU/GPU bottleneck × GPU family/driver. Collect optical latency, FrameView where valid, PresentMon frame/display fields, and short WPR/GPUView scheduling traces. Compare PresentMon’s affected GPU fields against WPR/independent evidence. ([Microsoft HAGS](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/), [PresentMon caveat](https://github.com/GameTechDev/PresentMon#tracking-gpu-work-with-hardware-accelerated-gpu-scheduling-enabled))

**Promotion gate:** a hardware/driver/build-specific rule only; invalidate the rule after relevant updates. Never promote HAGS based solely on PresentMon GPU busy/time.

## F.8 Presentation path, Fullscreen Optimizations, multi-monitor, and overlays

**Question:** Which actual present path does each CS2 window/renderer mode obtain, and when do overlays break independent/direct flip?

**Experiment:** fullscreen/borderless × windowed optimization on/off × FSO default/per-app-off × overlay/rolling capture absent/present × single/multi-monitor × direct-dGPU/hybrid output. Test current supported renderers separately with independent shader warm-up. Record actual `PresentMode`, runtime, tearing, displayed-time tails, DWM/GPUView path, optical latency, and Alt-Tab/VRR stability.

**Promotion gate:** recommend a change only when a detector observes the inefficient/problem path and the same state transition fixes it. Never infer from the fullscreen label or apply a global registry switch. ([Microsoft windowed optimizations](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11), [DXGI flip](https://devblogs.microsoft.com/directx/dxgi-flip-model/))

## F.9 CS2 video-setting sensitivity map

**Question:** Which supported setting materially reduces render latency on a measured GPU bottleneck without unacceptable visibility loss?

**Experiment:** one factor at a time for resolution/scaling, MSAA, shadows, ambient occlusion, shader, particle, texture/VRAM, and any current supported upscaling options. Run CPU- and GPU-bound scenes on each GPU tier after independent warm-up; store lossless reference screenshots and blinded user visibility ratings alongside GPU busy/render time, valid PCL/optical latency, p99, and VRAM.

**Promotion gate:** version-specific conditional rules only. A setting enters the product when its applicability can be detected and a gain replicates without violating a predeclared visibility/quality guardrail. “All Low” remains excluded.

## F.10 Game Mode under clean and controlled contention

**Question:** Does Game Mode improve consistency on current Windows/CS2, and only under what background load?

**Experiment:** Game Mode off/on × clean desktop/controlled signed CPU task/controlled disk task/normal user-selected capture workload. No service disabling. Measure resource allocation, CPU wait, p99/p99.9, hitches, foreground focus transitions, and background-task function.

**Promotion gate:** promote a current-build conditional recommendation only if representative classes show a reproducible tail benefit under a detector-visible workload and no clean-system or background-function regression. Until then, leave state unchanged and report **insufficient evidence**.

## F.11 Windows power mode and NVIDIA Boost/maximum-performance thermal carryover

**Question:** Is a short clock-ramp win preserved after heat reaches steady state?

**Experiment:** Balanced/Best performance × AC/battery where safe × normal/Reflex Boost/NVIDIA per-game maximum-performance as applicable. Equalize starting temperature, then capture burst, 10-minute, and 30-minute phases; log effective clocks, power, temperature, throttle flags, noise proxy, battery discharge, valid latency, and frame tails. Randomize across cooled blocks.

**Promotion gate:** suitability rule must require AC/thermal headroom and a sustained—not just first-minute—practical win. Reject any state that worsens sustained p99, throttles, or breaches the user’s power/noise guardrail.

## F.12 Mouse polling and background raw-input listeners

**Question:** When do 2/4/8 kHz modes reduce physical input-to-photon, and when does their CPU/DPC cost worsen CS2?

**Experiment:** same vendor-supported mouse/firmware/port; current/default plus 125/500/1000/2000/4000/8000 Hz where exposed × low/high CPU-load CS2 scene × zero/several controlled background raw-input listeners. Measure actual report-interval distribution, at least 200 mechanically actuated optical motion-to-photon events per cell, a separate button click-to-photon set, PresentMon frame tails, and short WPR DPC/ISR/CPU traces. Microsoft’s documented high-report-rate input changes make OS build a mandatory factor. ([Microsoft input-stack work](https://blogs.windows.com/windowsdeveloper/2023/05/26/delivering-delightful-performance-for-more-than-one-billion-users-worldwide/))

**Promotion gate:** preserve the current/vendor default until a detector predicts a repeatable alternative-rate optical benefit with no frame/USB/battery regression. Never generalize from report interval—or from button clicks alone—to motion latency.

## F.13 DPC/ISR, USB/audio topology, and remediation causality

**Question:** Can a driver/device trace feature predict an actual CS2 hitch or audio fault, and does a supported remedy remove both?

**Experiment:** time-align PresentMon hitch windows and audio glitches with WPR DPC/ISR stacks under controlled mouse, USB audio, network, Bluetooth, and storage loads. Test one signed driver update/rollback, physical port/controller move, sample format/buffer, or supported audio-enhancement toggle at a time. Track ETW loss and module/function duration.

**Promotion gate:** no generic numeric DPC pass/fail. Offer remediation only after repeatable timestamp correlation and confirmation that both the driver symptom and user-visible fault improve. Do not recommend “separate controllers” without enumerated topology and replicated benefit.

## F.14 Shader/cache stabilization

**Question:** How many runs after a game/driver update are cold, and can the app detect stabilization?

**Experiment:** ten consecutive identical runs after ordinary CS2 update, graphics-driver update, and—in isolated lab testing only—vendor-supported cache reset. Do not delete directories manually. Model compilation/hitch count, p99/p99.9, disk activity, clocks, and temperature by run.

**Promotion gate:** derive a vendor/build-specific warm-up stopping rule and invalidate it after cache/compiler changes. Routine clearing remains excluded even if a corrupted-cache case once improves.

## F.15 NIC interrupt moderation without packet manipulation

**Question:** Can reduced NIC batching lower CS2-relevant network delay without increasing CPU/DPC-driven frame tails?

**Experiment:** controlled wired LAN UDP echo outside CS2 plus an observational CS2 session; documented moderation on/off only, by adapter/driver. Measure RTT/jitter/loss, DPC/CPU, p99/p99.9 frame time, and CS2 raw loss/jitter HUD; do not capture or alter game packets. Repeat under clean and CPU-bound conditions. ([Microsoft interrupt moderation](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/interrupt-moderation), [Valve telemetry](https://store.steampowered.com/news/app/730/view/4472731215261073715))

**Promotion gate:** require repeatable network benefit with no local frame/power/stability regression on that adapter class. Until then, **insufficient evidence** and no v1 control.

## F.16 Monitor scan-out, VRR range, overdrive, and OSD latency mode

**Question:** How much of the result is monitor processing/response/scan position, and which OSD state avoids overshoot across VRR?

**Experiment:** fixed optical sensor at top/middle/bottom, at least 200 events per position, after a fixed one-hour panel warm-up; several refresh rates, fixed/VRR, near VRR floor/ceiling, and every manufacturer-documented overdrive/low-latency mode. Capture high-speed transition/overshoot and brightness/flicker as well as click-to-photon.

**Promotion gate:** publish model/firmware/refresh-specific guided advice only. Never convert an OSD marketing label into a measured number or automate DDC/CI before fault testing.

## F.17 Driver-update and regression policy

**Question:** When should the product prefer a new driver versus preserve a known-good one?

**Experiment:** for each reference GPU, preserve old A/A results, update normally, reboot, re-scan reset settings, execute the shader stabilization protocol, then repeat. Record release-note relevance, crashes/resets, presentation paths, PCL/optical latency, frame tails, power, and all profile changes.

**Promotion gate:** recommendations attach to explicit version ranges and relevant fixes/known issues. “Latest is fastest” is never a rule; a security-supported floor may still be required independently of performance.

## F.18 Launch options and renderer validation

**Question:** Is any proposed launch option currently supported by Valve and does it have a repeatable causal effect?

**Experiment:** first require current Valve documentation and a supported semantics test. Only then compare clean launch versus the single flag. Renderer comparison gets separate caches/warm-up, present-path verification, and stability coverage. Undocumented flags are not tested in the consumer product because testing would legitimize folklore.

**Promotion gate:** both current primary documentation and replicated benefit are required. Otherwise the product says **insufficient evidence** or **exclude**.

## F.19 WPR/PresentMon coexistence and diagnostic trace design

**Question:** Can one unified ETL replace competing live collectors without metric loss or observer effect?

**Experiment:** compare PresentMon live capture, WPR separate capture, concurrent sessions, and a custom unified ETL analyzed offline with PresentMon’s documented ETL path. Measure event loss, CPU/disk overhead, metric equality, trace size, and privacy content across CPU/GPU-bound loads.

**Promotion gate:** expert WPR mode remains separate until a unified profile shows equivalent results, zero event loss, acceptable overhead, bounded trace size, and reviewed redaction.

## F.20 Rollback, privacy, and anti-cheat compatibility fault testing

**Question:** Does the safety architecture fail closed under real faults and product/game updates?

**Experiment:** terminate the UI/worker/broker or reboot between every journal step; externally change a value before rollback; simulate display loss, missing backup, denied access, disk-full/partial write, game/driver update, corrupted state, and duplicate transaction. Audit all file/network activity with analytics off/on. On every app/PresentMon/CS2 update, smoke-test the documented ETW-only workflow and verify no write-capable CS2 handle, module injection, input generation, packet access, or security mutation.

**Promotion gate:** exact-state recovery or an explicit non-destructive conflict in every case; zero unconsented egress; no kernel/injection/input/packet behaviour. A pass is version-specific compatibility evidence, not Valve approval.

## F.21 Streamlined Push to Talk cold-start hitch and resource cost

**Question:** Does the current CS2 setting still remove a reproducible first-push hitch, and does it create any material idle-resource, device, or audio regression?

**Experiment:** supported setting Off/On in at least ten randomized cold-launch pairs per audio-stack class: USB headset, motherboard audio plus analog microphone, and a common wireless endpoint where stable. Keep endpoint, sample format, scene, and voice configuration fixed. A human performs one push-to-talk event at a scripted time in a private/local test context; capture the surrounding PresentMon frame-time window and a short WPR audio/DPC trace without recording microphone payload. Add Off/Off A/A cold-launch pairs, idle CPU/power observations, a 30-minute enabled soak, endpoint reconnect, sleep/resume, and voice-function checks. ([Valve release notes](https://steamcommunity.com/ogg/730/announcements/detail/4191242835348964245))

**Promotion gate:** expose the card only while the setting exists in the tested build. Recommend a trial only for push-to-talk users when it produces a reproducible first-use hitch reduction **and** no guardrail regression on that audio class. Otherwise preserve the current state and say **insufficient evidence**.

## F.22 Packet-loss/jitter buffering suitability threshold

**Question:** At which Valve HUD missed-tick rate does a supported buffering increment reduce network hitches enough to justify its documented added receive margin?

**Experiment:** use two physical clients and a user-owned private CS2 server in an isolated laboratory. Capture the current UI labels verbatim and compare baseline, first increment, and second increment; analyze the labels separately from Valve’s documented increase of one or more ticks’ worth of receive margin. A protocol-agnostic external appliance—not the app—runs twelve preregistered link cells: base RTT 20 or 60 ms × random loss 0 or 0.5% × peak one-way jitter bursts 0, 10, or 25 ms. It may delay/drop delivery but never inspect, alter, replay, or synthesize payloads. Test upstream and downstream settings separately. An external actuator physically operates client A; an optical sensor on client B detects a fixed remote-player luminance transition, yielding a cross-client event-to-visible distribution without software input or packet access. Use at least 200 events per condition across ten randomized blocks, with a five-minute HUD observation window per block. Primary endpoints are missed-tick percentage and remote-event p95/p99; guardrails are the median event-to-visible increase, hitch rate, frame tails, loss, stability, and voice/input function. Ping is recorded only as network RTT—it cannot verify engine buffering. For reference, Valve’s current FAQ says CS2 uses 64 ticks/s, so one tick’s worth is 15.625 ms; treat that as the documented configured receive-margin increment, not a measured RTT change. Then attempt three time-interleaved same-server field sessions without induced impairment or packet access. ([Valve Source 2 Telemetry FAQ](https://help.steampowered.com/en/faqs/view/5E6F-5B36-5485-F6B9))

**Promotion gate:** the consumer product never controls the network and never enables buffering for a clean link. Preregister a candidate suitability threshold of at least a 0.1-percentage-point absolute and 50% relative missed-tick reduction in the lab, no guardrail regression beyond the documented configured delay, and the same direction in all three field sessions; these are research thresholds, not Valve recommendations. Even after passing, the card may only suggest a user-approved trial and must state the exact selected receive-margin trade-off. If field conditions cannot reproduce the lab direction, report **observational/insufficient evidence** and preserve the current state.

---

# Final scope decision

The credible product is narrow but useful: verify the display is actually running correctly; expose Valve/NVIDIA/AMD’s supported latency and VRR controls with honest goal trade-offs; detect real GPU saturation or background contention; run controlled, versioned local experiments; and make every mutation attributable and reversible.

The app should **not** become a registry-tweak launcher, service remover, driver tuner, packet tool, game configurator, or anti-cheat-adjacent overlay. Where a primary source or local experiment cannot resolve a claim, the correct product output is **insufficient evidence**.

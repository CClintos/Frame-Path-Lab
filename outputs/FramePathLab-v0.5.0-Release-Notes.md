# FramePath Lab 0.5.0 — download and run

The first build you can download and run without installing anything. The .NET runtime is bundled,
so there is no SDK step and no runtime prerequisite. Extract the ZIP, run `FramePathLab.exe`.

This release is mostly corrections. Several features did not work at all, and one of them was
actively undoing settings it had just been told to apply.

## Fixed — behaviour you would have noticed

**Autotune reverted recommended defaults.** The keep/revert rule treats "no measured change" as a
reason to reverse, and the benchmark never moves the mouse — so the pointer acceleration card could
only ever measure as no change. Autotune applied it, failed to see it, and turned acceleration back
**on**, reporting that as evidence. Capture, telemetry and desktop transparency had the same
problem. Recommended defaults are now exempt from that verdict specifically; one that actually
widens the frame-time tails is still reversed, because that is a measurement the workload genuinely
made.

**Device disabling could not execute.** The mutation executor's write switch had no case for
`DeviceState`. Every device card captured its before-state, journalled a pending transaction, threw
on the write, and then threw again on the automatic rollback — reporting a failed revert for a
change that had never happened. `WriteDeviceState` existed and had no callers. The device tests
only covered the allowlist and the plan shape, so nothing caught it.

**The platform-timer gate could never open.** Two compounding faults. `bcdedit` only prints a boot
option that has been explicitly set, so on a healthy machine `useplatformclock` is simply absent —
which was read as "state unknown", the one condition that refuses. And the gate consulted a value
that is hardcoded to null by design, while the authoritative boot-configuration read sat a few
lines away and was discarded. The HPET card was unreachable on every machine, elevated or not.

**Two network adapter controls were dead on arrival.** `NET-PNPCAP-` and `NET-FLOW-` built write
plans that policy never classified, so both fell through to the diagnostic default, had their plans
stripped, and displayed a generic reason — reading as a deliberate exclusion rather than an
oversight.

**Network throttling could not be applied.** `NetworkThrottlingIndex` is written as 4294967295 and
reads back through a signed int as -1, so the read-back check called a correct write unverified and
rolled it straight back. Full-width DWords now round-trip.

## Changed — measurement honesty

**Isolate mode no longer chains baselines.** Each candidate's before-state was inherited from the
previous candidate's after-state, so the machine's warm-up drift accumulated and a candidate's
verdict depended on its position in the queue. Six identical runs on an untouched machine trend
+1.5%. Each candidate now measures an adjacent baseline, at a cost of one extra benchmark run.

**Conclusive now means the interval clears the threshold**, not merely excludes zero — which is
what its own documentation always claimed. A mean of 2.1% with an interval running 0.3% to 3.9% is
consistent with an effect far too small to act on.

**Early stopping is bounded and disclosed.** Re-examining the interval after every pair is optional
stopping, and the true false-positive rate sits above the nominal 5%. A positive verdict now needs
four pairs before it can end a run. Ruling an effect out early is unrestricted, since that costs a
false negative rather than a false claim.

**Tail verdicts are weighted, not counted.** Counting let two marginal 2% improvements outvote a
20% P99 regression and return "improved". P99 now carries the most weight. Frames-over-budget has
an absolute floor, because 0.05% of frames becoming 0.06% is a 20% relative "regression" and a
rounding error.

## Changed — safety

- A ledger failing its integrity check used to fall back to nothing, stranding every outstanding
  change with no way back through the app. It now falls back to the previous generation, which the
  same hash verifies, and names both files if neither survives.
- Registry restores keep the value kind already on the machine rather than coercing to the declared
  one. Kinds that cannot round-trip through text are refused at capture rather than written back as
  the literal string `System.Byte[]`.
- The dead process-mutation code is gone, along with its `OpenProcess` and `SetProcessInformation`
  imports. The catalogue refuses thread placement and EcoQoS on anti-cheat grounds; declaring those
  imports and never calling them still put them in the binary's import table.
- The stacked-cache card no longer tells a dual-die part that thread placement has nothing to choose
  between. On a part with one stacked die and one conventional die that is exactly backwards.

## Verification

72 tests pass, six of them new and each pinned to one of the faults above. The build is warning-clean
with `TreatWarningsAsErrors`.

```powershell
.\build.ps1
```

## This build is unsigned

No code-signing certificate. SmartScreen will warn; choose **More info → Run anyway**, or unblock
the ZIP in its Properties before extracting. Checksums are published alongside the download.

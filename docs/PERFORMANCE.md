# Performance

All numbers below were measured, not estimated, with the scripts in `scripts/`. They describe one machine; treat them
as a baseline to compare against, not as guarantees.

## Test environment

Intel Core i7-12700KF (20 logical CPUs), 31.7 GB RAM, Windows 11 Pro 10.0.26200, .NET 10 SDK 10.0.401,
Avalonia 12.1.4, `win-x64` self-contained, trimmed (`PublishTrimmed`, `TrimMode=partial`), Release.
Publish size: 45 MB, 86 files (symbols excluded). Date: 2026-10-12. Other software (SignalRGB, Logitech G HUB, browsers)
was running, so memory/CPU figures include normal desktop noise.

## Method

* `scripts/measure-perf.ps1` — launches the exe, records time-to-window, then samples working set / private bytes
  every 500 ms and computes CPU% from `TotalProcessorTime` deltas ÷ wall time ÷ logical CPUs.
* `scripts/measure-effects.ps1` — starts `tools/FreeRGB.SimServer` (a **simulated** OpenRGB server, not hardware), gives
  FreeRGB an isolated data dir whose profile runs a software effect, and measures FreeRGB over a window. It also counts
  `UPDATELEDS` packets received by the simulator to prove frames really flow. It measures FreeRGB's own cost
  (computing frames + a loopback socket write), **not** real USB traffic or OpenRGB's cost.

## Results

| State | CPU (% of all cores) | Working set | Private | Notes |
|---|---|---|---|---|
| Launch → main window | – | – | – | 1.17 s (one run; earlier run 1.23 s) |
| First 5 s after launch, window open | 0.18 | 122 MB | 118 MB | 78 threads |
| Idle, window open (60 s), real OpenRGB absent | 0.025 | 120 MB | 132 MB | |
| First 5 s, tray-only (`--minimized`) | 0.05 | 95 MB | 77 MB | |
| **Idle in tray (60 s)** | **0.006** | **88 MB** | **67 MB** | the normal background state |
| After device discovery | – | – | – | measured only against the *simulated* server (below); real OpenRGB reported 0 devices |
| Software effect, 1×60 LEDs, 20 fps (40 s) | 0.014–0.03 (≈0.3–0.6 % of one core) | 88–92 MB | 67–69 MB | 20 packets/s confirmed at the simulator |
| Software effect, 4×300 LEDs, 30 fps (40 s) | 0.048 (≈1 % of one core) | 101 MB | 79 MB | 121 packets/s confirmed |
| Extended: 1×60 LEDs rainbow, 6 min | 0.014 | 92 MB | **66–70 MB, flat** | no growth beyond ~3 MB |
| Device rescan | – | – | – | **not measured** |

## What was found and fixed

* **Allocation growth in software effects.** The first 10-minute soak showed private memory growing 67 → 132 MB.
  Cause: a per-frame array clone plus two intermediate buffers per `UPDATELEDS` packet. Fix: swap two frame buffers
  and build the packet in one allocation. Re-run: flat (66–70 MB over 6 min), and `GCgen0MaxBudget` made no difference
  (so it was allocations, not GC tuning).
* **Debug symbols** (native `libSkiaSharp.pdb` ≈ 80 MB) were inflating publishes: 145 MB → 45 MB after excluding them.
  An untrimmed ReadyToRun publish was 220 MB.

## Honest limitations

* The tray-only idle footprint (~88 MB) is dominated by the Avalonia/Skia runtime that stays loaded even with no window.
  Tuning knobs tried without effect: `DOTNET_gcConcurrent=0`, `TieredPGO=0`, `GCgen0size`. A much smaller footprint
  would need a separate headless service process (not built; a possible future change).
* Idle CPU is dominated by the 5-second reconnect/status wake-up and is at the noise floor of the measurement.
* No Linux measurements exist.

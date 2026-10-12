# Testing

```
dotnet test FreeRGB.slnx
```

## What the automated tests cover (54 tests, xUnit)

All hardware-facing tests run against `FakeOpenRgbServer`, an in-process **simulation** of an OpenRGB SDK server.
They prove FreeRGB's protocol handling and logic. They do **not** prove any real device works.

| Area | Tests |
|---|---|
| Colour maths | hex parse/reject, HSV primaries, scale clamp |
| Protocol | header round-trip, bad magic / oversized packets rejected, `UPDATELEDS` layout, device (de)serialisation for protocol 0/3/5, truncated data → `InvalidDataException` not crash |
| Backend | unavailable when nothing listens; empty device list is "connected"; negotiation down to server version; server that never answers the version request → protocol 0; connection drop is reported, reconnect works; `DEVICE_LIST_UPDATED` triggers rescan; out-of-range values rejected before sending |
| Lighting | static colour via Static mode; off on Direct-only device sends black; hardware speed maps onto an inverted device range (found a real bug); per-LED-colour hardware effect sends its colour via LEDs (found on real hardware); unsupported effect rejected and not sent; device with no LEDs reported "detected, not controllable"; software effect streams frames then stops its timer; settings for an absent device are kept and applied when it appears; effect list reflects real capabilities |
| Zones | saved LED count resizes an addressable header (device had 0 LEDs), rescans, then applies colour with the new count; out-of-range / non-resizable resize rejected |
| Identify mode | other devices get black, target device red, saved settings restored afterwards |
| LED-count wizard | search finds every count 0–100 in ≤ 7 questions and never probes a non-existent LED; probe sizes the zone to max and lights exactly one LED |
| Profiles | save/load, export/import (no silent overwrite), 8 invalid-input cases rejected, oversized file rejected, names cannot escape the store directory, settings normalisation rejects non-loopback hosts |
| Platform | XDG desktop entry content; Windows HKCU Run key round-trip (throwaway value name); second instance refused and signals the first; lock released on exit |

## Manual / scripted checks

| Check | How | Where recorded |
|---|---|---|
| Launch, window, single instance | run twice, count processes | `docs/WORKLOG.md` |
| Real OpenRGB handshake & enumeration | `FreeRGB --diagnose` against a real OpenRGB 1.0 server | `docs/WORKLOG.md` |
| Real hardware, visually observed | `tools/FreeRGB.HwTest` (writes to real devices, then restores them) | `docs/HARDWARE.md` |
| CPU/memory, idle & tray-only | `scripts/measure-perf.ps1` | `docs/PERFORMANCE.md` |
| CPU/memory with a software effect (simulated server) | `scripts/measure-effects.ps1` | `docs/PERFORMANCE.md` |

## Not yet verified (be sceptical of anything not listed above)

* Real hardware beyond the Logitech G502 HERO (see `docs/HARDWARE.md`); the GUI has not been driven against real hardware.
* Linux: running the UI, tray, autostart, permission handling (CI only builds, tests and starts the binary headless).
* Tray menu behaviour and "start at sign-in" end to end on Windows (registry write implemented; see worklog for what was checked).
* Behaviour when another RGB program is actively managing a device.

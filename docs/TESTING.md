# Testing

```
dotnet test FreeRGB.slnx
```

## What the automated tests cover (47 tests, xUnit)

All hardware-facing tests run against `FakeOpenRgbServer`, an in-process **simulation** of an OpenRGB SDK server.
They prove FreeRGB's protocol handling and logic. They do **not** prove any real device works.

| Area | Tests |
|---|---|
| Colour maths | hex parse/reject, HSV primaries, scale clamp |
| Protocol | header round-trip, bad magic / oversized packets rejected, `UPDATELEDS` layout, device (de)serialisation for protocol 0/3/5, truncated data → `InvalidDataException` not crash |
| Backend | unavailable when nothing listens; empty device list is "connected"; negotiation down to server version; server that never answers the version request → protocol 0; connection drop is reported, reconnect works; `DEVICE_LIST_UPDATED` triggers rescan; out-of-range values rejected before sending |
| Lighting | static colour via Static mode; off on Direct-only device sends black; hardware speed maps onto an inverted device range (found a real bug); unsupported effect rejected and not sent; device with no LEDs reported "detected, not controllable"; software effect streams frames then stops its timer; settings for an absent device are kept and applied when it appears; effect list reflects real capabilities |
| Profiles | save/load, export/import (no silent overwrite), 8 invalid-input cases rejected, oversized file rejected, names cannot escape the store directory, settings normalisation rejects non-loopback hosts |
| Platform | XDG desktop entry content; Windows HKCU Run key round-trip (throwaway value name); second instance refused and signals the first; lock released on exit |

## Manual / scripted checks

| Check | How | Where recorded |
|---|---|---|
| Launch, window, single instance | run twice, count processes | `docs/WORKLOG.md` |
| Real OpenRGB handshake & enumeration | `FreeRGB --diagnose` against a real OpenRGB 1.0 server | `docs/WORKLOG.md` |
| CPU/memory, idle & tray-only | `scripts/measure-perf.ps1` | `docs/PERFORMANCE.md` |
| CPU/memory with a software effect (simulated server) | `scripts/measure-effects.ps1` | `docs/PERFORMANCE.md` |

## Not yet verified (be sceptical of anything not listed above)

* Any real RGB device being controlled through FreeRGB.
* Linux: running the UI, tray, autostart, permission handling (CI only builds, tests and starts the binary headless).
* Tray menu behaviour and "start at sign-in" end to end on Windows (registry write implemented; see worklog for what was checked).
* Behaviour when another RGB program is actively managing a device.

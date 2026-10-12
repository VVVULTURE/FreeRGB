# Development log

Newest information last. Everything marked ✔ has evidence in this log; "unverified" means it does not.

## 2026-10-12 — Phase 1: environment & research

**Environment (inspected):** Windows 11 Pro 10.0.26200; git 2.55, GitHub CLI 2.101 (authenticated), Node 26, Python 3.13,
.NET SDK 10.0.302/303/401. **Not installed:** Rust, Go, CMake, Visual Studio C++ tools. WSL present but with no distro,
so no local Linux.
**Hardware seen (Windows enumeration):** ASRock B760M-HDV/M.2; Team Group UD5-5600 ×2; RTX 5070; Logitech G502 HERO
(`046D:C08B`); Logitech G432 headset (`046D:0A9C`); an ASRock USB HID device (`26CE:01A2`).
**Other RGB software running:** SignalRGB (+service) and Logitech G HUB.

**OpenRGB research (from its repository docs and release data):**
* v1.0 released 2026-09-11; license GPL-2.0-or-later; Windows/Linux/macOS; binaries are on Codeberg (was returning
  HTTP 503 for ~1 hour during this session; retried successfully later).
* SDK: TCP port 6742, 16-byte `ORGB` header, version negotiation (`REQUEST_PROTOCOL_VERSION`), docs describe versions up
  to 6. RGBColor is `0x00BBGGRR` (bytes R,G,B,0). Enumerations (device types, mode flags, directions, color modes)
  taken from `RGBControllerAPI.md`.
* ✔ Windows zip downloaded (SHA-256 `182A52A3C97C4C4AE52C80286B4260C9666C51C3447DBC416EE8945F66192E90`, not compared
  against a publisher checksum — none found). CLI flags `--server --noautoconnect --startminimized --config <dir>`
  were accepted (port opened, config written to the given dir). `--help` prints nothing on the Windows GUI build, so
  the flags were verified by behaviour only. `--server-port` is unverified.
* ✔ OpenRGB's generated config lists 1,891 detectors, including "Logitech G502 HERO Gaming Mouse" and
  "ASRock Polychrome USB".

## Phase 2–3: architecture & implementation

See `docs/ARCHITECTURE.md`. Core library, OpenRGB backend (protocol ≤ 5), lighting service with software effects,
profiles, settings, platform helpers, Avalonia UI with tray, diagnostics, `--diagnose` CLI.

## Phase 4: hardware — what actually happened

* ✔ **Real OpenRGB 1.0 server:** `FreeRGB --diagnose` → `Connected to OpenRGB (protocol 5), 0 device(s)`. So the
  handshake, version negotiation and controller-count request work against the real server.
* ✘ **0 devices.** OpenRGB (private config dir, no PawnIO driver installed) logged HID connections for the Logitech
  mouse interfaces but never produced a device, although detectors for the G502 HERO and ASRock Polychrome USB are
  enabled. SMBus access failed as expected ("Could not open PawnIO") so RAM/motherboard-SMBus devices were not
  probed. **Likely cause (unconfirmed):** SignalRGB and/or Logitech G HUB hold those HID devices. Confirming requires
  closing them; not done without the owner's go-ahead.
* **No command has been written to any physical device** at any point so far.
* Hardware results therefore: **nothing verified.** See `docs/HARDWARE.md`.

## Phase 5: OS integration (Windows ✔, Linux unverified)

✔ (scripted, Windows 11, final code):
1. `FreeRGB --minimized` → process alive, **no visible window**.
2. Second launch exits immediately (exit code 0), and the first process's window becomes visible.
3. Close button with close-to-tray on → process alive, window hidden.
4. Relaunch after hiding → window restored; only one process for the exe.
5. Close-to-tray off → closing exits with code 0, lock file removed, immediate relaunch works.
6. HKCU Run entry write/read/delete round-trip (unit test, with a throwaway value name).

Not done: visually confirming the tray icon and clicking its menu; an actual sign-out/sign-in cycle.
Linux: nothing run locally. CI builds, tests, and starts the published binary under xvfb (see Actions).

## Phase 6: performance

See `docs/PERFORMANCE.md`. A soak test exposed per-frame allocation growth in software effects (67 → 132 MB in 10 min);
fixed; re-run is flat.

## Bugs found by testing and fixed

| Bug | Found by | Fix |
|---|---|---|
| Devices reporting `speedMin > speedMax` (lower = faster) treated as having no speed control | unit test `HardwareEffectMapsSpeedOntoDeviceRange` | `HasSpeed`/`HasBrightness` use `!=` |
| Single-instance "show window" pipe was per-user, so two instances with different data dirs collided (and a second launch raised the wrong window) | unit test failed while a measurement process was running | pipe name derived from the data directory |
| Per-frame allocation growth in software effects | 10-minute soak | buffer swap + single-allocation packet |
| Publish was 145–220 MB | measurement | exclude `.pdb`, trim → 45 MB |

## Known limitations / open questions

* No physical device verified (see above). Needs: close SignalRGB / G HUB (or find a machine without them), rerun
  `FreeRGB --diagnose`, then test one device end to end.
* Linux UI, tray (needs a StatusNotifier host), autostart and udev behaviour unverified.
* Windows: tray icon visuals/menu clicks and a real sign-in autostart unverified.
* Rescan resource cost not measured.
* OpenRGB protocol v6 features (per-zone modes, segment matrices) not implemented; v6 servers negotiate down to 5.
* The app does not detect *which* other program is holding a device; it only lists known RGB programs that are running.
* Memory: ~88 MB idle in the tray (Avalonia/Skia baseline).

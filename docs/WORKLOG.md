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
* In this first pass no command had been written to any physical device.
* Hardware results from this first pass: nothing verified. (Superseded by the update below.)

### Update - real hardware (same day, after the owner approved closing SignalRGB / G HUB)

The SignalRGB app turned out to be already closed (only its Windows service remained; left untouched. G HUB left running).
With it closed: OpenRGB registered "G502 HERO Gaming Mouse" and "ASRock B760M-HDV/M.2", and `FreeRGB --diagnose` listed both with
real modes (G502 speed range `200..10`, the inverted-range case the unit test had caught). `tools/FreeRGB.HwTest` then ran
a sequence through `LightingService`; every command returned OK, and the owner watched the mouse:
* Seen: static green / blue / dim white / orange, per-zone colours, a cycling rainbow; in a retest: red, white, off.
* **Not seen: hardware Breathing -> bug.** Breathing is a per-LED-colour mode and FreeRGB never sent its colour. Fixed,
  unit test added, re-verified visually (red breathing). Spectrum Cycle seen.
* A first run was missed (owner not watching) and one retest was invalid because the harness left the light switched off;
  both were repeated. OpenRGB exited by itself between runs twice (cause unknown; possibly its warning dialog being closed).
* ASRock board: commands accepted, nothing visibly connected to its headers, so no visual verification.
* Device state restored after every run (G502 back to mode "Off"; ASRock back to "Direct" with its original colours).
* Not confirmed by eye: software Rainbow distinct from Spectrum Cycle; the speed slider.

### Update - PawnIO / SMBus (owner asked to fix OpenRGB's PawnIO warning dialog)

`winget install namazso.PawnIO` -> PawnIO 2.2.0 installed, service `PawnIO` Running (UAC approved by the owner). OpenRGB
restarted elevated (UAC approved). Log: "PawnIO initialized successully" (SmbusI801, SmbusNCT6793), no warning dialog.
`FreeRGB --diagnose` now lists 4 devices: 2x ENE DRAM (I2C 0x70/0x72), G502 HERO, ASRock board. No commands were sent to the RAM.

### Update - zone sizes, headless OpenRGB, one-click setup

Owner screenshot: OpenRGB "Zone Initialization" dialog (addressable headers' LED counts can't be auto-detected) and a request to
build PawnIO/other dependencies into FreeRGB.
* Verified against OpenRGB 1.0: `OpenRGB --server --server-port N --config DIR` runs **with no window at all** (MainWindowHandle 0,
  port opens); adding `--startminimized` opens the GUI (which is what showed both dialogs). `--server-port` and `--config` work.
* New: SDK `RESIZEZONE` (packet 1000) via `ResizeZoneAsync`; `DeviceSetting.ZoneSizes`; applied before colours (rescan after).
  Real server: ASRock zones 'Addressable Header 1/2' have min 0, max 100, default 80; resize to 24 gave 106 LEDs, restored to 80 -> 162.
  A device whose only LEDs are on a 0-LED header is now "controllable, needs setup" instead of "not controllable" (found by a unit test).
* PawnIO licence check: pawnio.eu says "(c) namazso, all rights reserved", code "open-source" without naming a licence, custom licensing
  on request. No redistribution grant found, so **PawnIO is not embedded**; FreeRGB downloads the author's installer (winget, or
  github.com/namazso/PawnIO.Setup 2.2.0, SHA-256 `1F519A22...3032`, Authenticode signature Valid / namazso.eu).
* New `HardwareSetup`: downloads OpenRGB 1.0 (pinned SHA-256), one elevated script (UAC): PawnIO + scheduled task `FreeRGB-OpenRGB`
  (highest privileges, at logon). Run end to end through a harness (not via the GUI button): hash verified, UAC approved, exit 0,
  status all green; then `schtasks /run` (no prompt) started OpenRGB elevated and headless; `FreeRGB --diagnose` saw 4 devices incl.
  both RAM modules (8 LEDs each), log "PawnIO initialized successully".
* Not done: clicking the GUI button; a sign-out/in cycle with the task; sending any light command to RAM.

### Update - LED-count wizard, RAM test

* Owner's fans are powered by Molex and the case is off-brand; the LED count of addressable headers cannot be read from hardware.
  New: `LedCountSearch` (binary search, <= 7 yes/no answers for 0-100 LEDs; unit test checks every count 0..100) and
  `LightingService.ProbeZoneAsync` (zone sized to max, exactly one LED lit, other zones blanked), surfaced in the GUI as
  *Zones -> Find automatically...* with a "is anything connected?" pre-check and a final confirmation. GUI wizard not yet run on real hardware.
* RAM test (2 x ENE DRAM, via FreeRGB.HwTest, owner watching): static colours, dim white, off, orange, Spectrum Cycle, Breathing seen;
  first report had off/orange swapped, retest in 8 s steps matched exactly. See docs/HARDWARE.md.

* **GUI crash found on first real-device launch:** `NullReferenceException` in the device-list template (Avalonia built the template with a
  null item) killed the app moments after it appeared (WER event + stack trace in the Application log). Earlier GUI runs had no devices so
  never hit it. Fixed with a null guard; an unhandled-exception logger now writes to `freergb.log`. Re-checked: the app stays up with 4
  devices, selecting the ASRock board works, the Zones section renders. Zones moved directly under Effect and opens by default.
  Lesson: GUI had only been tested with an empty device list before.

### Update - ASRock fans, solo mode, case fans

* Owner: BIOS sets the fans to white; only the two CPU-cooler fans ever reacted, not the three case fans.
* Found by experiment (owner watching): hardware **Static** works and overrides the BIOS white; Direct mode did not change the fans (first
  "Direct red works" reading was a leftover from the previous Static step). Fix: solid colours and off now use hardware Static (per-LED
  Static) when a device has it (`PerLedStaticMode`), black = off (the board's own Off mode didn't darken the fans).
* Per-header isolation (one header red, others dark, left on until answered): CPU fan 1 = Addressable Header 1, CPU fan 2 = Addressable
  Header 2; RGB LED 1/2: no fan. Hardware Breathing, Rainbow, Wave, Strobe all changed the CPU fans.
* LED-count wizard result was unusable: the saved log showed answers 0.15-0.6 s apart (too fast to look) and a count of 100 (the max) for
  both headers; a ValueChanged side effect had also written 100/100 into the profile without any user action. Fixed (sizes only saved on
  real edits/wizard confirm), answers are delayed 2.5 s, and the wizard first checks whether the header can show single LEDs at all.
  On this board each header behaves as one colour, so the count is irrelevant for static colours and hardware effects.
* New: Identify mode (everything else dark, per device and per header, Restore button). Unit-tested; not yet clicked by the owner.
* Case fans: no board header reaches them, no USB RGB controller in `Get-PnpDevice` (only mouse, keyboard, headset, ASRock LED chip,
  fingerprint reader, BT radio, storage), no case button, lighting cable goes straight to Molex => fixed-colour fans, not software-controllable.

### Update - release packaging

`.github/workflows/release.yml` (tag `v*` -> release; manual run = dry run). First dry run failed in both smoke tests, which is
what they are for: (1) Windows step reported failure only because `--diagnose` exits 1 without OpenRGB and PowerShell carried that
exit code; (2) in a bare ubuntu:24.04 container `ldd` flagged `liblttng-ust.so.0` (optional .NET tracing lib; the app ran fine) -> check now
ignores that one library. Second run all green: portable exe runs, Inno installer silent install/run/uninstall, tar.gz and
AppImage run (AppImage GUI up for 8 s under xvfb), .deb installs/runs/removes in a bare container. Licence: kept MIT (no OpenRGB code is
linked; all bundled packages are MIT/BSD, see THIRD-PARTY-NOTICES.md). Not signed; not tested on a real Linux desktop.

## Phase 5: OS integration (Windows ✔, Linux unverified)

✔ (scripted, Windows 11, final code):
1. `FreeRGB --minimized` → process alive, **no visible window**.
2. Second launch exits immediately (exit code 0), and the first process's window becomes visible.
3. Close button with close-to-tray on → process alive, window hidden.
4. Relaunch after hiding → window restored; only one process for the exe.
5. Close-to-tray off → closing exits with code 0, lock file removed, immediate relaunch works.
6. HKCU Run entry write/read/delete round-trip (unit test, with a throwaway value name).

Not done: visually confirming the tray icon and clicking its menu; an actual sign-out/sign-in cycle.
Linux: nothing run locally. ✔ GitHub Actions (ubuntu-latest, run on commit "docs: correct test count"): build OK,
**47/47 tests pass on Linux** (including the single-instance pipe test), trimmed `linux-x64` publish OK, and the published
binary was still running after 8 s under `xvfb-run` (UI process starts; says nothing about tray, autostart or devices).
Windows runner: build OK, 47/47 tests pass.

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

* Only the G502 HERO is hardware-verified, and through the Core API, not the GUI. Not verified: ASRock board LEDs (nothing connected), RAM, GPU (need PawnIO / admin), headset.
* Linux UI, tray (needs a StatusNotifier host), autostart and udev behaviour unverified.
* Windows: tray icon visuals/menu clicks and a real sign-in autostart unverified.
* Rescan resource cost not measured.
* OpenRGB protocol v6 features (per-zone modes, segment matrices) not implemented; v6 servers negotiate down to 5.
* The app does not detect *which* other program is holding a device; it only lists known RGB programs that are running.
* Memory: ~88 MB idle in the tray (Avalonia/Skia baseline).

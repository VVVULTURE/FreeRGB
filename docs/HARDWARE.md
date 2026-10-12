# Hardware & platform compatibility

FreeRGB does not talk to RGB hardware itself. It controls whatever **OpenRGB** (the SDK server) detects and exposes.
So FreeRGB's hardware coverage is OpenRGB's coverage (see <https://openrgb.org/devices.html>), and every device
still has to be verified individually with FreeRGB.

## Status levels

| Level | Meaning |
|---|---|
| **Verified** | Tested with the actual hardware through FreeRGB. Details recorded below. |
| **Documented support** | OpenRGB's device list documents the device, but this exact configuration has not been tested with FreeRGB. |
| **Experimental** | Implementation exists, behaviour not fully verified. |
| **Unsupported** | No working integration. |
| **Untested** | Not enough evidence to classify further. |

Simulated hardware (the test suite's fake OpenRGB server) is **never** counted as hardware testing.

## Verified hardware

Test setup for every row: Windows 11 Pro 10.0.26200 · OpenRGB 1.0 (commit 81bbe18, SDK protocol 5) run as a normal user in
headless server mode, **without** the PawnIO driver · FreeRGB's `LightingService` driven by `tools/FreeRGB.HwTest`
(the real Core code, not the GUI) · results are what the owner **saw with their own eyes** (OpenRGB has no read-back).
Date 2026-10-12.

| Manufacturer / model | Revision / firmware | Connection | Backend | Verified (observed) | Not verified / limits |
|---|---|---|---|---|---|
| Logitech G502 HERO | not reported by OpenRGB | USB HID `046D:C08B` | OpenRGB 1.0 SDK | static red, green, blue, orange, white; brightness scaling (25 % white visibly dim); lighting **off**; per-zone colours (Primary / Logo zones different); hardware **Spectrum Cycle**; hardware **Breathing** (red), after fixing a bug found by this test | software "Rainbow": a cycling rainbow incl. the side lights was seen, but the owner could not tell it apart from Spectrum Cycle, so it is not separately confirmed. GUI not used for this run. Speed slider not checked visually. |

Status: **Verified** for the features in the "Verified" column; everything else **Untested**.

### What the first hardware pass found

* With the SignalRGB app running, OpenRGB found **0 devices**; with it closed, OpenRGB registered both the G502 HERO and the
  ASRock board. "Only one program can drive a device" is a real, observed constraint on this machine.
* Hardware *Breathing* on the G502 is a per-LED-colour mode; FreeRGB wasn't sending the colour for such modes, so it
  breathed black. Fixed in `LightingService` (+ unit test `PerLedColourEffectSendsColourViaLeds`) and re-verified visually.
* OpenRGB shows a "PawnIO failed to initialize" dialog on start without the PawnIO driver. That is OpenRGB's own dialog.
  **Fix applied on the dev machine:** `winget install namazso.PawnIO` (PawnIO 2.2.0, installer hash verified by winget,
  one UAC prompt), then start OpenRGB **elevated** (it needs administrator rights to open the driver). Result: the dialog
  is gone, OpenRGB's log shows "PawnIO initialized successfully" for the Intel i801 SMBus and the previously invisible
  **two ENE DRAM modules** are now detected (I2C 0x70 and 0x72). FreeRGB itself still runs unelevated and talks to the
  elevated OpenRGB over loopback. (OpenRGB logs two harmless errors for the `SmbusIntelSkylakeIMC` module, which does
  not apply to this CPU.) Trade-off: PawnIO is a third-party freeware kernel driver, and an elevated OpenRGB means a UAC
  prompt at each start unless you run it as a service/scheduled task, which FreeRGB does not set up for you.

## Dev-machine inventory

Identified from Windows device enumeration. Only the G502 HERO has a verified result.

| Device | Category | Connection | Notes |
|---|---|---|---|
| ASRock B760M-HDV/M.2 | Motherboard | USB HID `26CE:01A2` | OpenRGB registered it (162 LEDs, 4 zones: 2 RGB headers, 2 addressable headers with 80 LEDs each) and accepted every FreeRGB command, but nothing visible is connected to the headers on the dev machine, so there is no visual result. Untested. |
| Team Group T-Force UD5-5600 ×2 | RAM | SMBus/I²C | After installing PawnIO, OpenRGB detects two "ENE DRAM" controllers (8 LEDs each, `Direct` mode; FreeRGB lists them as controllable, verified through the FreeRGB-launched headless setup). **No lighting command has been sent to them yet**, so Untested. |
| Logitech G502 HERO | Mouse | USB HID `046D:C08B` | **Verified** (see above). |
| Logitech G432 headset | Headset | USB HID `046D:0A9C` | No matching OpenRGB detector name seen in its list. Untested. |
| NVIDIA GeForce RTX 5070 | GPU | NVAPI I²C | OpenRGB registered an "Nvidia NvAPI I2C" interface for it; no device reported. Untested. |

## Per-device report format (add rows here when verified)

| Manufacturer / model | Revision / firmware | Connection | OS | Backend | Features tested | Result / limits |
|---|---|---|---|---|---|---|

## Device categories

All of these are **Untested** in FreeRGB; coverage depends on OpenRGB.

Motherboards · RAM · fans (via motherboard/controller headers) · RGB/ARGB controllers & hubs · LED strips · graphics cards ·
CPU coolers/pumps · keyboards · mice · headsets · mousepads · other USB accessories.

## Platform status

| Platform | Built | Unit tests | App launched | Tray / autostart | Notes |
|---|---|---|---|---|---|
| Windows 11 (dev machine) | yes | yes | yes | see `docs/WORKLOG.md` | Single-instance behaviour verified manually. |
| Linux x64 | yes (CI, ubuntu-latest) | yes, 47/47 (CI) | CI: still running after 8 s under xvfb | **unverified** | No Linux machine/VM available to the author. Tray requires a StatusNotifier host; autostart writes `~/.config/autostart/freergb.desktop`. |

## Permissions & drivers (all handled by OpenRGB, not FreeRGB)

* **Windows:** USB/HID devices need no driver. SMBus/I²C devices (most RAM, many motherboards) need OpenRGB's PawnIO driver
  (installed separately by the user, requires administrator approval once). FreeRGB itself never needs administrator rights.
* **Linux:** install OpenRGB's udev rules and, for SMBus, load `i2c-dev` and grant access (see OpenRGB's `UdevRules.md`
  and `SMBusAccess.md`). Never run FreeRGB as root.
* **Other RGB software:** two programs driving one device conflict. Close the other program or disable its lighting.

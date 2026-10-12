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
| Team Group T-Force UD5-5600 ×2 (OpenRGB: "ENE DRAM", 8 LEDs each) | not reported | SMBus/I²C, addresses 0x70 and 0x72 (Intel i801 via PawnIO 2.2.0) | OpenRGB 1.0 SDK, run elevated | static red, green, blue, dim white (25 %), white, **off**, orange; hardware **Spectrum Cycle**; hardware **Breathing** (red). Both sticks reacted together. | Per-stick control, speed, and the other modes (Flashing, Rainbow, Chase…) not tried. A first run was reported with off/orange swapped; a slower retest (8 s steps) matched exactly white → dark → orange, so the first report was an observation slip, but it was not re-checked with a timer. GUI not used. |
| ASRock B760M-HDV/M.2 (OpenRGB "Polychrome USB") | not reported | USB HID `26CE:01A2`; two 12 V RGB headers + two 5 V addressable headers; the two **CPU-cooler fans** are on *Addressable Header 1* (fan 1) and *Addressable Header 2* (fan 2) | OpenRGB 1.0 SDK | hardware **Static** colour per header (red, blue, white, dark = off), overriding the BIOS white; hardware **Breathing, Rainbow, Wave, Strobe** all visibly changed the CPU fans; per-header isolation (one header red, others dark) | **Direct per-LED mode: not observed to change the fans** (streamed 3-LED test left them white), so per-LED and software effects are not usable here; each header acts as one colour. Hardware "Spectrum Cycle" and "Off" modes did not visibly affect the fans in the first test (Spectrum Cycle not re-tested; "off" works as Static black). RGB LED 1/2 headers have nothing connected. After a reboot the BIOS white returns until FreeRGB is running again. |

Status: **Verified** for the features in the "Verified" column; everything else **Untested**.

### Case fans (3 ×, off-brand case) — not controllable from software

Observed: none of the four board headers changed them, no USB RGB controller exists in the PC, the case has no RGB button, and
each fan's thin lighting cable goes **straight to a Molex power adapter**. That means a built-in fixed-colour (white) light with
no control wire. FreeRGB cannot change it. Options that need hardware: (a) replace them with ARGB fans (3-pin 5 V) and connect
their hub/splitter to an addressable header (both are currently used by the CPU-cooler fans, so chain the cooler fans or use a
splitter); (b) an ARGB controller/hub that OpenRGB supports; (c) unplug the Molex lighting adapter to switch their light off.

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
| Team Group T-Force UD5-5600 ×2 | RAM | SMBus/I²C | **Verified** (see above). |
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

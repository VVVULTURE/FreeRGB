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

*None yet.* No physical device has been successfully controlled through FreeRGB. See `docs/WORKLOG.md` for what has
been checked and why (summary: on the only dev machine a real OpenRGB 1.0 server connected and negotiated protocol 5,
but reported 0 devices while SignalRGB and Logitech G HUB were running).

## Dev-machine inventory (all **Untested**)

Identified from Windows device enumeration; none have been controlled through FreeRGB yet.

| Device | Category | Connection | Notes |
|---|---|---|---|
| ASRock B760M-HDV/M.2 | Motherboard | USB HID `26CE:01A2` appears present (ASRock) | OpenRGB lists an "ASRock Polychrome USB" detector, enabled by default. Not detected in the one run so far. |
| Team Group T-Force UD5-5600 ×2 | RAM | SMBus/I²C | Windows SMBus access needs OpenRGB's PawnIO driver, which was **not** installed on the dev machine. Untested. |
| Logitech G502 HERO | Mouse | USB HID `046D:C08B` | OpenRGB lists "Logitech G502 HERO Gaming Mouse". Not detected in the one run so far. |
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
| Linux x64 | CI only | CI only | CI headless smoke test only | **unverified** | No Linux machine/VM available to the author. Tray requires a StatusNotifier host; autostart writes `~/.config/autostart/freergb.desktop`. |

## Permissions & drivers (all handled by OpenRGB, not FreeRGB)

* **Windows:** USB/HID devices need no driver. SMBus/I²C devices (most RAM, many motherboards) need OpenRGB's PawnIO driver
  (installed separately by the user, requires administrator approval once). FreeRGB itself never needs administrator rights.
* **Linux:** install OpenRGB's udev rules and, for SMBus, load `i2c-dev` and grant access (see OpenRGB's `UdevRules.md`
  and `SMBusAccess.md`). Never run FreeRGB as root.
* **Other RGB software:** two programs driving one device conflict. Close the other program or disable its lighting.

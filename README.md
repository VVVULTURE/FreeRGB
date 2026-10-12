# FreeRGB

A free, open-source, lightweight RGB lighting controller for **Windows 11** and **Linux** with one simple interface:
pick a device, pick a colour or effect, done. No account, no telemetry, no manufacturer-specific app per component.

> **Status: early / pre-release.** The application, profile system, tray/startup behaviour and OpenRGB integration are
> implemented and tested against a *simulated* OpenRGB server and a real OpenRGB 1.0 server. **Two kinds of physical
> device (Logitech G502 HERO mouse, ENE-based DDR5 RAM) have been hardware-verified so far**, through the Core API rather than the GUI — see
> [Verified & unverified](#verified--unverified) and [docs/HARDWARE.md](docs/HARDWARE.md). Don't treat any other hardware
> as supported until it appears as *Verified* there.

## What it does

* Lists detected devices (name, vendor, type, location) and whether each is **controllable**, **detected but not
  controllable** (with the reason), or **not connected** (saved in the profile, absent now).
* Per-device controls: on/off, colour picker + exact hex, brightness, effect, effect speed, per-zone colours.
  Controls appear **only if the device really supports them** (taken from the device's own mode flags).
* Hardware effects exactly as the device reports them, plus software effects (Rainbow, Colour cycle, Breathing, Wave) on
  devices with a per-LED "Direct" mode.
* "All devices" bar to set one static colour on every device that can do it, or turn everything off.
* Named **profiles** (save / save as / load / delete / import / export, documented JSON format).
  Settings for devices that aren't present are kept and applied when they return.
* **System tray**: open, lights on/off, switch profile, rescan, settings, exit. Closing the window hides to the tray
  (configurable); tray → *Exit* really quits.
* **Start at sign-in**, minimised to the tray (no window pops up). Per-user, no administrator rights.
* Single instance: launching again just raises the existing window.
* Diagnostics tab and `FreeRGB --diagnose` for missing devices, permission problems and conflicting RGB software.

## How hardware compatibility works

FreeRGB does not contain device drivers. It talks to the **[OpenRGB](https://openrgb.org) SDK server**
(`127.0.0.1:6742`, loopback only), and OpenRGB does the device-specific work. OpenRGB 1.0 documents support for a very
large device list (<https://openrgb.org/devices.html>); FreeRGB's coverage is therefore OpenRGB's coverage.

**Windows: one-click setup.** *Settings → Hardware setup → Set up hardware support* downloads OpenRGB 1.0 (SHA-256 pinned),
installs the PawnIO driver (only needed for RAM and motherboard SMBus lighting; via winget or the author's signed installer,
hash pinned) and registers a scheduled task so OpenRGB starts elevated at sign-in without a prompt each time. Windows shows
one UAC prompt. Nothing is downloaded or installed until you press the button. FreeRGB then starts OpenRGB itself
(headless, no GUI, so none of OpenRGB's dialogs appear). Not embedded: PawnIO's site gives no redistribution terms, so FreeRGB
fetches it from its author instead of shipping it. OpenRGB is GPL-2.0-or-later and stays a separate program that FreeRGB talks to
over a documented network protocol; FreeRGB contains none of its code.

**Linux / manual:** install OpenRGB yourself and run `OpenRGB --server` (no `--startminimized`, which opens its GUI).

**Addressable (ARGB) headers:** hardware can't tell how many LEDs are plugged in. In the device's *Zones* section, enter the LED
count per header (e.g. what your fans have in total). FreeRGB saves it in your profile and applies it automatically, so OpenRGB's
"Zone Initialization" dialog never needs to be used. Don't know the count? Click *Find automatically…*: FreeRGB lights one LED at a time and asks if you can see it (about 7 yes/no questions), then asks you to confirm. A wrong count wastes data or leaves LEDs dark. Note: Molex only *powers* fan lights; colour control needs the separate small ARGB/RGB cable from the fans or the case's hub to the motherboard.

What OpenRGB needs for some devices (not FreeRGB): on Windows, RAM and many motherboards need OpenRGB's PawnIO
driver (one-time administrator approval); on Linux, udev rules (and `i2c-dev` for SMBus). Details:
[docs/HARDWARE.md](docs/HARDWARE.md), [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).

## Install & run

No packaged release has been published yet. Build from source (below), or download the CI build artifacts for
`win-x64` / `linux-x64` from the GitHub Actions run of the latest commit (self-contained, no .NET install needed).

1. Windows: run `FreeRGB.exe`, open *Settings → Hardware setup* and click the button. Linux: install OpenRGB and start `OpenRGB --server`, then run `./FreeRGB`.
2. Pick a device, set colours/effects. Addressable headers: enter the LED count under *Zones*.

### Start at sign-in

Settings → *Start FreeRGB when I sign in*.
* **Windows:** writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FreeRGB = "<path>\FreeRGB.exe" --minimized`.
  Turn it off in the same place. (Moving the exe? toggle the option off and on again.)
* **Linux:** writes `~/.config/autostart/freergb.desktop` (XDG autostart). Honoured by most desktop environments;
  **not yet verified on a real Linux desktop**.

### Profiles

Header bar → *Profile*. **Save** writes the current settings, **Save as…** creates a new profile, picking one from the
list loads and applies it, **Export/Import** use the JSON format in [docs/PROFILE-FORMAT.md](docs/PROFILE-FORMAT.md).
The last-used profile is restored at start. Files live in `%APPDATA%\FreeRGB\profiles` (Windows) or
`~/.config/FreeRGB/profiles` (Linux).

### Missing devices / permissions

Open the **Diagnostics** tab (it names other RGB programs that are running and tells you what to do), or run
`FreeRGB --diagnose`. Full table: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).
Typical cause on a machine with SignalRGB / iCUE / Armoury Crate / Logitech G HUB: **only one program can drive a
device at a time** — close the other one.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build FreeRGB.slnx
dotnet test  FreeRGB.slnx
dotnet run --project src/FreeRGB.App                  # window
dotnet run --project src/FreeRGB.App -- --minimized   # tray only (what start-at-sign-in uses)

# self-contained, trimmed (what CI publishes)
dotnet publish src/FreeRGB.App -c Release -r win-x64   --self-contained -p:PublishTrimmed=true -p:TrimMode=partial -o publish/win-x64
dotnet publish src/FreeRGB.App -c Release -r linux-x64 --self-contained -p:PublishTrimmed=true -p:TrimMode=partial -o publish/linux-x64
```

Release process: tag `vX.Y.Z`, let CI build both artifacts, attach them to a GitHub Release. (Not done yet.)

Linux packaging: a self-contained directory/tarball for now. AppImage / Flatpak were considered and **not** implemented
(Flatpak sandboxing would need explicit device and session-bus permissions that haven't been validated; AppImage
needs a Linux machine to test). Contributions welcome.

## Verified & unverified

Full detail and evidence in [docs/WORKLOG.md](docs/WORKLOG.md) and [docs/TESTING.md](docs/TESTING.md).

| Verified | Evidence |
|---|---|
| 52 automated tests pass on Windows (local + CI) and Linux (CI) | `dotnet test`, GitHub Actions |
| Real OpenRGB 1.0 server accepts FreeRGB's handshake (protocol 5) and enumerates devices | `FreeRGB --diagnose` |
| Logitech G502 HERO and 2× Team Group DDR5 RAM (ENE): static colours, brightness, off, Spectrum Cycle, Breathing (+ zones on the mouse), as seen by the owner | [docs/HARDWARE.md](docs/HARDWARE.md) |
| Windows 11: app starts; `--minimized` shows no window; second launch raises the existing window; close-to-tray hides, tray-off exits cleanly and releases the lock | scripted run, worklog |
| Windows 11: HKCU Run entry written/removed correctly | unit test |
| Idle / effect CPU & memory | [docs/PERFORMANCE.md](docs/PERFORMANCE.md) |

| **Not** verified | |
|---|---|
| Any other real device (ASRock board LEDs, RAM, GPU, headset); any hardware via the GUI | not done |
| Linux UI, tray, autostart, permissions | CI only builds, runs the tests, and starts the binary under a virtual display |
| Tray menu clicks (visual check) | not done |
| The Hardware-setup button in the GUI (the underlying code was run end to end, see worklog) | not clicked |
| Behaviour while another RGB program actively manages a device | not tested |

## Add a hardware integration

Implement `FreeRGB.Core.Backends.IDeviceBackend` (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and
[CONTRIBUTING.md](CONTRIBUTING.md)): report devices with their *real* modes/zones, apply modes and LED colours,
raise `Changed`, and report problems through `BackendStatus` rather than exceptions. Test against a simulated peer,
document permissions, and only mark hardware *Verified* after testing the actual device.

## Privacy & security

Local only. No accounts, telemetry, update checks or listening sockets. The only connection is loopback to OpenRGB.
Imported profiles are size-limited and validated. See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE). Dependencies: Avalonia (MIT). OpenRGB (GPL-2.0-or-later) is an external program, not bundled.

# Architecture

## Stack and why

| Choice | Reason (evidence) |
|---|---|
| **.NET 10 + Avalonia 12** | The only GUI toolchain installed and verified on the dev machine (.NET SDK 10.0.401; no Rust/MSVC/Go/CMake present). Avalonia runs on Windows and Linux from one code base and ships a built-in tray icon (`TrayIcon`/`NativeMenu`). |
| **OpenRGB SDK server as the hardware backend** | OpenRGB 1.0 (released 2026-09-11) is GPL-2.0-or-later, supports Windows/Linux/macOS, ships ~1,900 device detectors (count taken from the `OpenRGB.json` it generated on the dev machine), and documents its network protocol (`Documentation/OpenRGBSDK.md`). Re-implementing those device protocols would be unverifiable work without every device. |
| **Loopback TCP, protocol ≤ 5** | The SDK doc describes versions up to 6; FreeRGB implements ≤ 5 and negotiates down. A real OpenRGB 1.0 server accepted protocol 5 (see `docs/WORKLOG.md`). |
| **MIT license** | FreeRGB contains no OpenRGB code — it speaks a documented wire protocol to a separate process — so OpenRGB's GPL does not propagate. Avalonia is MIT. |

Rejected for now: Tauri/Rust (no toolchain here, couldn't build/verify), Electron (RAM), Qt (heavier packaging, LGPL obligations), embedding OpenRGB code (GPL propagation, huge).

## Layers

```
FreeRGB.App (Avalonia)            UI, tray, window lifecycle         src/FreeRGB.App
  AppHost                         long-lived services, reconnect loop
FreeRGB.Core                      no UI dependency                    src/FreeRGB.Core
  LightingService                 lighting engine: apply settings, software effects
  IDeviceBackend / OpenRgbBackend hardware discovery + device integration
  ProfileStore / ProfileValidator profile storage
  StartupManager / SingleInstance / OpenRgbLauncher   platform integration
```

* **UI ↔ hardware separation:** the UI only talks to `LightingService` and the read-only `DeviceInfo` model.
* **Backend interface:** `IDeviceBackend` = discovery (`ConnectAsync`/`RescanAsync`), capabilities (`DeviceInfo.Modes/Zones`), writes (`SetModeAsync`/`SetLedsAsync`/`SetZoneLedsAsync`), `Changed` event, and a `BackendStatus` that distinguishes *unavailable / disconnected / connecting / connected*. Ordinary connectivity failures are reported via status, not exceptions.
* **Device states** (`Controllability`): *Controllable*, *DetectedNotControllable* (reported but no LEDs/modes — with a reason string), and *Absent* (in the profile but not connected; shown greyed in the UI, settings kept, applied automatically on return).
* **Capabilities are never invented:** the effect list is built from the device's own modes (`LightingService.AvailableEffects`); speed/brightness/colour controls are shown only when the selected mode's flags say they exist (`ControlsFor`). Software effects (Rainbow, Colour cycle, Breathing, Wave) are offered only when the device has a per-LED "Direct" mode.
* **Hardware effects preferred.** Software effects run on one shared `PeriodicTimer` (default 20 fps, cap 5–60) that exists only while at least one software effect is active, sends one `UPDATELEDS` per device per frame, and skips frames identical to the previous one.

## Resource behaviour

* No polling of hardware. Device changes arrive as OpenRGB `DEVICE_LIST_UPDATED` events.
* While OpenRGB is unreachable, reconnect attempts use exponential backoff 2 s → 60 s (one loopback TCP connect attempt each). While connected, one 5-second wake-up checks state.
* Single instance: exclusive lock file in the data dir + per-user named pipe to raise the existing window.
* Close button hides to tray (configurable); only tray → *Exit* terminates and disposes backend/timers.

## Safety

* Values sent to a device are validated against the ranges the device advertised (speed, brightness).
* FreeRGB sends only OpenRGB SDK packets; it never writes raw bytes to hardware.
* Packet sizes are bounded (16 MiB) and parsing is bounds-checked; a malformed device is skipped, not fatal.
* Only loopback hosts are accepted for the backend.

## Data locations

Windows `%APPDATA%\FreeRGB`, Linux `$XDG_CONFIG_HOME/FreeRGB` (default `~/.config/FreeRGB`). Override with `FREERGB_DATA_DIR`.
Contents: `settings.json`, `profiles/<name>.json`, `instance.lock`.

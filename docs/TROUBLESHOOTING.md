# Troubleshooting

Start with the **Diagnostics** tab (or run `FreeRGB --diagnose` in a terminal: it prints the backend state and every
device with its modes, then exits without writing anything to hardware).

| Symptom | Meaning | Fix |
|---|---|---|
| "Could not reach OpenRGB at 127.0.0.1:6742" | The OpenRGB SDK server isn't running. FreeRGB cannot control hardware without it. | Install OpenRGB and start it with `OpenRGB --server` (or GUI → SDK Server → Start Server). Or set the OpenRGB path in *Settings* and enable *Start OpenRGB for me*. |
| Connected, **0 devices** | OpenRGB is running but detected nothing. Typical causes: another program holds the device (next rows); missing permissions (Linux); SMBus devices (RAM, motherboard) need OpenRGB's SMBus access set up (Windows: PawnIO driver; Linux: i2c-dev and permissions). | See OpenRGB's docs: *SMBusAccess*, *USBAccess*, *UdevRules*. |
| OpenRGB shows "Zone Initialization" or an I2C/SMBus warning dialog | You started OpenRGB's GUI (`--startminimized`) yourself. FreeRGB launches it headless (`--server`, no GUI) so no dialogs appear; set addressable-header LED counts in FreeRGB's *Zones* section instead. | Use *Settings → Hardware setup*, or start OpenRGB with `--server` only. |
| OpenRGB shows "One or more I2C/SMBus interfaces failed to initialize" (Windows) | PawnIO driver missing, or OpenRGB isn't elevated. Only matters for RAM / motherboard-SMBus devices. | `winget install namazso.PawnIO`, then run OpenRGB as administrator (or as a service). Or tick "Don't show again" if you don't use such devices. |
| Device says "no LEDs are configured yet" | An addressable header has 0 LEDs. | Enter the connected LED count in the device's *Zones* section. |
| Device listed as **detected, not controllable** | OpenRGB reports it, but with no LEDs or modes. The Diagnostics tab shows why. | Usually a protocol/firmware limitation inside OpenRGB; report it there. |
| Colours flicker or revert, or a device is missing | Another RGB program (SignalRGB, iCUE, Armoury Crate, Logitech G HUB, Razer Synapse, …) may be using the same device. The Diagnostics tab lists known RGB programs that are running. | Close the other program or disable its lighting control. Only one program should drive each device. |
| Device in profile shows "not connected" | It is absent right now. | Nothing to do: its settings are kept and applied when it returns. |
| Linux: devices missing / permission denied | The user can't open the HID/I²C node. | Install OpenRGB's udev rules (`Documentation/UdevRules.md` in the OpenRGB repository) and re-plug or reload. Do not run FreeRGB as root. |
| Linux: no tray icon | The desktop may lack a StatusNotifier host (stock GNOME). | Install the AppIndicator/KStatusNotifierItem shell extension or keep the window open. Not yet verified on a real desktop — see `docs/HARDWARE.md`. |

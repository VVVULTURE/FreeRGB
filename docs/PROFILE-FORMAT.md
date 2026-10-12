# Profile format (version 1)

A profile is one UTF-8 JSON file. Unknown properties are ignored; comments and trailing commas are tolerated.
Files over 1 MiB are rejected. Import never overwrites an existing profile unless explicitly told to.

```json
{
  "version": 1,
  "name": "Gaming",
  "devices": {
    "Example RAM|0123456789|I2C: SMBus 0, address 0x58": {
      "on": true,
      "color": "#FF8800",
      "brightness": 80,
      "effect": "Static",
      "speed": 50,
      "zones": { "Strip": "#00FF00" },
      "deviceName": "Example RAM"
    }
  }
}
```

| Field | Rules |
|---|---|
| `version` | must be `1` |
| `name` | 1–64 chars, no control characters; also the file name (sanitised) |
| `devices` key | `name\|serial\|location` exactly as the backend reports it, ≤ 512 chars, ≤ 256 devices |
| `on` | bool |
| `color` | `#RRGGBB` |
| `brightness`, `speed` | integers 0–100; speed is mapped onto the device's own speed range (including devices where a lower number is faster) |
| `effect` | `"Static"`, a mode name reported by the device, or a software effect: `Software: Rainbow`, `Software: Color cycle`, `Software: Breathing`, `Software: Wave` |
| `zones` | optional zone name → `#RRGGBB`; only used with `Static` on devices with a per-LED mode |

**Portable across machines:** settings for devices that aren't present are kept untouched and are applied the moment the device appears. An effect the device doesn't support is reported and *not* sent.

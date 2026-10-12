# Security policy

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private
vulnerability reporting: *Security → Report a vulnerability* on this repository.
Include what you found, how to reproduce it, and the affected version/OS.

You should get an acknowledgement within a few days. This is a volunteer project, so there is no
guaranteed fix timeline, but confirmed issues are prioritised.

## Scope and design notes

- During normal use FreeRGB makes **no network connections except to a loopback OpenRGB SDK server**
  (`127.0.0.1`, default port 6742). Non-loopback hosts in `settings.json` are rejected and reset.
- The **only other network access** is the *Set up hardware support* button (Windows), pressed by the user: it downloads
  OpenRGB 1.0 from codeberg.org and, if needed, the PawnIO installer (winget, or github.com/namazso/PawnIO.Setup). Downloads are
  checked against SHA-256 values pinned in the source (`HardwareSetup.cs`) and refused on mismatch; the PawnIO installer's
  Authenticode signature is also checked on the non-winget path.
- That button runs **one elevated PowerShell script** (UAC prompt) that installs PawnIO (a third-party kernel driver; it gives
  software direct hardware access, which is why it needs your explicit approval) and registers a scheduled task
  `FreeRGB-OpenRGB` that starts OpenRGB with highest privileges at your sign-in. Remove it with
  `schtasks /delete /tn FreeRGB-OpenRGB /f` (admin). Uninstall PawnIO via *Apps & features* or `winget uninstall namazso.PawnIO`.
- FreeRGB opens **no listening TCP ports**. Its single-instance signal uses a per-user named pipe.
- No telemetry, accounts, or update checks.
- Imported profiles are size-limited (1 MiB) and strictly validated before use.
- FreeRGB never runs elevated and never needs to. Hardware access that needs elevated rights
  (e.g. SMBus on Windows) is performed by OpenRGB, not FreeRGB.
- The OpenRGB SDK has no authentication: any local process can already talk to it. Keep its
  server bound to localhost (the default) and do not expose it to your network.

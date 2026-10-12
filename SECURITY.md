# Security policy

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private
vulnerability reporting: *Security → Report a vulnerability* on this repository.
Include what you found, how to reproduce it, and the affected version/OS.

You should get an acknowledgement within a few days. This is a volunteer project, so there is no
guaranteed fix timeline, but confirmed issues are prioritised.

## Scope and design notes

- FreeRGB makes **no network connections except to a loopback OpenRGB SDK server**
  (`127.0.0.1`, default port 6742). Non-loopback hosts in `settings.json` are rejected and reset.
- FreeRGB opens **no listening TCP ports**. Its single-instance signal uses a per-user named pipe.
- No telemetry, accounts, or update checks.
- Imported profiles are size-limited (1 MiB) and strictly validated before use.
- FreeRGB never runs elevated and never needs to. Hardware access that needs elevated rights
  (e.g. SMBus on Windows) is performed by OpenRGB, not FreeRGB.
- The OpenRGB SDK has no authentication: any local process can already talk to it. Keep its
  server bound to localhost (the default) and do not expose it to your network.

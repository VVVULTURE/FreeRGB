# Contributing

Thanks for helping. The two rules that matter most:

1. **Don't claim what you haven't verified.** Hardware support is only "Verified" if someone tested the
   exact device (see `docs/HARDWARE.md` for the levels and the report format).
2. **Never send undocumented commands to hardware to see what happens.** Hardware protocols belong in
   OpenRGB or in a backend with a documented protocol.

## Development

```
dotnet build FreeRGB.slnx
dotnet test  FreeRGB.slnx
dotnet run --project src/FreeRGB.App            # start the app
dotnet run --project src/FreeRGB.App -- --minimized   # tray-only, as at sign-in
```

Requires the .NET 10 SDK. See `docs/ARCHITECTURE.md` for the layout and `docs/TESTING.md` for what
the tests do and don't prove (they use a simulated OpenRGB server — that is *not* hardware testing).

## Adding a hardware backend

Implement `FreeRGB.Core.Backends.IDeviceBackend` (discovery, capabilities, mode/LED writes, a `Changed`
event, no exceptions for ordinary connectivity failures). Then wire it up in `AppHost`. Include tests
against a simulated peer, document its permissions/driver requirements in `docs/HARDWARE.md`, and add
an entry to `docs/WORKLOG.md`.

## Pull requests

Keep them focused, update docs when behaviour changes, and fill in the PR template. CI must pass on
Windows and Linux.

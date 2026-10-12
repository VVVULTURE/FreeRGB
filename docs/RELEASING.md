# Releasing

Releases are built by `.github/workflows/release.yml`.

## Make a release

1. Make sure `main` is green (CI) and docs/`docs/HARDWARE.md` are up to date.
2. Tag and push: `git tag v0.2.0 && git push origin v0.2.0`.
3. The workflow builds, tests and smoke-tests everything, then creates the GitHub Release with all files and `SHA256SUMS.txt`.
   Versions starting with `0.` or containing `-` are marked *pre-release* automatically.

To dry-run without publishing: Actions → *Release* → *Run workflow* (builds and smoke-tests; no release is created; artifacts are
attached to the run).

## What is built

| File | How | Smoke test in CI |
|---|---|---|
| `FreeRGB-<v>-win-x64-portable.exe` | `dotnet publish` single-file, self-contained, trimmed, compressed | runs `--diagnose` |
| `FreeRGB-<v>-win-x64-setup.exe` | Inno Setup (`installer/windows/FreeRGB.iss`), per-user, optional start-at-sign-in | silent install → run installed exe → silent uninstall |
| `FreeRGB-<v>-linux-x64.AppImage` | `appimagetool` 1.9.1 (pinned by SHA-256) | runs `--diagnose`; GUI stays up 8 s under xvfb |
| `freergb_<v>_amd64.deb` | `dpkg-deb` (`installer/linux/build-linux-packages.sh`) | installed in a bare `ubuntu:24.04` container, runs `--diagnose`, `ldd` finds no missing libraries (except the optional `liblttng-ust`), removed cleanly |
| `FreeRGB-<v>-linux-x64.tar.gz` | plain folder | runs `--diagnose` |

## Known limits

* Windows binaries are **not code-signed** (SmartScreen may warn). Signing needs a certificate; not set up.
* Linux packages are x64 only. No arm64, no Flatpak, no rpm yet.
* Smoke tests prove the packages install and start; they do **not** prove the tray, autostart or hardware access on a real Linux desktop.
* The version comes from the tag; `Version` in `FreeRGB.App.csproj` is only the default for local builds.
* Windows uninstall leaves `%APPDATA%\FreeRGB` (your profiles) and, if you used *Set up hardware support*, the elevated scheduled task and PawnIO driver (see `SECURITY.md`).

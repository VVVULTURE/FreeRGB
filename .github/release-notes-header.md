**FreeRGB __VERSION__** — lightweight, open-source RGB control for Windows 11 and Linux. MIT licensed.

## Downloads

| File | What it is |
|---|---|
| `FreeRGB-__VERSION__-win-x64-portable.exe` | **Windows, portable.** One file, no install; just run it. Settings go to `%APPDATA%\FreeRGB`. |
| `FreeRGB-__VERSION__-win-x64-setup.exe` | **Windows installer.** Per-user (no admin needed), Start-menu entry, optional start-at-sign-in. |
| `FreeRGB-__VERSION__-linux-x64.AppImage` | **Linux, portable.** `chmod +x` and run. |
| `freergb___VERSION___amd64.deb` | **Linux installer** for Debian / Ubuntu and derivatives (`sudo apt install ./freergb_*.deb`). |
| `FreeRGB-__VERSION__-linux-x64.tar.gz` | Linux, plain folder for other distributions. |
| `SHA256SUMS.txt` | Checksums of every file above. |

## Before you start

* FreeRGB controls hardware **through OpenRGB** (a separate program). On Windows, *Settings → Hardware setup* can install it for
  you; on Linux install OpenRGB and its udev rules yourself. See the README.
* **This is an early pre-release.** Hardware verified so far: one Logitech mouse, two DDR5 RAM sticks (ENE), and the CPU-cooler
  fans on an ASRock B760M board. Everything else is untested, and the Linux builds have only been checked by automated
  tests, not on a real desktop. Details: [docs/HARDWARE.md](https://github.com/VVVULTURE/FreeRGB/blob/main/docs/HARDWARE.md).
* The Windows downloads are **not code-signed**, so SmartScreen may warn on first run.

# Third-party notices

FreeRGB itself is MIT-licensed (see `LICENSE`). The release downloads bundle the following components. Licences were read
from each package's NuGet metadata (`license` expression) on 2026-10-12; the native-library rows are from the upstream
projects' well-known licences and should be re-checked when versions change.

| Component | Version | Licence | Notes |
|---|---|---|---|
| .NET runtime and base class libraries | 10.0 | MIT | self-contained builds ship the runtime (trimmed) |
| Avalonia (Base, Controls, Desktop, Themes.Fluent, ColorPicker, Win32, X11, FreeDesktop, Skia, Native, Remote.Protocol) | 12.1.4 | MIT | UI framework |
| SkiaSharp + native Skia | 3.119.4 | MIT (bindings); BSD-3-Clause (Skia) | 2D rendering |
| HarfBuzzSharp + native HarfBuzz | 8.3.1.3 | MIT | text shaping |
| MicroCom.Runtime | 0.11.6 | MIT | Windows COM interop |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux tray / desktop integration |
| Avalonia.Angle.Windows.Natives (ANGLE) | 2.1.27548.20260419 | BSD-3-Clause | Windows GPU rendering layer |

## Not bundled (separate programs FreeRGB talks to or fetches on request)

* **OpenRGB** (GPL-2.0-or-later, <https://openrgb.org>): FreeRGB talks to it over its documented network protocol and
  contains none of its code. On Windows, the optional *Set up hardware support* button downloads OpenRGB 1.0 from its
  official release page.
* **PawnIO** (author: namazso, <https://pawnio.eu>): optional kernel driver used by OpenRGB for RAM / SMBus lighting. Not
  redistributed by FreeRGB; fetched from its author on request (winget or the signed installer).

Packaging tools used at build time only: Inno Setup (Windows installer), appimagetool (AppImage), dpkg (deb).

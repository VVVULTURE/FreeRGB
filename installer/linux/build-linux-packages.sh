#!/usr/bin/env bash
# Builds the Linux release files from a `dotnet publish -r linux-x64 --self-contained` folder.
#   usage: build-linux-packages.sh <publish-dir> <version> <out-dir>
# Produces: FreeRGB-<v>-linux-x64.tar.gz, freergb_<v>_amd64.deb, FreeRGB-<v>-linux-x64.AppImage
# Needs: dpkg-deb, tar, and (for the AppImage) an appimagetool binary pointed to by $APPIMAGETOOL.
set -euo pipefail

PUB=$(realpath "$1"); VER="$2"; OUT=$(realpath -m "$3")
HERE=$(cd "$(dirname "$0")" && pwd); REPO=$(cd "$HERE/../.." && pwd)
ICON="$REPO/src/FreeRGB.App/Assets/icon.png"
mkdir -p "$OUT"
[ -x "$PUB/FreeRGB" ] || { echo "FreeRGB executable not found in $PUB" >&2; exit 1; }

WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT

# ---------------- tar.gz (portable folder) ----------------
mkdir -p "$WORK/tar/FreeRGB"
cp -a "$PUB/." "$WORK/tar/FreeRGB/"
cp "$REPO/LICENSE" "$REPO/THIRD-PARTY-NOTICES.md" "$WORK/tar/FreeRGB/"
tar -C "$WORK/tar" --owner=0 --group=0 -czf "$OUT/FreeRGB-$VER-linux-x64.tar.gz" FreeRGB

# ---------------- .deb ----------------
D="$WORK/deb"
mkdir -p "$D/DEBIAN" "$D/opt/freergb" "$D/usr/bin" "$D/usr/share/applications" \
         "$D/usr/share/icons/hicolor/64x64/apps" "$D/usr/share/doc/freergb"
cp -a "$PUB/." "$D/opt/freergb/"
chmod 755 "$D/opt/freergb/FreeRGB"
ln -s /opt/freergb/FreeRGB "$D/usr/bin/freergb"
cp "$HERE/freergb.desktop" "$D/usr/share/applications/freergb.desktop"
cp "$ICON" "$D/usr/share/icons/hicolor/64x64/apps/freergb.png"
cp "$REPO/LICENSE" "$D/usr/share/doc/freergb/copyright"
cp "$REPO/THIRD-PARTY-NOTICES.md" "$D/usr/share/doc/freergb/"
SIZE=$(du -sk "$D/opt" | cut -f1)
cat > "$D/DEBIAN/control" <<EOF
Package: freergb
Version: $VER
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: $SIZE
Maintainer: FreeRGB contributors <noreply@users.noreply.github.com>
Depends: libc6, libstdc++6, zlib1g, libx11-6, libice6, libsm6, libfontconfig1
Recommends: openrgb
Homepage: https://github.com/VVVULTURE/FreeRGB
Description: Lightweight RGB lighting control
 FreeRGB controls RGB hardware (motherboards, RAM, fans, peripherals) through
 the OpenRGB SDK server. It has a tray icon, lighting profiles and a diagnostics
 page. OpenRGB itself is a separate program and must be installed and running.
EOF
dpkg-deb --build --root-owner-group "$D" "$OUT/freergb_${VER}_amd64.deb" >/dev/null

# ---------------- AppImage ----------------
if [ -n "${APPIMAGETOOL:-}" ]; then
  A="$WORK/AppDir"
  mkdir -p "$A/usr/lib/freergb"
  cp -a "$PUB/." "$A/usr/lib/freergb/"
  cp "$HERE/freergb.desktop" "$A/freergb.desktop"
  cp "$ICON" "$A/freergb.png"
  cat > "$A/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/freergb/FreeRGB" "$@"
EOF
  chmod 755 "$A/AppRun" "$A/usr/lib/freergb/FreeRGB"
  ARCH=x86_64 "$APPIMAGETOOL" --appimage-extract-and-run "$A" "$OUT/FreeRGB-$VER-linux-x64.AppImage"
else
  echo "APPIMAGETOOL not set; skipping AppImage" >&2
fi
ls -l "$OUT"

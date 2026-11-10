#!/usr/bin/env bash
#
# Packages the NativeAOT publish output (publish/aot) into a Linux tarball:
#
#   dist/<FileName>-<version>-linux-x64.tar.gz
#
# The tarball holds the executable with its native libraries, an icon, a .desktop entry and install.sh, which puts the
# app under ~/.local so it shows up in the desktop's app launcher (the same layout the Flutter release uses).
# Run on Linux (the release workflow's build-linux job does). Inputs from the environment:
#   FILE_NAME  DISPLAY_NAME  APP_ID  VERSION   (Directory.Build.props: AppFileName, AppDisplayName, AppId, AppVersion)
#   ARCH       x64 (default) — file name only; publish with the matching RID

set -euo pipefail

: "${FILE_NAME:?}" "${DISPLAY_NAME:?}" "${APP_ID:?}" "${VERSION:?}"
ARCH="${ARCH:-x64}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/publish/aot"
STAGE="$ROOT/dist/linux-stage/$FILE_NAME"
TARBALL="$ROOT/dist/$FILE_NAME-$VERSION-linux-$ARCH.tar.gz"

test -f "$PUBLISH/$FILE_NAME" || { echo "error: $PUBLISH/$FILE_NAME not found — publish first" >&2; exit 1; }

rm -rf "$ROOT/dist/linux-stage"
mkdir -p "$STAGE/share/icons/hicolor/512x512/apps" "$STAGE/share/applications"

find "$PUBLISH" -maxdepth 1 -type f ! -name '*.pdb' ! -name '*.dbg' -exec cp {} "$STAGE/" \;
chmod +x "$STAGE/$FILE_NAME"

cp "$ROOT/installer/linux/app_icon.png" "$STAGE/share/icons/hicolor/512x512/apps/$APP_ID.png"

# The .desktop file's name must equal the application id so GNOME ties the running window to it (StartupWMClass).
cat > "$STAGE/share/applications/$APP_ID.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$DISPLAY_NAME
Exec="@INSTALL_DIR@/$FILE_NAME"
Icon=$APP_ID
StartupWMClass=$APP_ID
Terminal=false
Categories=Utility;
EOF

sed -e "s|@FILE_NAME@|$FILE_NAME|g" -e "s|@APP_ID@|$APP_ID|g" "$ROOT/installer/linux/install.sh" > "$STAGE/install.sh"
chmod +x "$STAGE/install.sh"

mkdir -p "$ROOT/dist"
tar -C "$ROOT/dist/linux-stage" -czf "$TARBALL" "$FILE_NAME"
rm -rf "$ROOT/dist/linux-stage"
echo "TARBALL=$TARBALL"

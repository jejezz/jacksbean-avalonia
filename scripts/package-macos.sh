#!/usr/bin/env bash
#
# Packages the NativeAOT publish output (publish/aot) into a macOS .app and a .dmg:
#
#   dist/<FileName>-<version>-macos-arm64.dmg
#
# Run on a Mac (the release workflow's build-macos job does). Inputs come from the environment:
#   FILE_NAME      executable / file-name stem        (Directory.Build.props AppFileName)
#   DISPLAY_NAME   name shown in Finder               (AppDisplayName)
#   APP_ID         bundle identifier                  (AppId)
#   VERSION        1.0.0 or 1.0.0-rc.1                (AppVersion)
#   BUILD          integer build number               (AppBuild)
#   COPYRIGHT      (optional)
#   ARCH           arm64 (default) — used in the file name only; publish with the matching RID
#   SIGN_IDENTITY  Developer ID identity, or "-" (default) for an ad-hoc signature.
#                  An ad-hoc signature is enough to run on Apple Silicon but Gatekeeper still warns
#                  ("unidentified developer") on first launch — see .github/release-notes-header.md.
#
# Signing and notarization with a Developer ID certificate are separate workflow steps (they need secrets),
# so this script only produces a signed-or-ad-hoc .app and an unsigned .dmg container.

set -euo pipefail

: "${FILE_NAME:?}" "${DISPLAY_NAME:?}" "${APP_ID:?}" "${VERSION:?}" "${BUILD:?}"
COPYRIGHT="${COPYRIGHT:-}"
ARCH="${ARCH:-arm64}"
SIGN_IDENTITY="${SIGN_IDENTITY:--}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/publish/aot"
STAGE="$ROOT/dist/macos-stage"
APP="$STAGE/$DISPLAY_NAME.app"
DMG="$ROOT/dist/$FILE_NAME-$VERSION-macos-$ARCH.dmg"

test -f "$PUBLISH/$FILE_NAME" || { echo "error: $PUBLISH/$FILE_NAME not found — publish first" >&2; exit 1; }

rm -rf "$STAGE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# The executable and the native libraries it loads sit side by side (symbol files stay out of the bundle).
find "$PUBLISH" -maxdepth 1 -type f ! -name '*.pdb' ! -name '*.dbg' -exec cp {} "$APP/Contents/MacOS/" \;
chmod +x "$APP/Contents/MacOS/$FILE_NAME"

# Info.plist: the CFBundleShortVersionString only takes digits (1.0.0-rc.1 -> 1.0.0).
sed -e "s|@FILE_NAME@|$FILE_NAME|g" \
    -e "s|@DISPLAY_NAME@|$DISPLAY_NAME|g" \
    -e "s|@APP_ID@|$APP_ID|g" \
    -e "s|@SHORT_VERSION@|${VERSION%%-*}|g" \
    -e "s|@BUILD@|$BUILD|g" \
    -e "s|@COPYRIGHT@|$COPYRIGHT|g" \
    "$ROOT/installer/macos/Info.plist" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"

# AppIcon.icns from the 1024px master.
ICONSET="$STAGE/AppIcon.iconset"
mkdir -p "$ICONSET"
MASTER="$ROOT/installer/macos/app_icon_1024.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$MASTER" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$MASTER" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

# Sign every Mach-O (libraries first, then the executable, then the bundle). Hardened runtime and a secure
# timestamp are only meaningful for a real identity (notarization needs both).
SIGN_FLAGS=(--force --sign "$SIGN_IDENTITY")
if [ "$SIGN_IDENTITY" != "-" ]; then SIGN_FLAGS+=(--options runtime --timestamp); fi
for lib in "$APP"/Contents/MacOS/*.dylib; do
  [ -e "$lib" ] && codesign "${SIGN_FLAGS[@]}" "$lib"
done
codesign "${SIGN_FLAGS[@]}" "$APP/Contents/MacOS/$FILE_NAME"
codesign "${SIGN_FLAGS[@]}" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"

# .dmg: a window with the app and a link to /Applications to drag it onto. create-dmg lays the window out
# (size, large icons, positions) the same way as the Flutter versions; plain hdiutil would leave Finder's
# default small-icon grid. create-dmg sometimes exits non-zero over unrelated warnings even after producing a
# working DMG, so only the file's existence counts. Without create-dmg, fall back to the plain container.
mkdir -p "$ROOT/dist"
rm -f "$DMG"
if command -v create-dmg >/dev/null 2>&1; then
  create-dmg \
    --volname "$DISPLAY_NAME" \
    --window-size 500 300 \
    --icon-size 100 \
    --icon "$DISPLAY_NAME.app" 120 120 \
    --app-drop-link 380 120 \
    "$DMG" \
    "$APP" || true
fi
if [ ! -f "$DMG" ]; then
  echo "create-dmg unavailable or failed; building a plain DMG with hdiutil" >&2
  ln -s /Applications "$STAGE/Applications"
  hdiutil create -volname "$DISPLAY_NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
  rm "$STAGE/Applications"
fi

echo "DMG_PATH=$DMG"

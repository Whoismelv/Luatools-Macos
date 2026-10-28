#!/usr/bin/env bash
# Packages a built LuaTools.app into a distributable .dmg.
# macOS only — hdiutil is the whole point.
#
#   ./scripts/make-dmg.sh dist/osx-arm64/LuaTools.app 1.3.2 arm64
#
# Output: dist/LuaTools-<version>-<label>.dmg
set -euo pipefail

APP="${1:?usage: make-dmg.sh <LuaTools.app> [version] [label]}"
VERSION="${2:-1.3.2}"
LABEL="${3:-$(uname -m)}"

if [[ ! -d "$APP" ]]; then
  echo "no bundle at $APP" >&2
  exit 1
fi

DMG="dist/LuaTools-${VERSION}-${LABEL}.dmg"
STAGE="$(mktemp -d)/stage"
mkdir -p "$STAGE" dist

cp -R "$APP" "$STAGE/LuaTools.app"
ln -s /Applications "$STAGE/Applications"

cat > "$STAGE/READ ME.txt" <<'NOTES'
LuaTools for macOS

Install: drag LuaTools.app onto the Applications folder in this window.

This build is ad-hoc signed, not notarized by Apple, so Gatekeeper blocks the
first launch. Use Terminal:

    xattr -dr com.apple.quarantine /Applications/LuaTools.app
    open /Applications/LuaTools.app

Do not rely on System Settings -> Open Anyway: the bundle signature is
incomplete, so Gatekeeper may report it as damaged.

Requirements: macOS 11 or newer. The .NET runtime is bundled.

The app downloads Steam depots. It does not install games into your Steam
library and does not bypass DRM — see the project README.
NOTES

rm -f "$DMG"
hdiutil create -volname "LuaTools" -srcfolder "$STAGE" -ov -format UDZO -fs HFS+ "$DMG"
hdiutil verify "$DMG"

shasum -a 256 "$DMG" | tee "${DMG}.sha256"
rm -rf "$(dirname "$STAGE")"

echo "built: $DMG"

#!/usr/bin/env bash
# Builds LuaTools.app for macOS. Run this ON a Mac.
#   ./scripts/build-macos.sh            -> universal-ish default (host arch)
#   ./scripts/build-macos.sh osx-arm64
#   ./scripts/build-macos.sh osx-x64
set -euo pipefail

RID="${1:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/LuaTools.App/LuaTools.App.csproj"

if [[ -z "$RID" ]]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    *)     RID="osx-x64" ;;
  esac
fi

OUT="$ROOT/dist/$RID"
APP="$OUT/LuaTools.app"
PUB="$OUT/publish"

echo "==> publishing $RID"
rm -rf "$PUB" "$APP"
dotnet publish "$PROJ" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishTrimmed=false \
  -p:UseAppHost=true \
  -o "$PUB"

echo "==> assembling bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/LuaTools.App"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>LuaTools</string>
  <key>CFBundleDisplayName</key>       <string>LuaTools</string>
  <key>CFBundleIdentifier</key>        <string>tools.lua.luatools</string>
  <key>CFBundleVersion</key>           <string>1.3.2</string>
  <key>CFBundleShortVersionString</key><string>1.3.2</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>CFBundleExecutable</key>        <string>LuaTools.App</string>
  <key>CFBundleIconFile</key>          <string>LuaTools.icns</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
  <key>LSApplicationCategoryType</key> <string>public.app-category.utilities</string>
  <key>NSAppTransportSecurity</key>
  <dict><key>NSAllowsArbitraryLoads</key><true/></dict>
</dict>
</plist>
PLIST

printf 'APPL????' > "$APP/Contents/PkgInfo"

if [[ -f "$ROOT/assets/LuaTools.icns" ]]; then
  cp "$ROOT/assets/LuaTools.icns" "$APP/Contents/Resources/"
fi

echo "==> ad-hoc signing"
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "codesign skipped (no Xcode CLT)"

echo
echo "built: $APP"
echo "run:   open \"$APP\""
echo
echo "first launch is unsigned-ish: right-click -> Open, or:"
echo "  xattr -dr com.apple.quarantine \"$APP\""

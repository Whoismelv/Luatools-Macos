#!/usr/bin/env bash
# probe.sh — runs on a Mac. Answers the only question that matters:
# can we inject into Steam's inner client, and what does that client look like?
#
#   ./probe.sh                 inspect the installed Steam
#   ./probe.sh /path/to/Steam.app
set -uo pipefail

APP="${1:-/Applications/Steam.app}"
BIN="$APP/Contents/MacOS/steam_osx"
OUT="$(cd "$(dirname "$0")" && pwd)/probe-$(date +%Y%m%d-%H%M%S).txt"

say() { echo "$@" | tee -a "$OUT"; }
sec() { echo "" | tee -a "$OUT"; echo "=== $1 ===" | tee -a "$OUT"; }

: > "$OUT"

sec "host"
say "uname:   $(uname -a)"
say "arch:    $(uname -m)"
say "macos:   $(sw_vers -productVersion 2>/dev/null)"
say "sip:     $(csrutil status 2>/dev/null)"

sec "steam app"
if [[ ! -d "$APP" ]]; then
  say "MISSING: $APP"
  say "pass the real path: ./probe.sh /Applications/Steam.app"
  exit 1
fi
say "app:     $APP"
say "version: $(defaults read "$APP/Contents/Info.plist" CFBundleShortVersionString 2>/dev/null)"
say "build:   $(defaults read "$APP/Contents/Info.plist" CFBundleVersion 2>/dev/null)"

sec "inner client binary"
if [[ ! -f "$BIN" ]]; then
  say "no steam_osx at $BIN — listing MacOS dir:"
  ls -la "$APP/Contents/MacOS" | tee -a "$OUT"
  exit 1
fi
say "path:    $BIN"
say "size:    $(stat -f '%z bytes' "$BIN")"
say "archs:   $(lipo -archs "$BIN" 2>/dev/null || echo unknown)"
say "file:    $(file -b "$BIN")"

sec "codesign of inner client (THE KEY QUESTION)"
codesign -dvvv "$BIN" 2>&1 | tee -a "$OUT"
say ""
say "--- looking for hardened runtime flag (0x10000) ---"
ENT=$(codesign -d --entitlements - "$BIN" 2>/dev/null)
if echo "$ENT" | grep -qi get-task-allow; then
  say "entitlement get-task-allow: PRESENT"
else
  say "entitlement get-task-allow: absent"
fi

sec "codesign of the bootstrapper (expected to strip DYLD_*)"
codesign -dvv "$APP" 2>&1 | tee -a "$OUT"

sec "dynamic library validation"
otool -l "$BIN" 2>/dev/null | grep -A3 LC_CODE_SIGNATURE | tee -a "$OUT"
say ""
say "--- rpaths ---"
otool -l "$BIN" 2>/dev/null | grep -A2 LC_RPATH | grep path | tee -a "$OUT"

sec "does an env test survive into the client?"
say "manual check:"
say "  DYLD_INSERT_LIBRARIES=/tmp/nonexistent.dylib LUATOOLS_TEST=1 \\"
say "    \"$BIN\" 2>&1 | head -5"
say "  if you see 'dyld: Library not loaded: /tmp/nonexistent.dylib'"
say "  -> injection is ALLOWED into this binary."

sec "entitlements tree"
codesign -d --entitlements :- "$APP" 2>&1 | tee -a "$OUT"

sec "done"
say "report written to: $OUT"
say ""
say "send this file back. nothing in it is secret — it is all public"
say "metadata about a Valve binary you already have installed."

# Handoff — continuing on a Mac

Repo: <https://github.com/Whoismelv/Luatools-Macos>
Default branch: `main`
Release: <https://github.com/Whoismelv/Luatools-Macos/releases/latest>

This file is the short version of where the project stands, what is proven, and
what to do next on Apple Silicon.

## Install the published build

1. Download `LuaTools-<version>-arm64.dmg` (M1/M2/M3/M4) or `-x64.dmg` (Intel).
2. Open the DMG and drag `LuaTools.app` to **Applications**.
3. Clear the quarantine flag (the build is ad-hoc signed, not notarized):

   ```sh
   xattr -dr com.apple.quarantine /Applications/LuaTools.app
   open /Applications/LuaTools.app
   ```

Use the `xattr` command, not "Open Anyway": the bundle seal is incomplete, so
Gatekeeper may report the app as damaged. This exact path is tested by
`.github/workflows/release.yml` on every build: mount DMG → copy → set
quarantine → `spctl` rejects → `xattr` → `--selftest` → window opens.

The DMG does **not** contain a Steam account login. Downloads are anonymous and
Steam will refuse paid games (and free games that require ownership).

## Build and run from source

Requirements: macOS 11+, Xcode Command Line Tools, .NET 8 SDK.

```sh
git clone https://github.com/Whoismelv/Luatools-Macos
cd Luatools-Macos
chmod +x scripts/build-macos.sh scripts/make-dmg.sh
./scripts/build-macos.sh osx-arm64
open dist/osx-arm64/LuaTools.app
```

Headless checks:

```sh
dist/osx-arm64/LuaTools.app/Contents/MacOS/LuaTools.App --selftest
dist/osx-arm64/LuaTools.app/Contents/MacOS/LuaTools.App --verify-tools
```

`--selftest` covers VDF/ACF parsing, the macOS depot filter, storage, the
DepotDownloader runtime tag, and a live probe of any Steam install it finds.
`--verify-tools` downloads SteamRE's self-contained `DepotDownloader` release
and runs `--version`.

Package a DMG locally:

```sh
./scripts/make-dmg.sh dist/osx-arm64/LuaTools.app 1.3.6 arm64
```

## What is actually verified

- Release workflow on `macos-14-arm64`: Mach-O signing, `--selftest`,
  `--verify-tools`, GUI window, DMG creation, `hdiutil verify`, and the
  quarantine → `xattr` → launch path from a copy of the DMG.
- Real Windows Steam install: 11 `appmanifest_*.acf` files parsed, 0 malformed
  (this is where the KeyValues parser bug was found and fixed).
- DepotDownloader pipeline: app 740, 2,813 files (Windows, anonymous).
- Live metadata: `config.oslist` macOS detection for real apps.
- Steam bootstrapper: x86_64-only, hardened runtime, notarized, measured — see
  [`docs/steam-macos-analysis.md`](docs/steam-macos-analysis.md).

Not verified yet:

- A download on macOS of a real game depot through the UI (no account login).
- The full inner Steam client after first launch (the probe only saw a 4.4 MB
  copy of the bootstrapper at `~/Library/Application Support/Steam/SteamAppBundle`).
- Injection on a SIP-enabled Mac (the CI runner has SIP disabled).
- The `.app` bundle seal (`codesign` rejects it; every Mach-O is signed
  individually, the app runs, but `spctl` calls the bundle incomplete).

## Continue the Steam analysis on your Mac

This is the highest-value next step and does not need CI.

```sh
cd inject
chmod +x probe.sh
./probe.sh
```

`probe.sh` writes `probe-<timestamp>.txt` in the current directory. It records:

- whether `steam_osx` is Intel or universal,
- hardened-runtime flags and entitlements of the shipped and inner clients,
- where the full client lands under `~/Library/Application Support/Steam`,
- whether a correctly-architected dylib lands with SIP enabled.

Treat the output as data. Do not publish claims until it lands or fails on your
Mac with SIP enabled (`csrutil status` should say `enabled`).

Then launch Steam normally, let it finish updating, and repeat the Mach-O /
signature scan over `~/Library/Application Support/Steam`. The open question is
whether the **inner** client is also hardened. If it is, client injection on
stock macOS is not viable.

## Next feature work

1. **Steam login for DepotDownloader.** `DownloadRequest` already carries
   `Username`/`Password`, but no view sets them and the process does not handle
   a Steam Guard prompt. Decide between a login form plus stdin handling, or a
   documented one-time `DepotDownloader -username ... -remember-password` setup.
   Until then the app is anonymous-only.
2. **Fix the bundle signature.** The managed DLLs sit in `Contents/MacOS`, so
   `codesign` treats them as nested code. Move the payload to
   `Contents/Resources/app` with a small launcher, or sign the bundle with a
   layout codesign accepts. Then `spctl` can report a normal ad-hoc rejection
   instead of an incomplete bundle.
3. **Arm64 Steam reality check.** If Steam remains x86_64, note it in the UI and
   docs; do not imply native Apple Silicon support for the client.
4. **macOS download test.** With an account that owns a small macOS game, run
   one real download through the UI and record the result in the README.

## Release mechanics

- `.github/workflows/release.yml` runs on `macos-14` for `osx-arm64` and
  `osx-x64`. A tag push (`v1.3.6`) publishes a GitHub release; a
  `workflow_dispatch` builds and verifies without publishing.
- The DMG is built only on macOS (`hdiutil`). Do not try to build it on Windows.
- If a build fails, the workflow pushes diagnostics to the `ci-log` branch and
  the release job is skipped.
- Artifacts from a dispatch run are under the run's **Artifacts** section for 7
  days.

```sh
# build + verify, no release
gh workflow run release.yml -f version=1.3.7-dev

# publish
git tag v1.3.7 && git push origin v1.3.7
```

## Layout

```
src/LuaTools.Core/      Steam discovery, VDF/ACF, API, DepotDownloader, builds
src/LuaTools.App/       Avalonia UI, --selftest, --verify-tools
inject/                 macOS injection research (not a feature)
docs/                   measured Steam analysis
scripts/                local build + DMG packaging
.github/workflows/      release (tag) and steam-probe (dispatch, read-only)
```

## Known limits (do not overclaim)

- Ad-hoc signed, not notarized; `xattr` is required.
- No Steam login UI; anonymous downloads only.
- A depot download is not a Steam install; games do not appear in the library.
- No lua injection, no DRM removal, no Steamless for Mach-O.
- The Windows app's `stplug-in` path does not exist in the macOS client.

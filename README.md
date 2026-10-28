# LuaTools for macOS

A macOS port of [LuaTools](https://lua.tools) — the Steam depot manager.
Fetches Steam depots into a folder and reads/writes `luabuilds_*.lua`.
It does **not** install games into Steam and does **not** inject into the Steam
client.

Built with Avalonia on .NET 8, so the same codebase compiles for Windows,
macOS and Linux.

> **Status: partial.** The depot pipeline works and is verified. The Steam
> client integration (lua injection) is **not** implemented — `inject/` is a
> research harness, not a working feature. Read [What works](#what-works)
> before expecting LuaTools-for-Windows behaviour.

## Download

Prebuilt `.dmg` files are attached to the [latest release](https://github.com/Whoismelv/Luatools-Macos/releases/latest):

| File | For |
|---|---|
| `LuaTools-<version>-arm64.dmg` | Apple Silicon (M1 and later) |
| `LuaTools-<version>-x64.dmg` | Intel Macs |

Open the disk image, drag `LuaTools.app` onto **Applications**, then clear the
quarantine flag — the build is ad-hoc signed, not notarized:

```sh
xattr -dr com.apple.quarantine /Applications/LuaTools.app
open /Applications/LuaTools.app
```

Do **not** rely on System Settings → **Open Anyway** for this build: `spctl`
reports the bundle signature as incomplete (`code has no resources but signature
indicates they must be present`), so Gatekeeper may call it damaged instead of
offering the usual override. The `xattr` command is the supported path and is
tested on every release build.

The release is built by [`.github/workflows/release.yml`](.github/workflows/release.yml)
on a `macos-14` runner, because `hdiutil` (the only real DMG tool) exists only
on macOS. Each build is ad-hoc signed; the arm64 build runs `--selftest` and
`--verify-tools` on the runner, then mounts the finished DMG, copies the app,
sets a real quarantine attribute, clears it with the command above, and proves
the copied app opens a window.

Continuing on a Mac? See [HANDOFF.md](HANDOFF.md) for build, test, and
release steps, and [docs/steam-macos-analysis.md](docs/steam-macos-analysis.md)
for the measured Steam client analysis.

---

## What works

| Feature | State |
|---|---|
| Steam discovery (macOS / Windows / Linux paths) | ✅ works |
| Library + `appmanifest_*.acf` parsing | ✅ works, verified against 11 real manifests (Windows install) |
| Steam app list + depot metadata | ✅ works (live APIs) |
| macOS depot detection (`config.oslist`) | ✅ works, verified against live metadata |
| DepotDownloader acquisition + invocation | ✅ verified on macos-arm64 (self-contained asset, `--version` exit 0) |
| Progress parsing | ✅ works |
| `luabuilds_*.lua` read/write | ✅ works |
| `--selftest` headless check | ✅ works (VDF/ACF, filter, storage) |
| DMG first-run path (mount → copy → quarantine → `xattr` → window) | ✅ verified on `macos-14` |
| Steam client injection | ❌ not implemented |
| Steam account login | ❌ not implemented — downloads are anonymous only |
| DRM removal | ❌ not implemented (Steamless is PE-only) |

### macOS filtering

Every depot carries `config.oslist`. A game is marked macOS-supported only if
at least one depot explicitly lists `macos`. Shared depots without an `oslist`
do **not** count — otherwise Windows-only games get false positives.

Verified against live data:

| Game | Depots | explicit macOS | supported |
|---|---|---|---|
| Terraria | 3 | 1 | ✅ |
| Hollow Knight | 3 | 1 | ✅ |
| DARK SOULS III | 4 | 0 | ❌ |

## What does not work

- **Steam account login.** This build has no login UI and always invokes
  DepotDownloader anonymously. Paid games — and free games that require
  ownership — are refused by Steam (`not available from this account`). The
  download pipeline itself is verified with apps that allow anonymous access
  (app 740, 2,813 files, on Windows; the arm64 tool binary is verified on macOS).
- **Playing what you downloaded.** A depot download is not a Steam install.
  There is no lua manifest injection on macOS and no Steamless equivalent for
  Mach-O, so downloaded games will not appear in your Steam library.
- **Anything Steamless does.** Steamless rewrites Windows PE files. It does not
  apply to macOS binaries.
- **A Gatekeeper-friendly signature.** The app is ad-hoc signed. The bundle
  seal is invalid, so `spctl` rejects it; after `xattr -dr` it runs, and that
  exact path is what CI verifies.

The macOS-equivalent path is [Goldberg](https://github.com/bitmap4/goldberg-mac-shim)
for `libsteam_api.dylib`, which this project does not implement.

## Build on macOS

```sh
git clone https://github.com/Whoismelv/Luatools-Macos
cd Luatools-Macos
chmod +x scripts/build-macos.sh
./scripts/build-macos.sh
open dist/osx-arm64/LuaTools.app
```

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

Verify the bundle before trusting it:

```sh
dist/osx-arm64/LuaTools.app/Contents/MacOS/LuaTools.App --selftest
```

Package it into a `.dmg`:

```sh
./scripts/make-dmg.sh dist/osx-arm64/LuaTools.app 1.3.2 arm64
```

If macOS blocks the app:

```sh
xattr -dr com.apple.quarantine dist/osx-arm64/LuaTools.app
```

## Cross-build from Windows

```powershell
.\scripts\publish-macos.ps1 -All      # osx-arm64 + osx-x64
```

Produces `dist/<rid>/LuaTools.app`. Copy the folder to a Mac and run it. No Mac
toolchain needed — the runtime is self-contained (~104 MB, no .NET install).

Warm build: ~3 s. First publish: ~150 s (runtime packs download once).

## Layout

```
src/LuaTools.Core/        Steam logic, no UI
  Vdf.cs                  Valve KeyValues parser (libraryfolders.vdf, .acf)
  SteamLocator.cs         install discovery + manifest reading
  SteamApi.cs             app list, depot metadata, oslist parsing
  DepotDownloaderRunner.cs tool acquisition + process orchestration
  BuildStore.cs           luabuilds_*.lua read/write
  ConfigStore.cs          settings

src/LuaTools.App/         Avalonia UI
  ViewModels/MainViewModel.cs
  Views/MainWindow.axaml

inject/                   macOS injection research harness (see its README)
scripts/                  build + publish
```

## Injection research

`inject/` asks one question: can we get into Steam's client on macOS? The
shipped bootstrapper was measured on a real `macos-14` runner — see
[docs/steam-macos-analysis.md](docs/steam-macos-analysis.md) for the raw
evidence:

- `Steam.app/Contents/MacOS/steam_osx` is **x86_64-only** (runs under Rosetta on
  Apple Silicon), **hardened runtime** (`flags=0x10000(runtime)`), notarized, and
  signed by Valve (`MXGJJ98X76`).
- It has **no** `com.apple.security.cs.allow-dyld-environment-variables` and
  **no** `com.apple.security.cs.disable-library-validation` entitlement.
- It contains none of the Windows LuaTools strings (`stplug-in`, `addappid`,
  `luabuilds`, `depotcache`, `appmanifest`).
- On a CI runner with **SIP disabled**, a correctly-architected x86_64 dylib did
  load into the bootstrapper. That is not evidence for a default SIP-enabled
  Mac; the entitlements say `DYLD_*` should be stripped there.
- The full inner client that the bootstrapper downloads was **not** captured:
  the `steam_osx` found under `~/Library/Application Support/Steam` was a 4.4 MB
  copy of the bootstrapper itself.

This contradicts the earlier fakepad-derived claim that the shipped Steam client
has no hardened runtime. The inner client is still unmeasured. Do not build a
feature on this until `inject/probe.sh` reports a landing on a SIP-enabled Mac.

The harness compiles a dylib that logs its own load, walks the dyld image list,
and resolves a signature table over `steam_osx`'s `__TEXT`. The signature table
ships placeholders — finding the real license-check signatures on arm64 is
unfinished work.

```sh
cd inject
make
./probe.sh          # writes probe-<timestamp>.txt — start here
```

## Verified numbers

| Step | Time |
|---|---|
| `dotnet build` (cold) | 7.6 s |
| `dotnet build` (warm) | 3.3 s |
| `publish -r osx-arm64` (warm) | 3.1 s |
| bundle size | 104 MB |

## License

MIT. Not affiliated with Valve or LuaTools.

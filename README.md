# LuaTools for macOS

A macOS port of [LuaTools](https://lua.tools) — the Steam depot manager.
Fetches Steam depots, manages `luabuilds_*.lua`, and keeps the same
manifest workflow on Apple Silicon.

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

Open the disk image, drag `LuaTools.app` onto **Applications**, then allow the
first launch — the build is ad-hoc signed, not notarized:

```sh
xattr -dr com.apple.quarantine /Applications/LuaTools.app
open /Applications/LuaTools.app
```

Or: System Settings → Privacy & Security → **Open Anyway**.

The release is built by [`.github/workflows/release.yml`](.github/workflows/release.yml)
on a `macos-14` runner, because `hdiutil` (the only real DMG tool) exists only
on macOS. Each build is ad-hoc signed and verified; the arm64 build additionally
runs `LuaTools.App --selftest` on the runner before packaging.

---

## What works

| Feature | State |
|---|---|
| Steam discovery (macOS / Windows / Linux paths) | ✅ works |
| Library + `appmanifest_*.acf` parsing | ✅ works, verified against a real 11-game install |
| Steam app list + depot metadata | ✅ works |
| macOS depot detection (`config.oslist`) | ✅ works, verified |
| DepotDownloader acquisition + invocation | ✅ works |
| Progress parsing | ✅ works |
| `luabuilds_*.lua` read/write | ✅ works |
| `--selftest` headless check | ✅ works (VDF/ACF, filter, storage, live Steam) |
| Steam client injection | ❌ not implemented |
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

- **Anonymous downloads of paid games.** Steam rejects anonymous access to
  client app depots (`not available from this account`). You need an account
  that owns the game, or a depot key.
- **Playing what you downloaded.** A depot download is not a Steam install.
  There is no lua manifest injection on macOS and no Steamless equivalent for
  Mach-O, so downloaded mac games will not appear in your Steam library.
- **Anything Steamless does.** Steamless rewrites Windows PE files. It does not
  apply to macOS binaries.

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

`inject/` answers one question: can we get into Steam's inner client on macOS?

Two open-source projects established that it is possible —
[fakepad](https://github.com/loks0n/fakepad) (Steam's `steam_osx` has no
hardened runtime; the `.app` bootstrapper strips `DYLD_*`, so you launch the
inner client under a pty) and
[Bridgeplay](https://github.com/raymondchins/Bridgeplay) (signature-scanning
dylib injected into `steam_osx`).

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

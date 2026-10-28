# luatools-inject

Injection harness for Steam on macOS. This is **layer 2** of the LuaTools
macOS port — the part that gets our code inside `steam_osx` so layers 3 and 4
can hook it.

Nothing here bypasses anything yet. It is an instrument: it proves whether
injection works on your specific Steam build and tells us what the client looks
like from the inside. Everything else is guesswork until this runs.

## What is actually known (measured)

The shipped bootstrapper was probed on a real `macos-14` runner — see
[`../docs/steam-macos-analysis.md`](../docs/steam-macos-analysis.md):

- `steam_osx` is 4.6 MB, **x86_64-only**, **hardened runtime**
  (`flags=0x10000(runtime)`), notarized, signed by Valve (`MXGJJ98X76`).
- It has **no** `com.apple.security.cs.allow-dyld-environment-variables` and
  **no** `com.apple.security.cs.disable-library-validation`.
- It contains no `stplug-in` / `addappid` / `luabuilds` / `depotcache` /
  `appmanifest` strings.
- On a runner with **SIP disabled**, a correctly-architected x86_64 dylib did
  land. That is not evidence for a default SIP-enabled Mac.
- The full inner client was not captured; the `steam_osx` found under
  `~/Library/Application Support/Steam` was a 4.4 MB copy of the bootstrapper.

This contradicts the earlier fakepad-derived assumption that the shipped
`steam_osx` has no hardened runtime. Fakepad may be talking about the inner
client. Treat that as unmeasured until `probe.sh` runs on your Mac.

The harness is still useful: it answers the question with evidence on the exact
machine that matters.

## What it does

`luatools-inject.dylib`:

1. logs that it loaded, with pid/ppid
2. enumerates every dyld image and locates `__TEXT` of the main executable
3. optionally scans a signature table over `__TEXT`

Injection only, no hooks. Hooks come after we have real signatures.

## Build

```sh
make            # -> build/luatools-inject.dylib + build/ptylaunch
```

Universal (arm64 + x86_64), ad-hoc signable.

## Run

```sh
chmod +x probe.sh && ./probe.sh        # step 1: know your target
```

Then, once probe confirms injection is allowed:

```sh
export DYLD_INSERT_LIBRARIES="$PWD/build/luatools-inject.dylib"
export LUATOOLS_SCAN=1
export LUATOOLS_LOG=/tmp/luatools-inject.log
./build/ptylaunch /Applications/Steam.app/Contents/MacOS/steam_osx
cat /tmp/luatools-inject.log
```

If the log shows `======== injection landed ========`, layer 2 works on that
machine with SIP as it is. Record `csrutil status` next to the result.

## Signature table

`src/inject.c` ships placeholder patterns. They exist to prove the scanner
runs, not to find anything real. Replacing them is the actual research work:

1. run with `LUATOOLS_SCAN=1`, note which resolve
2. for each real target (license check, manifest lookup), find the function in
   a disassembler on the live arm64 slice
3. take 16-24 bytes around a stable instruction, wildcard the branch offsets
4. add to the table, verify it resolves across two Steam builds

Bridgeplay's `signatures/macos.arm64/` is a working reference for the format
and for how build-to-build drift is handled.

## Layout

```
inject/src/inject.c       dylib entry, image walk, signature resolution
inject/src/pattern.c      IDA-style wildcard byte scanner
inject/src/ptylaunch.c    forkpty launcher so DYLD_* survives
inject/probe.sh           macOS-side recon, writes probe-<ts>.txt
```

## Status

The dylib and scanner have not been built on a Mac here. The only injection
evidence so far is the CI probe: x86_64 dylib, SIP **disabled**, landed in the
bootstrapper. The decisive run is `probe.sh` on a SIP-enabled Apple Silicon Mac
with the full Steam client installed.

The expected problems on first real run: struct packing, `__TEXT` offset
assumptions, code signing of the dylib, and whether Steam re-execs through the
bootstrapper. Find those before building hooks on top.

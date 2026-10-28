# luatools-inject

Injection harness for Steam on macOS. This is **layer 2** of the LuaTools
macOS port — the part that gets our code inside `steam_osx` so layers 3 and 4
can hook it.

Nothing here bypasses anything yet. It is an instrument: it proves whether
injection works on your specific Steam build and tells us what the client looks
like from the inside. Everything else is guesswork until this runs.

## Why this is possible at all

Two open-source projects established the facts:

- **loks0n/fakepad** — Steam's inner client (`steam_osx`) has no hardened
  runtime and no library validation, so `DYLD_INSERT_LIBRARIES` loads. The
  outer `.app` bootstrapper *does* have the hardened runtime and strips `DYLD_*`
  before exec'ing the real client. Their fix: a launcher that sets the variable
  itself and starts the inner client under a controlling pty so Steam doesn't
  re-exec through the bootstrapper.
- **raymondchins/Bridgeplay** — a production dylib injected into `steam_osx`
  and Steam Helper, using byte-signature scanning (`signatures/macos.arm64/`)
  instead of fixed addresses, and re-patching itself after Steam updates.

This repo is the same shape, much smaller, aimed at a different target.

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

If the log shows `======== injection landed ========`, layer 2 is done.

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

Untested. Written on Windows, cannot be compiled here. First run on a Mac will
surface the real problems: struct packing, `__TEXT` offset assumptions, and
whether Steam's inner client on your version re-execs.

That is the point. It is cheaper to find those out now than after a month of
building on assumptions.

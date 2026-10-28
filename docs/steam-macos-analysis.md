# Steam.app on macOS — measured, not assumed

Date: 2026-10-29
Host: GitHub Actions `macos-14-arm64`, macOS 14.8.9 (23J631), Apple Silicon,
`csrutil status` = **disabled** (this matters for the injection test).
Workflow: [`.github/workflows/steam-probe.yml`](../.github/workflows/steam-probe.yml)
Runs: `40780235377` (first pass), `40781605759` (corrected x86_64 pass).

Everything below comes from Valve's own installer,
`https://cdn.akamai.steamstatic.com/client/installer/steam.dmg`, mounted
read-only. No file inside `Steam.app` was modified.

## The installer

| Item | Value |
|---|---|
| `steam.dmg` size | 3.3 MB |
| compression | `lzfse encoded, lzvn compressed` |
| SHA-256 | `e1abfbaaa7be3e0f48a0e0d4c313233e05861b1d26af32830ddc838be88f2691` |
| `Steam.app` on disk | 6.9 MB |

So the download is a **bootstrapper**, not the whole client. The client is
fetched on first launch.

## Bundle internals

```
Steam.app/Contents/
  MacOS/
    steam_osx          4,599,104 bytes, Mach-O 64-bit executable x86_64
    steam.sh -> steam_osx          (a symlink, not a shell script)
    public -> ../Resources/public  (a symlink)
  Frameworks/
    Breakpad.framework               i386 + x86_64 + arm64
    crashhandler.dylib               x86_64 + arm64
  Resources/
    Assets.car, Steam.icns, public/
```

The **only** x86_64-only Mach-O is the one that matters: `steam_osx`. The
crash reporter is universal.

## Code signature of the bootstrapper

```
Identifier=com.valvesoftware.steam
Format=app bundle with Mach-O universal (x86_64)
CodeDirectory v=20500 size=35747 flags=0x10000(runtime) hashes=1106+7
Authority=Developer ID Application: Valve Corporation (MXGJJ98X76)
Authority=Developer ID Certification Authority
Authority=Apple Root CA
Timestamp=Mar 3, 2026 at 12:40:16
Notarization Ticket=stapled
TeamIdentifier=MXGJJ98X76
Runtime Version=14.2.0
```

Key facts:

- `flags=0x10000(runtime)` means the **hardened runtime is enabled**.
- The ticket is **stapled**; Gatekeeper accepts Valve's signature.
- No `__RESTRICT` segment.

Entitlements:

```
com.apple.application-identifier            TWKR2QMXJV.com.valvesoftware.Steam
com.apple.developer.aps-environment         production
com.apple.developer.associated-domains      *
com.apple.developer.hid.virtual.device      true
com.apple.developer.team-identifier         MXGJJ98X76
com.apple.security.automation.apple-events  true
com.apple.security.device.audio-input       true
keychain-access-groups                      TWKR2QMXJV.*
```

There is **no** `com.apple.security.cs.allow-dyld-environment-variables` and
**no** `com.apple.security.cs.disable-library-validation`. Under Apple's
documented hardened-runtime rules, `DYLD_INSERT_LIBRARIES` is therefore not
honoured for this binary on a default, SIP-enabled Mac.

> This contradicts the fakepad-derived assumption that Steam's `steam_osx` has
> no hardened runtime. Fakepad may be describing a *different* binary (the inner
> client after the bootstrapper updates), which this probe did not reach. The
> shipped bootstrapper is hardened and notarized; the inner client is still
> unmeasured.

## Strings in the bootstrapper

`strings -a` on the 4.6 MB `steam_osx`:

| term | hits |
|---|---|
| `stplug-in` | 0 |
| `addappid` | 0 |
| `luabuilds` | 0 |
| `depotcache` | 0 |
| `appmanifest` | 0 |
| `libraryfolders` | 0 |
| `steamapps` | 3 |

There is no Windows LuaTools code path in the shipped bootstrapper.

## First launch

Running the bootstrapper created:

```
~/Library/Application Support/Steam/Steam.AppBundle/Steam/Contents/MacOS/steam_osx
```

That file is **4.4 MB, x86_64, and has the same signature** as the bundled
bootstrapper. It is a copy of the bootstrapper, not the full client. The probe
stopped here (the process was killed after the file appeared). The full client
payload and its eventual location are **not yet measured**.

## Injection test

First pass used an arm64 dylib against the x86_64 binary. dyld rejected it with
`incompatible architecture (have 'arm64', need 'x86_64')` — that result meant
nothing about injection.

Second pass built the dylib with `clang -arch x86_64` and ran the binary under
`arch -x86_64`:

```
RESULT: INJECTION LANDED in the bootstrapper (note: this runner has SIP disabled)
```

This is a real landing, but on a runner with **SIP disabled**. It does not prove
that injection works on a normal Mac. Given the missing dyld entitlements, the
expected behaviour on a default SIP-enabled Mac is that dyld strips the
variable. The decisive test is [`inject/probe.sh`](../inject/probe.sh) on a
SIP-enabled Apple Silicon Mac.

## What this means for the port

- The macOS port's decision to use DepotDownloader instead of client injection
  is justified by the evidence: the shipped client is hardened, notarized,
  Intel-only, and contains no `stplug-in` path.
- Do not claim injection support, and do not ship the `inject/` harness as a
  feature.
- Do not claim the client is arm64 native. It runs under Rosetta on M1.
- The open question is the inner client. Run Steam to completion on a real Mac,
  then inspect the Mach-O files under `~/Library/Application Support/Steam` and
  repeat the signature/entitlement check. If the inner client is also hardened,
  the injection idea is dead on stock macOS.

## Reproduce

```sh
gh workflow run steam-probe.yml --repo Whoismelv/Luatools-Macos
gh run watch --repo Whoismelv/Luatools-Macos
```

The workflow is read-only with respect to Valve's code. It writes only to the
runner's disposable `$HOME`.

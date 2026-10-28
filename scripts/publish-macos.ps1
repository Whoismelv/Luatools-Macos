# Cross-builds a macOS .app bundle from Windows.
# Produces dist/osx-arm64/LuaTools.app and dist/osx-x64/LuaTools.app as folders —
# copy the .app folder to the Mac, it runs there. No Mac toolchain needed here.
#
#   .\scripts\publish-macos.ps1            # both arches
#   .\scripts\publish-macos.ps1 -Rid osx-arm64

param(
    [string]$Rid = "osx-arm64",
    [string]$Configuration = "Release",
    [switch]$All
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\LuaTools.App\LuaTools.App.csproj"
$rids = if ($All) { @("osx-arm64", "osx-x64") } else { @($Rid) }

foreach ($r in $rids) {
    $out   = Join-Path $root "dist\$r"
    $pub   = Join-Path $out "publish"
    $app   = Join-Path $out "LuaTools.app"
    $macos = Join-Path $app "Contents\MacOS"
    $res   = Join-Path $app "Contents\Resources"

    Write-Host "==> publishing $r"
    if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
    if (Test-Path $app) { Remove-Item $app -Recurse -Force }

    & dotnet publish $proj -c $Configuration -r $r --self-contained true `
        -p:PublishTrimmed=false -p:UseAppHost=true -o $pub
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $r" }

    Write-Host "==> assembling bundle"
    New-Item -ItemType Directory -Force -Path $macos, $res | Out-Null
    Copy-Item "$pub\*" $macos -Recurse -Force

    $plist = @'
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
'@
    $plist | Set-Content -Path (Join-Path $app "Contents\Info.plist") -Encoding UTF8 -NoNewline
    [System.IO.File]::WriteAllText((Join-Path $app "Contents\PkgInfo"), "APPL????")

    $icon = Join-Path $root "assets\LuaTools.icns"
    if (Test-Path $icon) { Copy-Item $icon $res -Force }

    Write-Host "built: $app"
}

Write-Host ""
Write-Host "Copy dist\<rid>\LuaTools.app to the Mac, then on the Mac:"
Write-Host "  xattr -dr com.apple.quarantine LuaTools.app"
Write-Host "  open LuaTools.app"

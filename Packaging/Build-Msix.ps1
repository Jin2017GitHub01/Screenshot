<#
.SYNOPSIS
  Builds a Microsoft Store-ready .msixbundle (x64 + arm64) for Capture Everything.

.DESCRIPTION
  1. Generates logo assets and resources.pri
  2. dotnet publish (self-contained) for each architecture
  3. makeappx pack per architecture, then makeappx bundle
  Output: dist\CaptureEverything_<version>.msixbundle  -> upload this in Partner Center.

  The package is left unsigned; the Store signs it on ingestion.

.EXAMPLE
  .\Packaging\Build-Msix.ps1 -IdentityName "12345Jin.CaptureEverything" `
      -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" `
      -PublisherDisplayName "Jin" -Version 1.0.0.0
#>
param(
    # Partner Center > Product management > Product identity > Package/Identity/Name
    [Parameter(Mandatory)] [string]$IdentityName,
    # Partner Center > Product identity > Package/Identity/Publisher (starts with "CN=")
    [Parameter(Mandatory)] [string]$Publisher,
    # Partner Center > Product identity > Package/Properties/PublisherDisplayName
    [Parameter(Mandatory)] [string]$PublisherDisplayName,
    # Must be 4 parts; the Store requires the last part to be 0. Increase for every submission.
    [string]$Version = '1.0.0.0',
    [string[]]$Architectures = @('x64', 'arm64')
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Version must look like 1.2.3.0 (four parts, last part 0 for the Store). Got '$Version'."
}
if ($Publisher -notmatch '^CN=') {
    throw "Publisher must start with 'CN=' (copy it exactly from Partner Center)."
}

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Screenshot.csproj'
$dist    = Join-Path $root 'dist'
$stage   = Join-Path $dist 'stage'      # assets + manifest only (for makepri)
$pkgDir  = Join-Path $dist 'packages'   # per-arch .msix files

# ---- Locate Windows SDK tools (newest version) ------------------------------
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows 10/11 SDK not found (need makeappx.exe and makepri.exe).' }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$makepri  = Join-Path $sdkBin.FullName 'x64\makepri.exe'

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $exe -Leaf) failed with exit code $LASTEXITCODE." }
}

function Get-Manifest([string]$arch) {
    $xml = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.template.xml') -Raw
    $xml = $xml.Replace('{{IDENTITY_NAME}}', [Security.SecurityElement]::Escape($IdentityName))
    $xml = $xml.Replace('{{PUBLISHER}}', [Security.SecurityElement]::Escape($Publisher))
    $xml = $xml.Replace('{{PUBLISHER_DISPLAY_NAME}}', [Security.SecurityElement]::Escape($PublisherDisplayName))
    $xml = $xml.Replace('{{VERSION}}', $Version)
    return $xml.Replace('{{ARCH}}', $arch)
}

# ---- Clean -------------------------------------------------------------------
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage, $pkgDir | Out-Null

# ---- Assets + resources.pri (shared by all architectures) -------------------
Write-Host "`n== Generating assets" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'New-StoreAssets.ps1') -OutDir (Join-Path $stage 'Assets')
Set-Content (Join-Path $stage 'AppxManifest.xml') (Get-Manifest 'x64') -Encoding UTF8

$priConfig = Join-Path $dist 'priconfig.xml'
Invoke-Tool $makepri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')
Invoke-Tool $makepri @('new', '/pr', $stage, '/cf', $priConfig,
    '/mn', (Join-Path $stage 'AppxManifest.xml'), '/of', (Join-Path $stage 'resources.pri'), '/o')

# ---- Publish + pack per architecture -----------------------------------------
$fileVersion = $Version  # assembly/file version mirrors the package version
foreach ($arch in $Architectures) {
    Write-Host "`n== Publishing $arch" -ForegroundColor Cyan
    $layout = Join-Path $dist "layout-$arch"

    Invoke-Tool 'dotnet' @('publish', $project, '-c', 'Release', '-r', "win-$arch",
        '--self-contained', 'true', '-p:DebugType=none', '-p:DebugSymbols=false',
        "-p:Version=$($Version.Substring(0, $Version.LastIndexOf('.')))",
        "-p:FileVersion=$fileVersion", "-p:AssemblyVersion=$fileVersion",
        '-o', $layout)

    Copy-Item (Join-Path $stage 'Assets') $layout -Recurse
    Copy-Item (Join-Path $stage 'resources.pri') $layout
    Set-Content (Join-Path $layout 'AppxManifest.xml') (Get-Manifest $arch) -Encoding UTF8

    Write-Host "`n== Packing $arch" -ForegroundColor Cyan
    $msix = Join-Path $pkgDir "CaptureEverything_$($Version)_$arch.msix"
    Invoke-Tool $makeappx @('pack', '/d', $layout, '/p', $msix, '/o')
}

# ---- Bundle ------------------------------------------------------------------
Write-Host "`n== Bundling" -ForegroundColor Cyan
$bundle = Join-Path $dist "CaptureEverything_$Version.msixbundle"
Invoke-Tool $makeappx @('bundle', '/d', $pkgDir, '/p', $bundle, '/bv', $Version, '/o')

Write-Host "`nDone. Upload this file in Partner Center:" -ForegroundColor Green
Write-Host "  $bundle"
Write-Host "`nTo test locally (needs Developer Mode), register an unpacked layout:"
Write-Host "  Add-AppxPackage -Register `"$(Join-Path $dist 'layout-x64\AppxManifest.xml')`""

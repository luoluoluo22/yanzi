#Requires -Version 5.1
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\cookie-api'),
    [switch]$AllowDevelopmentHost
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\miniapps\cookie-api'))
$hostDll = Join-Path $env:LOCALAPPDATA 'Yanzi\current\Yanzi.dll'
if (-not $AllowDevelopmentHost) {
    if (!(Test-Path -LiteralPath $hostDll)) { throw 'Yanzi formal installation not found.' }
    $version = [Version]([Diagnostics.FileVersionInfo]::GetVersionInfo($hostDll).FileVersion)
    if ($version -lt [Version]'1.1.16.0') {
        throw "Yanzi version $version does not support Cookie API adapter; update host first."
    }
}
$sourceManifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($sourceManifest.id -ne 'cookie-api' -or $sourceManifest.runtime -ne 'csharp') { throw 'Invalid packaged miniapp.' }
if (Test-Path -LiteralPath $Target) {
    $existingManifest = Join-Path $Target 'manifest.json'
    if (!(Test-Path -LiteralPath $existingManifest)) { throw 'Existing destination is not a registered Cookie API miniapp.' }
    $existing = Get-Content -LiteralPath $existingManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($existing.id -ne 'cookie-api') { throw 'Destination belongs to another miniapp.' }
}
New-Item -ItemType Directory -Path $Target -Force | Out-Null
foreach ($file in @('manifest.json','cookie_api.cs','README.md')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $Target $file) -Force
    if ((Get-FileHash (Join-Path $source $file) -Algorithm SHA256).Hash -ne
        (Get-FileHash (Join-Path $Target $file) -Algorithm SHA256).Hash) {
        throw "Installed file digest mismatch: $file"
    }
}
Write-Output "COOKIE_API_MINIAPP_INSTALLED=$Target"
Write-Output "COOKIE_API_FORMAL_HOST=$(-not $AllowDevelopmentHost)"

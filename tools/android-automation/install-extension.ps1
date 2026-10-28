param([switch]$Force)
$ErrorActionPreference = 'Stop'
$extensionsRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
$extensionId = 'yanzi-android-automation'
$target = Join-Path $extensionsRoot $extensionId
$backupRoot = Join-Path $extensionsRoot ('.yanzi-backups\' + $extensionId)
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

# A miniapp is installed and updated only in the user Extensions directory.
# This maintenance command validates the installed version and cleans up old
# backup directories that previously appeared as duplicate discoverable apps.
$manifestPath = Join-Path $target 'manifest.json'
$entryPath = Join-Path $target 'provider.cs'
if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $entryPath)) {
    throw 'yanzi-android-automation miniapp is absent or incomplete; first restore it using account sync.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.id -ne $extensionId -or $manifest.entry -ne 'provider.cs') {
    throw 'User extension manifest does not match the expected android automation miniapp.'
}
foreach ($old in @(Get-ChildItem $extensionsRoot -Directory -Filter ($extensionId + '.backup-*'))) {
    $legacyManifest = Join-Path $old.FullName 'manifest.json'
    if (!(Test-Path $legacyManifest)) { continue }
    try { $id = (Get-Content $legacyManifest -Raw -Encoding UTF8 | ConvertFrom-Json).id } catch { continue }
    if ($id -ne $extensionId) { continue }
    $destination = Join-Path $backupRoot ($old.Name + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
    Move-Item -LiteralPath $old.FullName -Destination $destination
    Write-Output ('Moved legacy duplicate backup: ' + $old.Name)
}
if ($Force) { Write-Warning 'Force does not replace user miniapp files. Edit the user Extensions folder directly.' }
Write-Output "Validated user-installed extension: $target"

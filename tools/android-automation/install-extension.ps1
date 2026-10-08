param([switch]$Force)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$extensionsRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
$extensionId = 'yanzi-android-automation'
$target = Join-Path $extensionsRoot $extensionId
# LocalExtensionCatalog enumerates all non-dot directories under Extensions.
# Backups MUST be placed inside a dot-prefixed directory to avoid duplicate IDs.
$backupRoot = Join-Path $extensionsRoot ('.yanzi-backups\' + $extensionId)
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

function New-BackupPath([string]$baseName) {
    $candidate = Join-Path $backupRoot $baseName
    $index = 1
    while (Test-Path $candidate) {
        $candidate = Join-Path $backupRoot ($baseName + '-' + $index)
        $index++
    }
    return $candidate
}
# One-time migration of legacy backups that were incorrectly discoverable.
$legacy = @(Get-ChildItem $extensionsRoot -Directory -Filter ($extensionId + '.backup-*'))
foreach ($old in $legacy) {
    $manifest = Join-Path $old.FullName 'manifest.json'
    if (!(Test-Path $manifest)) { continue }
    try { $id = (Get-Content $manifest -Raw | ConvertFrom-Json).id } catch { continue }
    if ($id -ne $extensionId) { continue }
    $destination = New-BackupPath $old.Name
    Move-Item -LiteralPath $old.FullName -Destination $destination
    Write-Output ('Moved legacy backup outside extension discovery: ' + $old.Name)
}
if (Test-Path $target) {
    if (-not $Force) { throw 'Extension already exists. Stop it and retry with -Force to update.' }
    $backup = New-BackupPath ('backup-' + (Get-Date -Format 'yyyyMMddHHmmss'))
    Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $target 'automation\src') -Force | Out-Null
Copy-Item (Join-Path $here 'extension\manifest.json') (Join-Path $target 'manifest.json') -Force
Copy-Item (Join-Path $here 'extension\provider.cs') (Join-Path $target 'provider.cs') -Force
Copy-Item (Join-Path $here 'src\*.mjs') (Join-Path $target 'automation\src\') -Force
$duplicates = @(Get-ChildItem $extensionsRoot -Directory | Where-Object {$_.Name -like ($extensionId+'.backup-*')})
if ($duplicates.Count -gt 0) { throw 'Legacy extension backups still exist under public catalog: ' + $duplicates.Count }
Write-Output "Installed extension to $target"

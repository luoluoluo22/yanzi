param(
    [string]$ExtensionsRoot = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'),
    [string]$SharedUiRoot = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\SharedUI'),
    [string]$SharedUiDll = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$base = $PSScriptRoot
if (-not $SharedUiDll) {
    $SharedUiDll = [IO.Path]::GetFullPath((Join-Path $base '..\..\src\Yanzi.UI.Wpf\bin\Release\net9.0-windows\Yanzi.UI.Wpf.dll'))
}
if (-not (Test-Path -LiteralPath $SharedUiDll -PathType Leaf)) {
    throw "Build the shared library first: $SharedUiDll"
}
$manifest = Get-Content (Join-Path $base 'source-hashes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($name in @('semantic-search', 'capability-lab')) {
    $record = $manifest.$name
    $dir = Join-Path $ExtensionsRoot $name
    $target = Join-Path $dir $record.entry
    $patch = Join-Path $base $record.patch
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Missing extension entry: $target"
    }
    $current = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($current -eq $record.modifiedSha256) {
        Write-Host "ALREADY_INSTALLED $name"
        continue
    }
    if ($current -ne $record.originalSha256) {
        throw "Source has changed, refusing to overwrite $name. Current SHA256: $current"
    }
    & git -C $dir apply --check $patch
    if ($LASTEXITCODE -ne 0) { throw "Patch incompatible: $name" }
    $backup = $target + '.before-shared-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $target -Destination $backup
    try {
        & git -C $dir apply $patch
        if ($LASTEXITCODE -ne 0) { throw "Patch failed: $name" }
        # git apply on Windows may convert LF sources to CRLF. Keep the
        # original extension source's LF convention for bytewise verification.
        $updated = [IO.File]::ReadAllText($target).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($target, $updated, $utf8)
        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($actual -ne $record.modifiedSha256) {
            throw "Patched source SHA256 mismatch for $name ($actual)"
        }
        Write-Host "PATCHED $name BACKUP=$backup"
    }
    catch {
        Copy-Item -LiteralPath $backup -Destination $target -Force
        throw
    }
}
New-Item -ItemType Directory -Force -Path $SharedUiRoot | Out-Null
$destination = Join-Path $SharedUiRoot 'Yanzi.UI.Wpf.dll'
$expected = (Get-FileHash -LiteralPath $SharedUiDll -Algorithm SHA256).Hash
if (-not (Test-Path -LiteralPath $destination) -or
    (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
    try {
        Copy-Item -LiteralPath $SharedUiDll -Destination $destination -Force
    }
    catch {
        # A .NET assembly loaded by the long-lived Yanzi host stays mapped
        # even after extension windows close. Never force/overwrite it.
        $pending = $destination + '.pending'
        Copy-Item -LiteralPath $SharedUiDll -Destination $pending -Force
        if ((Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash -ne $expected) {
            throw 'Cannot safely stage the shared UI update'
        }
        # Windows can atomically replace the mapped DLL on next boot,
        # after the host exits. This avoids forcing a running shell to stop.
        if (-not ('YanziSharedUiMoveOnRestart' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YanziSharedUiMoveOnRestart {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool MoveFileEx(string current, string destination, int flags);
}
'@
        }
        $scheduled = [YanziSharedUiMoveOnRestart]::MoveFileEx($pending, $destination, 0x5)
        if ($scheduled) {
            Write-Host "SHARED_UI_UPDATE_SCHEDULED_AT_NEXT_BOOT=$destination"
        } else {
            Write-Warning "Windows could not schedule the update (error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())). Rerun installer after host exits. Pending: $pending"
        }
        Write-Host "SHARED_UI_UPDATE_STAGED=$pending"
        $global:LASTEXITCODE = 0
        return
    }
}
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
    throw 'Shared UI assembly SHA256 mismatch'
}
Write-Host "SHARED_UI_READY=$destination"
$global:LASTEXITCODE = 0

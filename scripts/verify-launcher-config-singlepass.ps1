#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$p = Join-Path $root 'src\OpenQuickHost\Sync\PersonalSyncService.cs'
$source = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
$start = $source.IndexOf('private async Task<(WebDavLauncherConfigSnapshot? Snapshot, bool HasRemoteObjects)>')
$end = $source.IndexOf('private async Task<bool> UploadYanmStateAsync', $start)
if ($start -lt 0 -or $end -lt $start) { throw 'Launcher object scanner not found' }
$scan = $source.Substring($start, $end - $start)
$readCount = [regex]::Matches($scan, [regex]::Escape('_backend.TryReadBytesAsync(')).Count
if ($readCount -ne 1) { throw "Expected one backend read in each object iteration, found $readCount" }
if (-not $scan.Contains('foreach (var definition in LauncherConfigObjectStore.Definitions)')) {
    throw 'Not scanning all configured object definitions'
}
if (-not $scan.Contains('hasRemoteObjects |= bytes is { Length: > 0 };')) {
    throw 'Remote presence semantics lost for malformed data'
}
if (-not $scan.Contains('LauncherConfigObjectStore.Deserialize(bytes)')) {
    throw 'Remote object validation lost'
}
if ($source -match 'private async Task<bool> HasLauncherConfigObjectsAsync') {
    throw 'Redundant launcher presence pre-scan still present'
}
if (-not $source.Contains('var (objectSnapshot, hasRemoteObjects) =') -or
    -not $source.Contains('if (hasRemoteObjects)')) {
    throw 'Launcher sync no longer distinguishes absent remote objects'
}
if (-not $source.Contains('var (objectSnapshot, _) = await TryLoadLauncherConfigObjectsAsync(')) {
    throw 'Legacy YanM migration path no longer reads remote object snapshot'
}
# Simulate old vs new remote GET count, separately from production network.
# This establishes the structural bound, not a claim of real HTTP savings.
$definitions = 14
foreach ($firstPresent in @(1, 3, 14)) {
    $oldReads = $firstPresent + $definitions
    $newReads = $definitions
    if ($oldReads - $newReads -ne $firstPresent) { throw 'GET accounting error' }
    Write-Output "PASS: first remote object at $firstPresent => $oldReads to $newReads GETs (modeled)"
}
Write-Output 'LAUNCHER_CONFIG_SINGLE_PASS_CONTRACT_PASS'

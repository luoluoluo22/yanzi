<#
.SYNOPSIS
  Read-only same-window Yanzi sync log counters. No network, credentials, or data mutations.
.DESCRIPTION
  Log lines are not physical HTTP requests. The Gitee sentTotal values are emitted
  only as snapshots, not a reliable delta across handler restarts or log rotations.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][datetime]$Start,
    [Parameter(Mandatory=$true)][datetime]$End,
    [string]$LogRoot = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\logs'),
    [switch]$IncludeRotated
)

$ErrorActionPreference = 'Stop'
if ($End -le $Start) { throw 'End must be strictly later than Start.' }

$names = @('cloud-sync-diagnostics.log', 'shell.log')
if ($IncludeRotated) { $names += @('cloud-sync-diagnostics.log.1', 'shell.log.1') }
$counts = [ordered]@{
    matchedLogLines = 0
    logicalGiteeReads = 0
    extensionFileChangeLines = 0
    extensionWatcherBatchLines = 0
    conflictKeywordLines = 0
    personalSyncLines = 0
    physicalGetMetricLines = 0
}
$files = New-Object System.Collections.Generic.List[object]
$metrics = New-Object System.Collections.Generic.List[object]
$culture = [System.Globalization.CultureInfo]::InvariantCulture
foreach ($name in $names) {
    $path = Join-Path $LogRoot $name
    if (-not [System.IO.File]::Exists($path)) { continue }
    $matched = 0
    foreach ($line in [System.IO.File]::ReadLines($path)) {
        if ($line.Length -lt 25 -or $line[0] -ne '[') { continue }
        $timestamp = [datetime]::MinValue
        $prefix = $line.Substring(1, 23)
        if (-not [datetime]::TryParseExact(
            $prefix, 'yyyy-MM-dd HH:mm:ss.fff', $culture,
            [System.Globalization.DateTimeStyles]::None, [ref]$timestamp)) { continue }
        if ($timestamp -lt $Start -or $timestamp -ge $End) { continue }
        $matched++
        $counts.matchedLogLines++
        if ($line.Contains('Read requested')) { $counts.logicalGiteeReads++ }
        if ($line.Contains('extension-file-change')) { $counts.extensionFileChangeLines++ }
        if ($line.Contains('Extension watcher batch')) { $counts.extensionWatcherBatchLines++ }
        if ($line -match 'same-version|conflict|冲突') { $counts.conflictKeywordLines++ }
        if ($line.Contains('Personal sync')) { $counts.personalSyncLines++ }
        if ($line -match 'Gitee.*(GET|HttpReadMetrics).*sentTotal=([0-9]+)') {
            $counts.physicalGetMetricLines++
            $metrics.Add([pscustomobject]@{ at = $timestamp; sentTotal = [long]$Matches[2] })
        }
    }
    $files.Add([pscustomobject]@{ name = $name; matchedLines = $matched })
}

$sorted = @($metrics | Sort-Object at)
$first = $null
$last = $null
if ($sorted.Count -gt 0) {
    $first = $sorted[0].sentTotal
    $last = $sorted[$sorted.Count - 1].sentTotal
}
[pscustomobject]@{
    schema = 'yanzi.phase1.log-window.v1'
    startBeijing = $Start.ToString('yyyy-MM-dd HH:mm:ss')
    endBeijingExclusive = $End.ToString('yyyy-MM-dd HH:mm:ss')
    sourceFiles = @($files.ToArray())
    counts = $counts
    physicalSentTotalFirstSnapshot = $first
    physicalSentTotalLastSnapshot = $last
    note = 'Read requested is logical, not HTTP; sentTotal snapshots cannot be subtracted across handler restarts; identical workloads required for comparisons.'
} | ConvertTo-Json -Depth 6 -Compress

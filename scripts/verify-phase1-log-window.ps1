$ErrorActionPreference='Stop'
$root=Join-Path ([System.IO.Path]::GetTempPath()) ('yanzi-phase1-log-fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force|Out-Null
try {
    $cloud=@(
        '[2026-11-01 20:24:59.999] [GiteeBackend] Read requested before',
        '[2026-11-01 20:25:00.000] [GiteeBackend] Read requested token=PRIVATE_VALUE',
        '[2026-11-01 20:25:02.000] [GiteeBackend] Read requested',
        '[2026-11-01 20:35:00.000] [GiteeBackend] Read requested after'
    )
    $shell=@(
        '[2026-11-01 20:25:01.000] extension-file-change',
        '[2026-11-01 20:25:02.000] Extension watcher batch: events=20',
        '[2026-11-01 20:25:03.000] same-version conflict token=PRIVATE_VALUE',
        '[2026-11-01 20:25:04.000] Personal sync auto',
        '[2026-11-01 20:25:05.000] Gitee HTTP GET metrics sentTotal=100',
        '[2026-11-01 20:29:05.000] Gitee HTTP GET metrics sentTotal=145'
    )
    [System.IO.File]::WriteAllLines((Join-Path $root 'cloud-sync-diagnostics.log'), $cloud)
    [System.IO.File]::WriteAllLines((Join-Path $root 'shell.log'), $shell)
    $script=Join-Path $PSScriptRoot 'measure-phase1-log-window.ps1'
    $json= & $script -Start ([datetime]'2026-11-01 20:25:00') -End ([datetime]'2026-11-01 20:35:00') -LogRoot $root
    $v=$json|ConvertFrom-Json
    if ($v.counts.logicalGiteeReads -ne 2) {throw 'Read count wrong'}
    if ($v.counts.extensionFileChangeLines -ne 1 -or $v.counts.extensionWatcherBatchLines -ne 1) {throw 'Watcher count wrong'}
    if ($v.counts.conflictKeywordLines -ne 1 -or $v.counts.personalSyncLines -ne 1) {throw 'Sync/conflict count wrong'}
    if ($v.counts.physicalGetMetricLines -ne 2 -or $v.physicalSentTotalFirstSnapshot -ne 100 -or $v.physicalSentTotalLastSnapshot -ne 145) {throw 'Metric snapshots wrong'}
    if ($json.Contains('PRIVATE_VALUE')) {throw 'Sensitive content leaked'}
    if ($v.counts.matchedLogLines -ne 8) {throw 'Window boundary wrong'}
    $bad=$false
    try { & $script -Start ([datetime]'2026-11-01 20:35:00') -End ([datetime]'2026-11-01 20:25:00') -LogRoot $root | Out-Null } catch { $bad=$true }
    if (-not $bad) {throw 'Invalid window accepted'}
    Write-Output 'PASS: 2 logical reads, watcher 1+1, conflict 1, sync 1, physical metrics 2, exclusive boundary, redaction, invalid-window guard.'
} finally {
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}

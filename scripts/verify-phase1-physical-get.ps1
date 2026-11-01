$ErrorActionPreference='Stop'
$d=Join-Path ([IO.Path]::GetTempPath()) ('yanzi-http-window-fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $d -Force|Out-Null
try{
 $lines=@(
 '[2026-11-01 20:24:59.999] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=99, completed=99, sentTotal=99, success2xx=99, notFound404=0, retryable429Or5xx=0, transportErrors=0, canceled=0',
 '[2026-11-01 20:25:01.000] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=10, completed=9, sentTotal=110, success2xx=8, notFound404=1, retryable429Or5xx=0, transportErrors=0, canceled=0, secret=PRIVATE',
 '[2026-11-01 20:26:01.000] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=3, completed=4, sentTotal=3, success2xx=3, notFound404=0, retryable429Or5xx=1, transportErrors=0, canceled=0',
 '[2026-11-01 20:27:01.000] [GiteeBackend] Read requested sent=200',
 '[2026-11-01 20:35:00.000] [GiteeBackend.Http] GET transport window | sent=500'
 )
 [IO.File]::WriteAllLines((Join-Path $d 'cloud-sync-diagnostics.log'),$lines)
 $v=(& (Join-Path $PSScriptRoot 'measure-phase1-physical-get.ps1') -Start ([datetime]'2026-11-01 20:25:00') -End ([datetime]'2026-11-01 20:35:00') -LogRoot $d)|ConvertFrom-Json
 if($v.counts.reportedWindows -ne 2 -or $v.counts.physicalGetSentClosedWindows -ne 13 -or $v.counts.physicalGetCompletedClosedWindows -ne 13){throw 'Physical GET count or boundary mismatch'}
 if($v.counts.success2xx -ne 11 -or $v.counts.retryable429Or5xx -ne 1 -or $v.exactWallClockCount){throw 'Status or scope mismatch'}
 if(($v|ConvertTo-Json -Depth 6) -match 'PRIVATE'){throw 'Sensitive content leaked'}
 $bad=$false;try{& (Join-Path $PSScriptRoot 'measure-phase1-physical-get.ps1') -Start ([datetime]'2026-11-01 20:35:00') -End ([datetime]'2026-11-01 20:25:00') -LogRoot $d|Out-Null}catch{$bad=$true};if(-not $bad){throw 'Invalid interval accepted'}
 'PASS: 2 closed windows, 13 physical GET sends, handler counter reset, exclusive boundaries, redaction, invalid interval'
}finally{Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue}

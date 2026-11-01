$ErrorActionPreference='Stop'
$d=Join-Path ([IO.Path]::GetTempPath()) ('yanzi-http-window-fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $d -Force|Out-Null
try{
 $lines=@(
 '[2026-11-01 20:24:59.999] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=99, completed=99, sentTotal=99, success2xx=99, notFound404=0, retryable429Or5xx=0, otherHttp=0, transportErrors=0, canceled=0',
 '[2026-11-01 20:25:01.000] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=10, completed=9, sentTotal=110, success2xx=7, notFound404=1, retryable429Or5xx=0, otherHttp=1, transportErrors=0, canceled=0, secret=PRIVATE',
 '[2026-11-01 20:26:01.000] [GiteeBackend.Http] GET transport window | windowSeconds=60, sent=3, completed=4, sentTotal=3, success2xx=3, notFound404=0, retryable429Or5xx=1, otherHttp=0, transportErrors=0, canceled=0',
 '[2026-11-01 20:27:01.000] [GiteeBackend] Read requested sent=200',
 '[2026-11-31 20:27:02.000] [GiteeBackend.Http] GET transport window | sent=500',
 '[2026-11-01 20:28:00.000] [GiteeBackend.Http] GET transport window | sent=2, completed=2, success2xx=1, notFound404=0, retryable429Or5xx=0, otherHttp=0, transportErrors=0, canceled=0',
 '[2026-11-01 20:29:00.000] [GiteeBackend.Http] GET transport window | sent=1, completed=1, success2xx=1, notFound404=0, retryable429Or5xx=0, transportErrors=0, canceled=0',
 '[2026-11-01 20:35:00.000] [GiteeBackend.Http] GET transport window | sent=500'
 )
 [IO.File]::WriteAllLines((Join-Path $d 'cloud-sync-diagnostics.log'),$lines)
 $v=(& (Join-Path $PSScriptRoot 'measure-phase1-physical-get.ps1') -Start ([datetime]'2026-11-01 20:25:00') -End ([datetime]'2026-11-01 20:35:00') -LogRoot $d)|ConvertFrom-Json
 if($v.counts.reportedWindows -ne 2 -or $v.counts.physicalGetSentClosedWindows -ne 13 -or $v.counts.physicalGetCompletedClosedWindows -ne 13){throw 'Physical GET count or boundary mismatch'}
 if($v.counts.success2xx -ne 10 -or $v.counts.otherHttp -ne 1 -or $v.counts.retryable429Or5xx -ne 1 -or $v.exactWallClockCount){throw 'Status or scope mismatch'}
 if($v.counts.invalidTimestamps -ne 1 -or $v.counts.invalidWindows -ne 1 -or $v.counts.missingFields -ne 1){throw 'Corrupt records not quarantined'}
 if($v.coverageComplete -or $v.coverageStatus -ne 'PARTIAL_CLOSED_WINDOWS_ONLY'){throw 'Partial coverage not declared'}
 $empty=(& (Join-Path $PSScriptRoot 'measure-phase1-physical-get.ps1') -Start ([datetime]'2026-11-01 20:36:00') -End ([datetime]'2026-11-01 20:37:00') -LogRoot $d)|ConvertFrom-Json
 if($empty.counts.reportedWindows -ne 0 -or $empty.coverageComplete -or $empty.coverageStatus -ne 'INSUFFICIENT_NO_CLOSED_WINDOWS'){throw 'Empty coverage incorrectly accepted'}
 if(($v|ConvertTo-Json -Depth 6) -match 'PRIVATE'){throw 'Sensitive content leaked'}
 $bad=$false;try{& (Join-Path $PSScriptRoot 'measure-phase1-physical-get.ps1') -Start ([datetime]'2026-11-01 20:35:00') -End ([datetime]'2026-11-01 20:25:00') -LogRoot $d|Out-Null}catch{$bad=$true};if(-not $bad){throw 'Invalid interval accepted'}
 'PASS: 2 closed windows, 13 physical GET sends, otherHttp classification, malformed timestamp, invalid status sum, missing fields, incomplete coverage, redaction, invalid interval'
}finally{Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue}

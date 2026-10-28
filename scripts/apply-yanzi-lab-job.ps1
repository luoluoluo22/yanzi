#requires -version 5.1
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$JobId,
  [Parameter(Mandatory=$true)][ValidateSet('title','duration','tags')][string]$Task
)
$ErrorActionPreference='Stop'
if($JobId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Invalid JobId'}
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lab=Join-Path $repo '.tmp\yanzi-iteration-lab'
$keyFile=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$token=[IO.File]::ReadAllText($keyFile).Trim()
$headers=@{Authorization='Bearer '+$token}
$job=Invoke-RestMethod ('http://127.0.0.1:53921/api/jobs/'+$JobId) -Headers $headers -TimeoutSec 5
if($job.status -ne 'success'){throw ('Job not successfully completed: '+$job.status)}
$text=[string]$job.data.text
if(-not $text -or $text.Length -gt 4000){throw 'Expected bounded JSON response'}
$payload=$text|ConvertFrom-Json
if($payload.task -ne $Task){throw 'Job proposal task mismatch'}
$proposal=Join-Path $lab ($Task+'-actual-'+$JobId+'.json')
[IO.File]::WriteAllText($proposal,$text,[Text.UTF8Encoding]::new($false))
$executor=Join-Path $repo 'tools\chatgpt-bridge\lab-patch-executor.mjs'
$reply=& node $executor --input $proposal
$exit=$LASTEXITCODE
$verdict=$reply|ConvertFrom-Json
'MODEL_JOB='+$JobId
'TASK='+$Task
'EXECUTOR_STATUS='+$verdict.status
'TEST_PASSED='+$verdict.testPassed
'TEST_UNCHANGED='+$verdict.testsUnchanged
'ROLLBACK='+$verdict.rollback
'REASON='+$verdict.reason
'SOURCE_SHA256_AFTER='+$verdict.sourceSha256After
'EXECUTOR_EXIT='+$exit
if($verdict.status -ne 'passed'){exit 2}

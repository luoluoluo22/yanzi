#requires -version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$TabId,[int]$WaitSeconds=15)
$ErrorActionPreference='Stop'
if($TabId -le 0){throw 'Invalid TabId'}
$dir=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge'
$key=[IO.File]::ReadAllText((Join-Path $dir 'api-token.txt')).Trim()
$headers=@{Authorization=('Bearer '+$key);'X-Bridge-Request'='1'}
$root='http://127.0.0.1:53921'
$payload=@{action='chatgpt_diagnostics';tabId=$TabId;timeoutSeconds=18}|ConvertTo-Json -Compress
$queued=Invoke-RestMethod ($root+'/api/jobs') -Headers $headers -Method POST -ContentType 'application/json' -Body $payload -TimeoutSec 5
$deadline=(Get-Date).AddSeconds($WaitSeconds)
do {
  $job=Invoke-RestMethod ($root+'/api/jobs/'+$queued.id) -Headers $headers -TimeoutSec 5
  if($job.status -notin @('running','queued')){break}
  Start-Sleep -Milliseconds 250
} while((Get-Date) -lt $deadline)
$summary=[ordered]@{
  jobId=$queued.id
  status=$job.status
  pageError=$job.data.error
  tabStatus=$job.data.status
  discarded=$job.data.discarded
  managed=$job.data.managed
  readyState=$job.data.dom.readyState
  draftPresent=$job.data.dom.draftPresent
  draftLength=$job.data.dom.draftLength
  composerExists=$job.data.dom.composerExists
  sendEnabled=$job.data.dom.sendEnabled
  generating=$job.data.dom.generating
  loginRequired=$job.data.dom.loginRequired
  challenge=$job.data.dom.challenge
}
$summary|ConvertTo-Json -Compress

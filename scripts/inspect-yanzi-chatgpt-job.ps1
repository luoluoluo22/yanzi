#requires -version 5.1
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F-]{36}$')][string]$JobId
)
$ErrorActionPreference='Stop'
$tokenPath=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$token=[IO.File]::ReadAllText($tokenPath).Trim()
$job=Invoke-RestMethod ('http://127.0.0.1:53921/api/jobs/'+$JobId) -Headers @{Authorization='Bearer '+$token} -TimeoutSec 6
$observation=$job.networkObservation
$diagnosis=$job.networkDiagnosis
if(-not $diagnosis -and $job.status -in @('queued','running')) {
  $diagnosis=[pscustomobject]@{status='in_progress';next='wait';reason='task_not_terminal'}
}
[pscustomobject]@{
  jobId=$job.id
  jobStatus=$job.status
  networkStage=$observation.stage
  sampleCount=$observation.samples
  networkStarted=$observation.latest.started
  conversationApiClassCount=$observation.latest.classes.conversation_api
  networkCompleted=$observation.latest.completed
  networkFailed=$observation.latest.failed
  diagnosis=$diagnosis.status
  nextAction=$diagnosis.next
  reason=$diagnosis.reason
  note='Network observation does not prove specific prompt acceptance. Never auto-resubmit uncertain jobs.'
}|ConvertTo-Json -Depth 5

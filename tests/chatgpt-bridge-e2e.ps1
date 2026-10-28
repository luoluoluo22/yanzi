#requires -version 5.1
param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root='http://127.0.0.1:53921'
$tokenPath=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$token=[IO.File]::ReadAllText($tokenPath).Trim()
$headers=@{Authorization=('Bearer '+$token);'X-Bridge-Request'='1'}
function Poll($id,[int]$seconds) {
  $deadline=(Get-Date).AddSeconds($seconds)
  do {
    $result=Invoke-RestMethod ($root+'/api/jobs/'+[uri]::EscapeDataString($id)) -Headers $headers -TimeoutSec 5
    if($result.status -notin @('queued','running','submitting','checking')) { return $result }
    Start-Sleep -Milliseconds 700
  }while((Get-Date) -lt $deadline)
  throw ('probe_poll_timeout Job='+$id)
}
$health=Invoke-RestMethod ($root+'/health') -TimeoutSec 5
'HEALTH connected='+[bool]$health.connected+' version='+$health.extensionVersion+' active='+$health.concurrency.active
if(-not $health.connected) {throw 'GPT extension disconnected'}
$statusPayload=@{action='chatgpt_status';timeoutSeconds=20} | ConvertTo-Json -Compress
$statusQueued=Invoke-RestMethod ($root+'/api/jobs') -Headers $headers -Method POST -ContentType 'application/json' -Body $statusPayload -TimeoutSec 5
$status=Poll $statusQueued.id 30
'STATUS_JOB id='+$statusQueued.id+' status='+$status.status
if($status.status -ne 'success') {throw ('status_failed '+$status.message)}
'STATUS_TABS='+@($status.data.tabs).Count+' active='+$status.data.concurrency.active
if(-not $Live){return}
$nonce='YANZI_GPT_BRIDGE_E2E_OK_20261020'
$send=@{action='chatgpt_send';prompt=('Reply with this exact token only: '+$nonce);temporary=$true;tabPolicy='new';closeAfter=$true;timeoutSeconds=0}|ConvertTo-Json -Compress
$payload=[Text.Encoding]::UTF8.GetBytes($send)
$queued=Invoke-RestMethod ($root+'/api/jobs') -Headers $headers -Method POST -ContentType 'application/json; charset=utf-8' -Body $payload -TimeoutSec 6
'LIVE_SUBMITTED id='+$queued.id+' status='+$queued.status
$job=Poll $queued.id 330
'LIVE_RESULT id='+$queued.id+' status='+$job.status
'LIVE_DIAG='+$job.networkDiagnosis.status
'LIVE_PAGE_META='+($job.data.diagnostics | Select-Object url,conversationId,temporary,temporaryEvidence,automationRevision | ConvertTo-Json -Compress)
$reply=[string]$job.data.text
if(!$reply){$reply=[string]$job.data.markdown}
'LIVE_REPLY_MATCH='+[bool]($reply.Contains($nonce))+' length='+$reply.Length
'LIVE_TEMPORARY='+[bool]$job.data.temporary+' evidence='+$job.data.temporaryEvidence
'LIVE_ERROR='+$job.message
if($job.status -ne 'success' -or -not $reply.Contains($nonce) -or $job.data.temporary -ne $true){throw ('live_not_passed job='+$queued.id)}
'LIVE_PASS=true'

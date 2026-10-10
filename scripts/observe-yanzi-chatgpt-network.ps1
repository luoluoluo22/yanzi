#requires -version 5.1
# Metadata-only ChatGPT traffic probe. Never prints tokens, URLs or message bodies.
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$TabId,
  [ValidateRange(1000,15000)][int]$DurationMs=4000,
  [switch]$PublicProbe
)
$ErrorActionPreference='Stop'
$root='http://127.0.0.1:53921'
$tokenFile=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$token=[IO.File]::ReadAllText($tokenFile).Trim()
$headers=@{Authorization='Bearer '+$token;'X-Bridge-Request'='1'}
$payload=@{
  action='chatgpt_network_probe'
  tabId=$TabId
  durationMs=$DurationMs
  publicProbe=[bool]$PublicProbe
  timeoutSeconds=30
}|ConvertTo-Json -Compress
$queued=Invoke-RestMethod ($root+'/api/jobs') -Headers $headers -Method POST -ContentType 'application/json' -Body $payload -TimeoutSec 6
$limit=(Get-Date).AddSeconds(32)
do {
  $job=Invoke-RestMethod ($root+'/api/jobs/'+$queued.id) -Headers $headers -TimeoutSec 6
  if($job.status -notin @('queued','running')){break}
  Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $limit)
if($job.status -in @('queued','running')){
  throw ('Probe still running, do not resubmit: '+$queued.id)
}
if($job.status -ne 'success'){
  throw ('Probe refused or failed: '+$job.status+' '+$job.message)
}
[pscustomobject]@{
  jobId=$job.id
  status=$job.status
  network=$job.data
}|ConvertTo-Json -Depth 8

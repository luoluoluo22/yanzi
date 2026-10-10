#requires -version 5.1
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][int]$TabId,
  [Parameter(Mandatory=$true)][string]$PromptFile,
  [switch]$Temporary,
  [ValidateRange(20,180)][int]$TimeoutSeconds=100
)
$ErrorActionPreference='Stop'
if($TabId -lt 1){throw 'invalid_tab_id'}
$path=(Resolve-Path $PromptFile).Path
if(-not(Test-Path $path -PathType Leaf)){throw 'prompt_file_missing'}
$prompt=[IO.File]::ReadAllText($path,[Text.Encoding]::UTF8).Trim()
if(-not $prompt -or $prompt.Length -gt 12000){throw 'invalid_prompt_length'}
$tokenPath=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$token=[IO.File]::ReadAllText($tokenPath).Trim()
$headers=@{Authorization='Bearer '+$token;'X-Bridge-Request'='1'}
$base='http://127.0.0.1:53921'
$probePayload=@{action='chatgpt_diagnostics';tabId=$TabId;timeoutSeconds=18}|ConvertTo-Json -Compress
$probe=Invoke-RestMethod ($base+'/api/jobs') -Headers $headers -Method POST -Body $probePayload -ContentType 'application/json' -TimeoutSec 6
$deadline=(Get-Date).AddSeconds(20)
do {
  $status=Invoke-RestMethod ($base+'/api/jobs/'+$probe.id) -Headers $headers -TimeoutSec 5
  if($status.status -notin @('queued','running')){break}
  Start-Sleep -Milliseconds 200
}while((Get-Date) -lt $deadline)
if($status.status -ne 'success' -or $status.data.error -or
   -not $status.data.dom.composerExists -or $status.data.dom.draftPresent -or
   $status.data.dom.generating -or $status.data.dom.loginRequired -or
   $status.data.dom.challenge){throw ('page_not_safe_to_send: '+($status.data|ConvertTo-Json -Compress -Depth 4))}
$payload=@{action='chatgpt_send';tabId=$TabId;newChat=$false;temporary=[bool]$Temporary;
   closeAfter=$false;timeoutSeconds=$TimeoutSeconds;prompt=$prompt}|ConvertTo-Json -Compress -Depth 5
$queued=Invoke-RestMethod ($base+'/api/jobs') -Headers $headers -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType 'application/json; charset=utf-8' -TimeoutSec 6
'SUBMITTED_JOB='+$queued.id
'SUBMISSION_STATUS='+$queued.status

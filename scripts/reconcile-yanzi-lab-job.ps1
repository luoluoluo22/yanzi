#requires -version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$JobId,
    [Parameter(Mandatory=$true)][int]$TabId,
    [Parameter(Mandatory=$true)][ValidateSet('title','duration','tags')][string]$Task
)
$ErrorActionPreference='Stop'
if($JobId -notmatch '^[0-9a-fA-F-]{36}$' -or $TabId -lt 1){throw 'Invalid job or tab'}
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lab=Join-Path $repo '.tmp\yanzi-iteration-lab'
$secretFile=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
$key=[IO.File]::ReadAllText($secretFile).Trim()
$headers=@{Authorization='Bearer '+$key;'X-Bridge-Request'='1'}
$base='http://127.0.0.1:53921'
function Wait-BridgeJob([string]$Id){
    $limit=(Get-Date).AddSeconds(24)
    do{
        $state=Invoke-RestMethod ($base+'/api/jobs/'+$Id) -Headers $headers -TimeoutSec 6
        if($state.status -notin @('running','queued')){return $state}
        Start-Sleep -Milliseconds 250
    }while((Get-Date) -lt $limit)
    throw 'Read-only Bridge request timed out'
}
$original=Wait-BridgeJob $JobId
if($original.status -notin @('error','timeout','interrupted')){
    throw ('Reconciliation is for uncertain jobs only. Status: '+$original.status)
}
if($original.task.tabId -ne $TabId){throw 'Job tab does not match requested tab'}
$shaMatch=[regex]::Match([string]$original.task.prompt,'(?i)\b[a-f0-9]{64}\b')
if(-not $shaMatch.Success){throw 'Original job did not bind a source hash'}
$expected=$shaMatch.Value.ToUpperInvariant()
if(-not ([string]$original.task.prompt).Contains($Task)){throw 'Task label missing from original job'}
$diagnose=Invoke-RestMethod ($base+'/api/jobs') -Method POST -Headers $headers -ContentType 'application/json' -Body (@{action='chatgpt_diagnostics';tabId=$TabId;timeoutSeconds=18}|ConvertTo-Json -Compress) -TimeoutSec 6
$probe=Wait-BridgeJob $diagnose.id
if($probe.status -ne 'success' -or $probe.data.error -or $probe.data.dom.generating -or
   $probe.data.dom.challenge -or $probe.data.dom.loginRequired){
    throw 'Page not safely inspectable or still generating'
}
$read=Invoke-RestMethod ($base+'/api/jobs') -Method POST -Headers $headers -ContentType 'application/json' -Body (@{action='chatgpt_messages';tabId=$TabId;timeoutSeconds=18}|ConvertTo-Json -Compress) -TimeoutSec 6
$messagesJob=Wait-BridgeJob $read.id
if($messagesJob.status -ne 'success'){throw 'Unable to read conversation'}
$messages=@($messagesJob.data.messages)
if($messages.Count -lt 2 -or $messages[-2].role -ne 'user' -or $messages[-1].role -ne 'assistant'){
    throw 'No final user/assistant message pair'
}
$userMessage=[string]$messages[-2].text
if(-not $userMessage.Contains($expected) -or -not $userMessage.Contains($Task)){
    throw 'Final user message does not match the original task and source hash'
}
$reply=[string]$messages[-1].text
if(-not $reply -or $reply.Length -gt 4000){throw 'Missing bounded model reply'}
$proposal=$reply|ConvertFrom-Json
if($proposal.task -ne $Task -or $proposal.expectedSha256 -ne $expected){
    throw 'Model reply source identity mismatch'
}
$sourceFile=Join-Path $lab ($Task+'.mjs')
$current=(Get-FileHash $sourceFile).Hash
$record=[ordered]@{jobId=$JobId;tabId=$TabId;task=$Task;sourceExpectedSha256=$expected;
    recoveredUtc=[DateTimeOffset]::UtcNow.ToString('o');originalJobStatus=$original.status;
    messagePairVerified=$true;result=$null}
$results=Join-Path $lab 'results'
New-Item -Path $results -ItemType Directory -Force|Out-Null
$existingFile=Join-Path $results ($Task+'-latest.json')
if($current -ne $expected){
    if(-not(Test-Path $existingFile)){throw 'Source already changed without verification record'}
    $existing=Get-Content $existingFile -Raw -Encoding UTF8|ConvertFrom-Json
    if($existing.status -ne 'passed' -or $existing.sourceSha256Before -ne $expected -or
       $existing.sourceSha256After -ne $current -or $existing.testsUnchanged -ne $true){
        throw 'Existing changed source is not proven by a matching verifier record'
    }
    $record.result=[ordered]@{status='already_verified';sourceSha256After=$current}
}else{
    $inputFile=Join-Path $lab ($Task+'-recovered-'+$JobId+'.json')
    [IO.File]::WriteAllText($inputFile,$reply,[Text.UTF8Encoding]::new($false))
    $out=& node (Join-Path $repo 'tools\chatgpt-bridge\lab-patch-executor.mjs') --input $inputFile
    $verified=$out|ConvertFrom-Json
    $record.result=$verified
}
$reportFile=Join-Path $results ('reconciled-'+$JobId+'.json')
[IO.File]::WriteAllText($reportFile,($record|ConvertTo-Json -Depth 9),[Text.UTF8Encoding]::new($false))
'RECONCILIATION='+$record.result.status
'ORIGINAL_JOB_STATUS='+$record.originalJobStatus
'FINAL_SOURCE_HASH='+$record.result.sourceSha256After
'PROOF='+$reportFile
if($record.result.status -notin @('passed','already_verified')){exit 2}

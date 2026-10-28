#requires -version 5.1
<#
Dedicated ChatGPT tab per task; persisted conversation identity per child agent.
Use a stable RequestKey when starting or continuing a task; retries are idempotent.
Requires the running local Yanzi ChatGPT bridge.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('start','continue','status','list','return','events','wait','file')]
    [string]$Action,
    [string]$SubagentId = '',
    [string]$Title = '',
    [string]$Prompt = '',
    [string]$RequestKey = '',
    [string]$ParentOriginId = '',
    [string]$ParentTaskId = '',
    [long]$AfterSeq = 0,
    [ValidateRange(0,25000)][int]$WaitMs = 25000
)
$ErrorActionPreference='Stop'
$api = 'http://127.0.0.1:53921'
$tokenFile=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
if(-not(Test-Path $tokenFile)){throw 'ChatGPT bridge is not installed or not configured.'}
$token=[IO.File]::ReadAllText($tokenFile).Trim()
$headers=@{Authorization='Bearer '+$token;'X-Bridge-Request'='1'}
$uri='/api/subagents'
if($Action -in @('events','wait','file')){
    if($ParentTaskId -notmatch '^[A-Za-z0-9_.:-]{5,100}$'){throw 'A valid -ParentTaskId is required'}
    $escaped=[uri]::EscapeDataString($ParentTaskId)
    if($Action -eq 'file'){
        $file=Join-Path $env:LOCALAPPDATA ('OpenQuickHost\ExtensionStorage\chatgpt-bridge\parent-feedback\'+$escaped+'.json')
        if(-not(Test-Path $file)){throw ('No parent snapshot yet: '+$file)}
        Get-Content $file -Raw -Encoding UTF8
        exit 0
    }
    if($AfterSeq -lt 0){throw 'AfterSeq must be nonnegative'}
    $uri='/api/parent-tasks/'+$escaped+'/events?after='+$AfterSeq+'&waitMs='+$(if($Action -eq 'wait'){$WaitMs}else{0})
}
$payload=$null
$method='GET'
if($Action -in @('continue','status','return')){
    if($SubagentId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'A valid -SubagentId is required.'}
    $uri+='/'+$SubagentId
}
if($Action -in @('start','continue')){
    if([string]::IsNullOrWhiteSpace($Prompt)){throw '-Prompt is required'}
    if($RequestKey -notmatch '^[A-Za-z0-9_.:-]{5,128}$') {
        throw 'Supply a stable -RequestKey (5-128 simple characters) for deduplication.'
    }
    $method='POST'
    $payload=@{prompt=$Prompt;requestKey=$RequestKey}
    if($Action -eq 'start') {
        if(-not [string]::IsNullOrWhiteSpace($Title)){$payload.title=$Title}
        if(-not [string]::IsNullOrWhiteSpace($ParentTaskId)){
            if($ParentTaskId -notmatch '^[A-Za-z0-9_.:-]{5,100}$'){throw 'Invalid -ParentTaskId'}
            $payload.parentTaskId=$ParentTaskId
        }
        if(-not [string]::IsNullOrWhiteSpace($ParentOriginId)){
            if($ParentOriginId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Invalid -ParentOriginId'}
            $payload.parentOriginId=$ParentOriginId
        }
    } else {
        $uri+='/messages'
    }
}
if($Action -eq 'return'){
    $method='POST'
    $uri+='/return'
    $payload=@{}
}
$parameters=@{Uri=$api+$uri;Method=$method;Headers=$headers;TimeoutSec=15}
if($method -eq 'POST'){
    $parameters.ContentType='application/json; charset=utf-8'
    $parameters.Body=[Text.Encoding]::UTF8.GetBytes(($payload|ConvertTo-Json -Compress -Depth 5))
}
$result=Invoke-RestMethod @parameters
$result|ConvertTo-Json -Depth 8

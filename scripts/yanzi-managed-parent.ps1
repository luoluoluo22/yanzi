#requires -version 5.1
<#
Persistent ChatGPT managed parent conversation.
Examples:
  -Action create -Title '验收父Agent' -RequestKey 'parent:ui-001' -Prompt '你是任务协调者'
  -Action status -ParentId <UUID>
  -Action list
  -Action child -ParentId <UUID> -RequestKey 'child:ui-001' -Prompt '验证截图 UI'
  -Action events -ParentId <UUID> -AfterSeq 0
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)]
  [ValidateSet('create','status','list','child','events','wait')]
  [string]$Action,
  [string]$ParentId='',
  [string]$Title='',
  [string]$Prompt='',
  [string]$RequestKey='',
  [long]$AfterSeq=0,
  [ValidateRange(0,25000)][int]$WaitMs=25000,
  [ValidateSet(53921,53922)][int]$BridgePort=53921
)
$ErrorActionPreference='Stop'
$root='http://127.0.0.1:'+$BridgePort
$tokenDirectory=if($BridgePort -eq 53922){'chatgpt-agent-bridge'}else{'chatgpt-bridge'}
$tokenPath=Join-Path $env:LOCALAPPDATA ('OpenQuickHost\ExtensionStorage\'+$tokenDirectory+'\api-token.txt')
if(-not(Test-Path $tokenPath)){throw 'Missing local ChatGPT bridge token'}
$headers=@{Authorization='Bearer '+[IO.File]::ReadAllText($tokenPath).Trim();'X-Bridge-Request'='1'}
$path='/api/managed-parents'
$method='GET'
$payload=$null
if($Action -in @('status','child','events','wait')){
  if($ParentId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'A valid -ParentId is required'}
}
if($Action -in @('create','child')){
  if([string]::IsNullOrWhiteSpace($Prompt)){throw '-Prompt is required'}
  if($RequestKey -notmatch '^[A-Za-z0-9_.:-]{5,128}$'){throw 'Provide a stable -RequestKey (5-128 safe characters)'}
  $method='POST'
  $payload=@{prompt=$Prompt;requestKey=$RequestKey}
  if(-not [string]::IsNullOrWhiteSpace($Title)){$payload.title=$Title}
  if($Action -eq 'child'){
    $path='/api/subagents'
    $payload.parentManagedId=$ParentId
  }
} elseif($Action -eq 'status'){$path+='/'+$ParentId}
elseif($Action -in @('events','wait')){
  $parentTaskId='managed-parent:'+$ParentId
  $after=[Math]::Max(0,$AfterSeq)
  $delay=if($Action -eq 'wait'){$WaitMs}else{0}
  $path='/api/parent-tasks/'+[uri]::EscapeDataString($parentTaskId)+'/events?after='+$after+'&waitMs='+$delay
}
$args=@{Uri=$root+$path;Headers=$headers;Method=$method;TimeoutSec=35}
if($method -eq 'POST'){
  $args.ContentType='application/json; charset=utf-8'
  $args.Body=[Text.Encoding]::UTF8.GetBytes(($payload|ConvertTo-Json -Compress -Depth 6))
}
Invoke-RestMethod @args | ConvertTo-Json -Depth 10

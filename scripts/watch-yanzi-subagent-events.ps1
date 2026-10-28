#requires -version 5.1
<#
Consume durable Yanzi ChatGPT subagent events for a parent task.
Each event is saved to a per-parent file and the consumer cursor is advanced.
No foreground browser input, no ChatGPT messages are sent by this watcher.
Examples:
  ... -ParentTaskId yanzi-quality-20261009 -MaxCompletedEvents 1
  ... -ParentTaskId yanzi-quality-20261009 -MaxCompletedEvents 0 -MaxWaitSeconds 0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ParentTaskId,
    [string]$ConsumerId = 'primary',
    [ValidateRange(0,10000)][int]$MaxCompletedEvents = 1,
    [ValidateRange(0,86400)][int]$MaxWaitSeconds = 60,
    [ValidateRange(100,25000)][int]$PollWaitMs = 5000,
    [switch]$ReplayFromStart
)
$ErrorActionPreference='Stop'
if($ParentTaskId -notmatch '^[a-zA-Z0-9_.:-]{5,100}$'){throw 'Invalid ParentTaskId'}
if($ConsumerId -notmatch '^[a-zA-Z0-9_.-]{1,60}$'){throw 'Invalid ConsumerId'}
$data=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge'
$tokenPath=Join-Path $data 'api-token.txt'
if(-not (Test-Path $tokenPath)){throw 'ChatGPT bridge token missing'}
$token=[IO.File]::ReadAllText($tokenPath).Trim()
$headers=@{Authorization='Bearer '+$token}
$folder=Join-Path $data 'parent-feedback\received'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$name=[uri]::EscapeDataString($ParentTaskId)
$cursorPath=Join-Path $folder ($name+'-'+$ConsumerId+'.cursor')
$cursor=[long]0
if(-not $ReplayFromStart -and (Test-Path $cursorPath)){
    $saved=[IO.File]::ReadAllText($cursorPath).Trim()
    if($saved -match '^[0-9]{1,16}$'){$cursor=[long]::Parse($saved)}
}
$started=(Get-Date)
$count=0
Write-Output ('SUBAGENT_WATCH_PARENT='+$ParentTaskId)
Write-Output ('SUBAGENT_WATCH_CURSOR='+$cursor)
while($MaxCompletedEvents -eq 0 -or $count -lt $MaxCompletedEvents){
    if($MaxWaitSeconds -gt 0 -and ((Get-Date)-$started).TotalSeconds -ge $MaxWaitSeconds){break}
    $path='http://127.0.0.1:53921/api/parent-tasks/'+$name+'/events?after='+$cursor+'&waitMs='+$PollWaitMs
    $response=Invoke-RestMethod -Uri $path -Headers $headers -TimeoutSec 35
    foreach($event in @($response.events)){
        if($event.seq -le $cursor){continue}
        $out=Join-Path $folder ($name+'-event-'+$event.seq+'.json')
        $json=$event|ConvertTo-Json -Depth 8
        $temp=$out+'.tmp'
        [IO.File]::WriteAllText($temp,$json,[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temp -Destination $out -Force
        $cursor=[long]$event.seq
        $tempCursor=$cursorPath+'.tmp'
        [IO.File]::WriteAllText($tempCursor,[string]$cursor,[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $tempCursor -Destination $cursorPath -Force
        Write-Output ('SUBAGENT_EVENT_SEQ='+$cursor+' KIND='+$event.kind+' CHILD='+$event.subagentId+' STATUS='+$event.status)
        Write-Output ('SUBAGENT_EVENT_FILE='+$out)
        if($event.kind -eq 'subagent_finished'){
            $count++
            Write-Output ('SUBAGENT_FINISHED_RESULT='+$event.text)
            if($MaxCompletedEvents -gt 0 -and $count -ge $MaxCompletedEvents){break}
        }
    }
}
Write-Output ('SUBAGENT_WATCH_LAST_SEQ='+$cursor)
Write-Output ('SUBAGENT_WATCH_FINISHED_COUNT='+$count)

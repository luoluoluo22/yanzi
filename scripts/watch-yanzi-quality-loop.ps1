#requires -version 5.1
<#
Optional bounded development-feedback watcher.
Watches source changes WITHOUT modifying the desktop. After changes settle,
runs quality checks and posts the report to the explicitly bound conversation.
Never enables itself at startup.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OriginId,
    [string]$TaskId = 'yanzi-ui-quality',
    [ValidateRange(1,20)][int]$MaxRounds = 5,
    [ValidateRange(5,600)][int]$PollSeconds = 20,
    [ValidateRange(15,1800)][int]$SettleSeconds = 45,
    [switch]$RunImmediately
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if($OriginId -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Specify a bound origin UUID, not a guessed tab ID'}
function SourceFingerprint {
    $paths = @('src/Yanzi.Capture', 'src/Yanzi.UiTesting', 'tools/yanzi-ui-test-samples',
      'tools/yanzi-ui-test-runner', 'tools/chatgpt-bridge', 'browser-extension')
    $seen = New-Object System.Collections.Generic.List[string]
    foreach($item in $paths) {
        $dir=Join-Path $repo $item
        if(-not(Test-Path $dir)){continue}
        Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue |
          Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.git|dist|results)\\'
            -and $_.Extension -in @('.cs','.js','.mjs','.json','.ps1','.csproj') } |
          ForEach-Object {
            $seen.Add(($_.FullName.Substring($repo.Length) + '|' + $_.Length + '|' + $_.LastWriteTimeUtc.Ticks))
          }
    }
    $data=[Text.Encoding]::UTF8.GetBytes((($seen | Sort-Object) -join [Environment]::NewLine))
    $sha=[Security.Cryptography.SHA256]::Create()
    try {return ([BitConverter]::ToString($sha.ComputeHash($data))).Replace('-','')}
    finally {$sha.Dispose()}
}
$last=SourceFingerprint
$lastChange=Get-Date
$pending=[bool]$RunImmediately
$nextRound=1
Write-Output 'QUALITY_WATCH_STARTED=true'
Write-Output ('QUALITY_WATCH_TASK='+$TaskId)
Write-Output ('QUALITY_WATCH_ORIGIN='+$OriginId)
Write-Output ('QUALITY_WATCH_MAX_ROUNDS='+$MaxRounds)
while($nextRound -le $MaxRounds) {
    if(-not $RunImmediately) {Start-Sleep -Seconds $PollSeconds}
    $current=SourceFingerprint
    if($current -ne $last) {
        $last=$current
        $lastChange=Get-Date
        $pending=$true
        Write-Output ('QUALITY_WATCH_SOURCE_CHANGED='+$lastChange.ToString('o'))
    }
    if(-not $RunImmediately -and ((Get-Date)-$lastChange).TotalSeconds -lt $SettleSeconds){continue}
    if(-not $pending){continue}
    # One round per stable fingerprint; avoid repeated runs on unchanged code.
    $pending=$false
    $RunImmediately=$false
    Write-Output ('QUALITY_WATCH_RUNNING_ROUND='+$nextRound)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-yanzi-quality-loop.ps1') -TaskId $TaskId -Round $nextRound -OriginId $OriginId
    Write-Output ('QUALITY_WATCH_ROUND_EXIT='+$LASTEXITCODE)
    $nextRound++
    $last=SourceFingerprint
    $lastChange=Get-Date
    $pending=$false
}
Write-Output 'QUALITY_WATCH_FINISHED_MAX_ROUNDS=true'

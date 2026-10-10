#requires -version 5.1
<#
Safe rollout: never interrupts a running ChatGPT job. Backs up the bridge
service and its state, then restarts the local Node bridge. Reloads the
already-unpacked browser extension from the repository source.
#>
[CmdletBinding()]
param([ValidateRange(0,300)][int]$WaitSeconds=0,[switch]$SkipBrowserReload)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$base='http://127.0.0.1:53921'
$install=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\ext_e056b6373b8a45c4978a2db049467fe5'
$data=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge'
$source=Join-Path $repo 'tools\chatgpt-bridge'
$needed=@('server.mjs','origin-routing.mjs','subagent-routing.mjs','network-evidence.mjs')
foreach($file in $needed){if(-not(Test-Path (Join-Path $source $file))){throw ('Source file missing: '+$file)}}
$deadline=(Get-Date).AddSeconds($WaitSeconds)
do {
    $health=Invoke-RestMethod ($base+'/health') -TimeoutSec 4
    if($health.service -ne 'yanzi-chatgpt-bridge'){throw 'Bridge service identity mismatch'}
    if($health.concurrency.active -eq 0 -and $health.concurrency.queued -eq 0){break}
    if((Get-Date) -ge $deadline){
        Write-Output ('DEPLOY_DEFERRED_ACTIVE_JOBS='+$health.concurrency.active)
        exit 2
    }
    Start-Sleep -Seconds 2
}while($true)
$listener=Get-NetTCPConnection -State Listen -LocalPort 53921 | Select-Object -First 1
if(-not $listener){throw 'Bridge listener not found'}
$process=Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
if($process.Name -ne 'node.exe' -or $process.CommandLine -notlike ('*'+$install+'*server.mjs*')){
    throw 'Port 53921 listener is not the installed Yanzi ChatGPT bridge'
}
$backup=Join-Path $data ('backup-origin-routing-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item $backup -ItemType Directory -Force|Out-Null
foreach($file in $needed){
    $prior=Join-Path $install $file
    if(Test-Path $prior){Copy-Item $prior (Join-Path $backup $file) -Force}
}
Copy-Item (Join-Path $data 'state.json') (Join-Path $backup 'state.json') -Force
$server=Join-Path $install 'server.mjs'
$node=(Get-Command node -ErrorAction Stop).Source
function Start-BridgeService {
    $proc=Start-Process -FilePath $node -ArgumentList ('"'+$server+'"') -WorkingDirectory $install -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $data 'service.stdout.log') -RedirectStandardError (Join-Path $data 'service.stderr.log')
    for($i=0;$i -lt 55;$i++){
        Start-Sleep -Milliseconds 250
        try { $check=Invoke-RestMethod ($base+'/health') -TimeoutSec 1
            if($check.ok){return $proc}
        }catch{}
    }
    throw 'Bridge failed to start after deployment'
}
Stop-Process -Id $listener.OwningProcess -Force
for($i=0;$i -lt 30;$i++){
    if(-not (Get-NetTCPConnection -LocalPort 53921 -State Listen -ErrorAction SilentlyContinue)){break}
    Start-Sleep -Milliseconds 150
}
try {
    foreach($file in $needed){Copy-Item (Join-Path $source $file) (Join-Path $install $file) -Force}
    $nodeProc=Start-BridgeService
    $token=[IO.File]::ReadAllText((Join-Path $data 'api-token.txt')).Trim()
    $origins=Invoke-RestMethod ($base+'/api/origins') -Headers @{Authorization='Bearer '+$token} -TimeoutSec 6
    $health=Invoke-RestMethod ($base+'/health') -TimeoutSec 6
    Write-Output ('DEPLOY_BRIDGE_OK=true PID='+$nodeProc.Id+' CONNECTED='+$health.connected)
    Write-Output ('DEPLOY_BACKUP='+$backup)
    Write-Output ('DEPLOY_ORIGIN_COUNT='+@($origins).Count)
} catch {
    $failed=$_.Exception.Message
    Write-Warning ('New bridge failed, restoring backup: '+$failed)
    $l=Get-NetTCPConnection -LocalPort 53921 -State Listen -ErrorAction SilentlyContinue|Select-Object -First 1
    if($l){Stop-Process -Id $l.OwningProcess -Force}
    Start-Sleep -Milliseconds 500
    foreach($file in $needed){$prior=Join-Path $backup $file; if(Test-Path $prior){Copy-Item $prior (Join-Path $install $file) -Force}else{Remove-Item (Join-Path $install $file) -Force -ErrorAction SilentlyContinue}}
    $null=Start-BridgeService
    throw ('Deployment rolled back: '+$failed)
}
if(-not $SkipBrowserReload) {
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts\dev-browser-extension-refresh.ps1') -WaitSeconds 25
        if($LASTEXITCODE -ne 0){throw 'Browser extension reload script failed'}
        $after=Invoke-RestMethod ($base+'/health') -TimeoutSec 4
        Write-Output ('DEPLOY_EXTENSION_VERSION='+$after.extensionVersion)
        if($after.extensionVersion -ne '0.5.57') {
            Write-Warning 'Extension did not load version 0.5.57; source may not be the unpacked install path.'
        }
    } catch {Write-Warning ('Extension reload needs follow-up: '+$_.Exception.Message)}
}

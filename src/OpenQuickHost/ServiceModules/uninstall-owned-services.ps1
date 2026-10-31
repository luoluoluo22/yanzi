param(
    [Parameter(Mandatory=$true)][string]$ManifestDir,
    [Parameter(Mandatory=$true)][string]$LedgerPath,
    [switch]$DryRun
)
# Runs only for a full Velopack uninstall. Never called on update or GUI exit.
# No computer-wide service discovery: only this install's ownership ledger + trusted descriptors.
$ErrorActionPreference='Stop'
$owner='yanzi-on-demand-service-v1'
$rootData=Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$logDir=Join-Path $rootData 'ServiceUninstall'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log=Join-Path $logDir 'latest.json'
$results=@()
$failures=0

function Result([string]$Id,[string]$Status,[string]$Detail){
    $script:results+=@{id=$Id;status=$Status;detail=$Detail;at=[DateTimeOffset]::UtcNow.ToString('o')}
}
function IsSafeChild([string]$Path,[string]$Base){
    $full=[IO.Path]::GetFullPath($Path)
    $parent=[IO.Path]::GetFullPath($Base).TrimEnd('\')+'\'
    if(-not $full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)){return $false}
    if(Test-Path -LiteralPath $full){
        if(([IO.File]::GetAttributes($full) -band [IO.FileAttributes]::ReparsePoint) -ne 0){return $false}
    }
    return $true
}
function SaveLedger([object[]]$Entries){
    $dir=Split-Path -Parent $LedgerPath
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $tmp=$LedgerPath+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        [IO.File]::WriteAllText($tmp, (ConvertTo-Json -InputObject @($Entries) -Depth 9),
            [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $tmp -Destination $LedgerPath -Force
    } finally {
        if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp -Force}
    }
}
if(-not (Test-Path -LiteralPath $LedgerPath)){ Result 'ledger' 'none' 'no managed installs' }
else {
    $entries=@(Get-Content -LiteralPath $LedgerPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $remaining=@()
    foreach($item in $entries){
        $id=[string]$item.id
        if($item.owner -ne $owner){$remaining+=,$item;continue}
        try {
            if($id -notmatch '^[a-z0-9][a-z0-9-]{2,63}$'){throw 'invalid_id'}
            if($item.state -notin @('installing','installed')){throw 'invalid_lifecycle_state'}
            $descriptorFile=Join-Path $ManifestDir ($id+'.json')
            if(-not (Test-Path -LiteralPath $descriptorFile)){throw 'trusted_manifest_missing'}
            $module=Get-Content -LiteralPath $descriptorFile -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach($field in @('repository','localDirectory','managedTaskName','managedEntryPoint')){
                if([string]$item.$field -cne [string]$module.$field){throw ('manifest_mismatch_'+$field)}
            }
            if($module.id -ne $id){throw 'manifest_id_mismatch'}
            if($module.localDirectory -notmatch '^[A-Za-z][A-Za-z0-9-]{2,80}$' -or
               $module.managedTaskName -notmatch '^[A-Za-z][A-Za-z0-9-]{2,100}$' -or
               $module.managedEntryPoint -notmatch '^app/[A-Za-z0-9/.-]+\.(js|mjs|exe)$' -or
               @($module.managedEntryPoint.Split('/')) -contains '..'){throw 'unsafe_manifest'}
            $dir=Join-Path $env:LOCALAPPDATA $module.localDirectory
            $entry=Join-Path $dir ($module.managedEntryPoint.Replace('/','\'))
            if(-not (IsSafeChild $entry $dir)){throw 'unsafe_entrypoint'}
            if(Test-Path -LiteralPath $dir){
                if(([IO.File]::GetAttributes($dir) -band [IO.FileAttributes]::ReparsePoint) -ne 0){
                    throw 'installation_directory_is_link'
                }
            }
            $task=Get-ScheduledTask -TaskName $module.managedTaskName -ErrorAction SilentlyContinue
            if($task){
                # The task name alone is NOT proof of ownership.
                $ownedActions=@($task.Actions | Where-Object {
                    $_.Arguments -and $_.Arguments.Trim().Trim('"') -ieq $entry -and
                    $_.Execute -and $_.Execute -match '(^|[\\/])node[.]exe$'
                })
                if($ownedActions.Count -ne 1 -or @($task.Actions).Count -ne 1){
                    throw 'task_name_taken_by_unowned_process'
                }
            }
            if($DryRun) {
                Result $id 'dry-run' ('verified ownership; task='+[bool]$task)
                $remaining+=,$item
                continue
            }
            # Try to release the service-managed EasyTier tunnel before stopping Node.
            # Failure here must not result in removal of any foreign tunnel or executable.
            if($module.managedStopRoute -and $module.managedStopRoute -match '^/v1/[A-Za-z0-9/-]{1,100}$' -and
               (Test-Path -LiteralPath (Join-Path $dir 'config.json'))){
                try {
                    $cfg=Get-Content -LiteralPath (Join-Path $dir 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                    $tokenFile=Join-Path $dir 'control.token'
                    if((Test-Path -LiteralPath $tokenFile) -and
                       [int]$cfg.controlPort -ge 1024 -and [int]$cfg.controlPort -le 65535){
                        $token=(Get-Content -LiteralPath $tokenFile -Raw).Trim()
                        if($token -match '^[a-f0-9]{64}$'){
                            Invoke-RestMethod -Method Post -Uri ('http://127.0.0.1:'+$cfg.controlPort+[string]$module.managedStopRoute) -Headers @{'x-yanzi-service-token'=$token} -TimeoutSec 3 | Out-Null
                        }
                    }
                } catch { # Active jobs can reject graceful shutdown. The user explicitly uninstalled the app.
                }
            }
            # Match exact managed entrypoint. Do not terminate arbitrary node.exe/easytier.exe.
            $processes=@(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
                $_.CommandLine -and
                $_.CommandLine.IndexOf($entry,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and
                $_.Name -in @('node.exe','powershell.exe','YanziTransferService.exe')
            })
            if($task){Disable-ScheduledTask -TaskName $module.managedTaskName -ErrorAction Stop | Out-Null}
            # Kill the verified worker process tree before its parent disappears, so its
            # exclusively-owned EasyTier child is not left behind.
            foreach($p in $processes){
                & taskkill.exe /PID $p.ProcessId /T /F 2>$null | Out-Null
            }
            if($task){Stop-ScheduledTask -TaskName $module.managedTaskName -ErrorAction SilentlyContinue}
            if($task){Unregister-ScheduledTask -TaskName $module.managedTaskName -Confirm:$false -ErrorAction Stop}
            Start-Sleep -Milliseconds 350
            $left=@(Get-CimInstance Win32_Process | Where-Object {
                $_.CommandLine -and $_.CommandLine.IndexOf($entry,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and
                $_.Name -in @('node.exe','YanziTransferService.exe')
            })
            if($left.Count -gt 0){throw 'owned_process_still_running'}
            # Remove only binaries/checkouts and machine-local control credentials.
            # Keep data/, received files, and user-created material for recovery.
            foreach($name in @('app','repo')){
                $target=Join-Path $dir $name
                if(-not (IsSafeChild $target $dir)){throw ('unsafe_path_'+$name)}
                if(Test-Path -LiteralPath $target){
                    # Refuse to recursively delete trees containing symlinks/junctions.
                    $links=@(Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction Stop |
                        Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0})
                    if($links.Count -gt 0){throw ('linked_path_in_'+$name)}
                    Remove-Item -LiteralPath $target -Recurse -Force
                }
            }
            foreach($name in @('control.token','config.json','private-network.secret')){
                $target=Join-Path $dir $name
                if(-not (IsSafeChild $target $dir)){throw ('unsafe_path_'+$name)}
                if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
            }
            Result $id 'removed' 'scheduled task, managed process and executable files removed; user data retained'
        } catch {
            $failures++
            $remaining+=,$item
            Result $id 'failed' $_.Exception.Message
        }
    }
    if(-not $DryRun){SaveLedger -Entries $remaining}
}
$report=[ordered]@{ at=[DateTimeOffset]::UtcNow.ToString('o'); dryRun=[bool]$DryRun; failures=$failures; results=$results }
[IO.File]::WriteAllText($log, ($report|ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$report|ConvertTo-Json -Depth 8
if(-not $DryRun -and $failures -eq 0){
    # Staging copies must not linger after a successful, detached uninstall.
    Remove-Item -LiteralPath $ManifestDir -Recurse -Force -ErrorAction SilentlyContinue
}
if($failures -gt 0){exit 1}

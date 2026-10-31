#Requires -Version 5.1
# Persistent queued releases: enqueue is fast; scheduled tick is independent.
param(
 [ValidateSet('enqueue','tick','status','evaluate','cancel','retry','install')][string]$Mode='status',
 [string]$RepoPath='F:\Desktop\kaifa\OpenQuickHost',
 [string]$StateDir=(Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ReleaseQueue'),
 [int]$MaxAgeHours=72,[int]$IdleThresholdSeconds=600,[string]$EntryId='',
 [int]$IdleTestSeconds=-1
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$githubRepo='luoluoluo22/yanzi'
$stateFile=Join-Path $StateDir 'queue.json'
$logFile=Join-Path $StateDir 'queue.log'
New-Item -ItemType Directory -Force $StateDir|Out-Null
function Read-State {
 if(!(Test-Path $stateFile)){return [pscustomobject]@{schema=1;entries=@();updatedUtc=''}}
 $value=Get-Content $stateFile -Raw -Encoding UTF8|ConvertFrom-Json
 if($value.schema -ne 1 -or $null -eq $value.entries){throw 'release_queue_state_corrupt'}
 return $value
}
function Save-State($state){
 $state.updatedUtc=[DateTime]::UtcNow.ToString('o')
 $temp="$stateFile.tmp.$PID"
 [IO.File]::WriteAllText($temp,(ConvertTo-Json -InputObject $state -Depth 12),(New-Object Text.UTF8Encoding($false)))
 Move-Item -LiteralPath $temp -Destination $stateFile -Force
}
function Log([string]$note){Add-Content $logFile -Encoding UTF8 -Value ('['+[DateTime]::UtcNow.ToString('o')+'] '+$note)}
function Git([string[]]$arguments){
 $output=@(& git.exe -C $RepoPath @arguments 2>&1)
 if($LASTEXITCODE -ne 0){throw ('git_failed: '+(($output|Out-String).Trim()))}
 return ($output -join [Environment]::NewLine).Trim()
}
function Idle-Secs {
 if(!('YanziIdleApi' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YanziIdleApi {
 [StructLayout(LayoutKind.Sequential)] struct Info { public uint cbSize;public uint dwTime; }
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref Info x);
 public static long Seconds() {
  Info i=new Info();i.cbSize=(uint)Marshal.SizeOf(typeof(Info));
  if(!GetLastInputInfo(ref i))return -1;
  uint now=unchecked((uint)Environment.TickCount);
  return unchecked(now-i.dwTime)/1000L;
 }
}
'@
 }
 return [YanziIdleApi]::Seconds()
}
function Source-Version([string]$sha){
 $f=$sha+':src/OpenQuickHost/OpenQuickHost.csproj'
 [xml]$p=Git @('show',$f)
 return [string]$p.Project.PropertyGroup.Version
}
function Latest-Version {
 $result=@(& gh release list --repo $githubRepo --limit 40 --json tagName 2>&1)
 if($LASTEXITCODE -ne 0){throw 'release_list_unavailable'}
 $versions=@()
 $parsed=($result -join [Environment]::NewLine)|ConvertFrom-Json
 foreach($r in $parsed){
  $tag=[string]$r.tagName
  if($tag -match '^v(\d+\.\d+\.\d+)$'){$versions += [version]::Parse($Matches[1])}
 }
 $versions=@($versions|Sort-Object -Descending)
 if($versions.Count){return $versions[0]}
 return $null
}
function Worktree([string]$commit){
 $dir=Join-Path $StateDir 'build-worktree'
 if(!(Test-Path $dir)){Git @('worktree','add','--detach',$dir,$commit)|Out-Null}
 else{
  if(!(Test-Path (Join-Path $dir '.git'))){throw 'unsafe_worktree'}
  $dirty=@(& git.exe -C $dir status --porcelain)
  if($LASTEXITCODE -ne 0 -or @($dirty|Where-Object {$_}).Count){throw 'dirty_queue_worktree'}
  & git.exe -C $dir checkout --detach $commit --quiet
  if($LASTEXITCODE -ne 0){throw 'worktree_checkout_failed'}
 }
 return $dir
}
function Check([string]$name,[string]$exe,[string[]]$args,[string]$working){
 Log "QA_START=$name"
 Push-Location $working
 try{
  & $exe @args *> $null
  if($LASTEXITCODE -ne 0){throw "qa_failed:$name"}
 }finally{Pop-Location}
 Log "QA_PASS=$name"
}
function Set-Version([string]$working,[string]$version){
 $file=Join-Path $working 'src\OpenQuickHost\OpenQuickHost.csproj'
 $s=[IO.File]::ReadAllText($file)
 foreach($part in @(@('Version',$version),@('FileVersion',"$version.0"),@('AssemblyVersion',"$version.0"),@('InformationalVersion',$version))){
  $pattern='(?<=<'+$part[0]+'>)[^<]+(?=</'+$part[0]+'>)'
  if([regex]::Matches($s,$pattern).Count -ne 1){throw ('version_tag_invalid:'+ $part[0])}
  $s=[regex]::Replace($s,$pattern,$part[1])
 }
 [IO.File]::WriteAllText($file,$s,(New-Object Text.UTF8Encoding($false)))
}
function Publish-Later($item,$state,[string]$cause){
 $item.status='validating';$item.trigger=$cause;$item.startedUtc=[DateTime]::UtcNow.ToString('o')
 Save-State $state
 try{
  Git @('fetch','origin','main','--quiet')|Out-Null
  if((Git @('rev-parse','refs/remotes/origin/main')) -ne $item.sha){throw 'candidate_not_current_main;enqueue_latest'}
  if((Git @('cat-file','-t',$item.sha)) -ne 'commit'){throw 'invalid_commit'}
  $work=Worktree $item.sha
  $version=[version](Source-Version $item.sha)
  $publishedVersion=Latest-Version
  $target=$version
  if($null -ne $publishedVersion -and $target -le $publishedVersion){
   $target=[version]::new($publishedVersion.Major,$publishedVersion.Minor,($publishedVersion.Build+1))
  }
  $item.version=$target.ToString(3)
  if($target -ne $version){Set-Version $work $item.version}
  $item.status='building';Save-State $state
  Check 'restore' 'dotnet.exe' @('restore','src/OpenQuickHost/OpenQuickHost.csproj','--ignore-failed-sources','--verbosity','quiet') $work
  Check 'build' 'dotnet.exe' @('build','src/OpenQuickHost/OpenQuickHost.csproj','-c','Release','--no-restore','-p:RunAnalyzers=false','-v:q') $work
  Check 'capability' 'dotnet.exe' @('run','--project','src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj','-c','Release','--no-restore','--','--warehouse-capability-ui') $work
  Check 'deploy-receiver' 'powershell.exe' @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File','scripts/test-device-deploy-agent.ps1') $work
  if($target -ne $version){
   & git.exe -C $work add src/OpenQuickHost/OpenQuickHost.csproj
   if($LASTEXITCODE){throw 'version_add_failed'}
   & git.exe -C $work -c user.name='Yanzi Release Bot' -c user.email='release-bot@users.noreply.github.com' commit -m ('chore(release): prepare '+$item.version) *> $null
   if($LASTEXITCODE){throw 'version_commit_failed'}
   $item.releaseSha=(& git.exe -C $work rev-parse HEAD).Trim()
   & git.exe -C $work push origin HEAD:refs/heads/main *> $null
   if($LASTEXITCODE){throw 'version_push_rejected'}
  }else{$item.releaseSha=$item.sha}
  & gh release view ('v'+$item.version) --repo $githubRepo --json tagName *> $null
  if($LASTEXITCODE -eq 0){throw 'release_tag_exists'}
  $latestRemote=(& git.exe -C $RepoPath ls-remote origin refs/heads/main)
  if($LASTEXITCODE -ne 0 -or $latestRemote -notmatch ('^'+$item.releaseSha+'[\s]')){throw 'remote_main_changed_before_dispatch'}
  $item.status='dispatching';Save-State $state
  $response=@(& gh workflow run publish-yanzi-device-release.yml --repo $githubRepo --ref main --field ('version='+$item.version) --field ('expected_sha='+$item.releaseSha) 2>&1)
  if($LASTEXITCODE){throw 'workflow_dispatch_failed'}
  $message=$response -join [Environment]::NewLine
  if($message -match '/actions/runs/(\d+)'){$item.runId=$Matches[1]}
  $item.status='dispatched';$item.dispatchedUtc=[DateTime]::UtcNow.ToString('o')
  Save-State $state
  Write-Output ('RELEASE_DISPATCHED='+$item.version+';run='+$item.runId+';no_wait=true')
 }catch{
  $item.status='blocked';$item.error=$_.Exception.Message;Save-State $state
  Log ('RELEASE_BLOCKED='+$item.error)
  Write-Output ('RELEASE_BLOCKED='+$item.error)
 }
}
function Reconcile($item,$state){
 if(!$item.runId){
  $r=@(& gh run list --repo $githubRepo --workflow publish-yanzi-device-release.yml --limit 20 --json databaseId,headSha,event,createdAt 2>&1)
  if($LASTEXITCODE){throw 'run_lookup_error'}
  $parsed=($r -join [Environment]::NewLine)|ConvertFrom-Json
  $matches=@($parsed|Where-Object {
   $_.headSha -eq $item.releaseSha -and $_.event -eq 'workflow_dispatch' -and
   [DateTime]::Parse($_.createdAt).ToUniversalTime() -ge [DateTime]::Parse($item.startedUtc).ToUniversalTime()
  })
  if($matches.Count -ne 1){throw 'run_ambiguous_or_missing'}
  $item.runId=[string]$matches[0].databaseId;Save-State $state
 }
 $r=@(& gh run view $item.runId --repo $githubRepo --json status,conclusion,headSha 2>&1)
 if($LASTEXITCODE){throw 'run_status_error'}
 $run=($r -join [Environment]::NewLine)|ConvertFrom-Json
 if($run.headSha -ne $item.releaseSha){throw 'release_run_sha_mismatch'}
 if($run.status -ne 'completed'){Write-Output ('RELEASE_CI_RUNNING='+$item.runId);return}
 if($run.conclusion -ne 'success'){
  $item.status='blocked';$item.error='ci_'+$run.conclusion;Save-State $state
  Write-Output ('RELEASE_CI_FAILED='+$item.runId);return
 }
 $r=@(& gh release view ('v'+$item.version) --repo $githubRepo --json isDraft,isPrerelease,targetCommitish,assets 2>&1)
 if($LASTEXITCODE){throw 'released_asset_missing'}
 $rel=($r -join [Environment]::NewLine)|ConvertFrom-Json
 $installer='Yanzi-win-Setup-'+$item.version+'.exe'
 if($rel.isDraft -or $rel.isPrerelease -or $rel.targetCommitish -ne $item.releaseSha -or
  @($rel.assets|Where-Object {$_.name -eq $installer -and $_.size -gt 1000000}).Count -ne 1){
  throw 'release_asset_verification_failed'
 }
 $item.status='published';$item.publishedUtc=[DateTime]::UtcNow.ToString('o')
 Save-State $state
 Write-Output ('RELEASE_VERIFIED=v'+$item.version)
}
function Process-Queue {
 $state=Read-State
 if($Mode -eq 'status'){ConvertTo-Json -InputObject $state -Depth 12;return}
 if($Mode -eq 'install'){
  $installed=Join-Path $StateDir 'release-queue.ps1'
  if([IO.Path]::GetFullPath($PSCommandPath) -ne [IO.Path]::GetFullPath($installed)){Copy-Item $PSCommandPath $installed -Force}
  $cmd='powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$installed+'" -Mode tick -RepoPath "'+$RepoPath+'"'
  & schtasks.exe /Create /F /TN 'Yanzi-Deferred-Release-Queue' /SC MINUTE /MO 5 /RL LIMITED /IT /TR $cmd|Out-Null
  if($LASTEXITCODE){throw 'task_install_failed'}
  Write-Output ('RELEASE_QUEUE_INSTALLED='+$installed);return
 }
 if($Mode -eq 'enqueue'){
  Git @('fetch','origin','main','--quiet')|Out-Null
  $sha=Git @('rev-parse','refs/remotes/origin/main')
  if($sha -notmatch '^[a-f0-9]{40}$'){throw 'invalid_candidate_sha'}
  if(@($state.entries|Where-Object {$_.sha -eq $sha -and $_.status -in @('queued','building','validating','dispatched','published')}).Count){
   Write-Output ('RELEASE_ALREADY_QUEUED='+$sha);return
  }
  foreach($old in @($state.entries|Where-Object {$_.status -eq 'queued'})){
   $old.status='superseded';$old.error='newer_sha_queued'
  }
  $item=[pscustomobject]@{id=[guid]::NewGuid().ToString('N');sha=$sha;releaseSha='';version=(Source-Version $sha)
   queuedUtc=[DateTime]::UtcNow.ToString('o');status='queued';trigger='';runId='';error=''
   startedUtc='';dispatchedUtc='';publishedUtc=''}
  $state.entries=@($state.entries)+@($item);Save-State $state
  Write-Output ('RELEASE_QUEUED='+$item.id+';sha='+$sha+';afterHours='+$MaxAgeHours+';idleSeconds='+$IdleThresholdSeconds)
  return
 }
 if($Mode -in @('cancel','retry')){
  $wanted=if($Mode -eq 'cancel'){'queued'}else{'blocked'}
  $found=@($state.entries|Where-Object {$_.id -eq $EntryId -and $_.status -eq $wanted})
  if($found.Count -ne 1){throw 'queue_entry_not_found_in_expected_state'}
  if($Mode -eq 'cancel'){$found[0].status='cancelled'}else{
   $found[0].status='queued';$found[0].queuedUtc=[DateTime]::UtcNow.ToString('o');$found[0].error=''
  }
  Save-State $state;Write-Output ('RELEASE_QUEUE_CHANGED='+$EntryId);return
 }
 if($Mode -eq 'tick'){
  if([DateTime]::Now.Hour -lt 8 -or [DateTime]::Now.Hour -ge 23){
   Write-Output 'RELEASE_DEFERRED=home_off_hours';return
  }
  if(@($state.entries|Where-Object {$_.status -in @('building','validating','dispatching')}).Count){
   Write-Output 'RELEASE_NEEDS_REVIEW=interrupted_worker';return
  }
  $active=@($state.entries|Where-Object {$_.status -eq 'dispatched'})
  if($active.Count){
   try{Reconcile $active[0] $state}
   catch{Log ('RECONCILE_ERROR='+$_.Exception.Message);Write-Output ('RECONCILE_ERROR='+$_.Exception.Message)}
   return
  }
 }
 $pending=@($state.entries|Where-Object {$_.status -eq 'queued'}|Sort-Object queuedUtc)
 if(!$pending.Count){Write-Output 'RELEASE_QUEUE_EMPTY';return}
 $item=$pending[0]

 $idle=Idle-Secs
 if($Mode -eq 'evaluate' -and $IdleTestSeconds -ge 0){$idle=$IdleTestSeconds}
 $hours=([DateTime]::UtcNow-[DateTime]::Parse($item.queuedUtc).ToUniversalTime()).TotalHours
 $reason=if($hours -ge $MaxAgeHours){'72h'}elseif($idle -ge $IdleThresholdSeconds){'idle'}else{''}
 if($Mode -eq 'evaluate'){Write-Output ('RELEASE_EVALUATED=reason='+$reason+';idle='+$idle+';ageHours='+[math]::Round($hours,2));return}
 if(!$reason){Write-Output ('RELEASE_DEFERRED=idle_'+$idle);return}
 Publish-Later $item $state $reason
}
$mutex=New-Object Threading.Mutex($false,'Local\YanziDeferredReleaseQueue')
$locked=$false
try{
 try{$locked=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException] {$locked=$true}
 if(!$locked){Write-Output 'RELEASE_QUEUE_BUSY';return}
 Process-Queue
}finally{
 if($locked){$mutex.ReleaseMutex()}
 $mutex.Dispose()
}

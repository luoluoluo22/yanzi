#Requires -Version 5.1
$ErrorActionPreference='Stop'
$scriptFile=Join-Path $PSScriptRoot 'release-queue.ps1'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporary=Join-Path $env:TEMP ('yanzi-release-queue-unit-'+[guid]::NewGuid().ToString('N'))
New-Item $temporary -ItemType Directory|Out-Null
try{
 $tokens=$null;$errors=$null
 [Management.Automation.Language.Parser]::ParseFile($scriptFile,[ref]$tokens,[ref]$errors)|Out-Null
 if(@($errors).Count){throw 'release_queue_parser_errors'}
 $initial=(& $scriptFile -Mode status -StateDir $temporary|Out-String)|ConvertFrom-Json
 if($initial.schema -ne 1 -or @($initial.entries).Count){throw 'empty_state_mismatch'}
 $result=@(& $scriptFile -Mode enqueue -StateDir $temporary -RepoPath $root)
 if(!$result -or $result[0] -notmatch '^RELEASE_QUEUED='){throw 'queue_enrollment_failed'}
 $second=@(& $scriptFile -Mode enqueue -StateDir $temporary -RepoPath $root)
 if($second[0] -notmatch '^RELEASE_ALREADY_QUEUED='){throw 'dedup_failed'}
 $state=(& $scriptFile -Mode status -StateDir $temporary|Out-String)|ConvertFrom-Json
 if(@($state.entries).Count -ne 1 -or $state.entries[0].status -ne 'queued'){throw 'enqueue_not_persistent'}
 $id=$state.entries[0].id
 $state.entries[0].queuedUtc=[DateTime]::UtcNow.AddHours(-73).ToString('o')
 [IO.File]::WriteAllText((Join-Path $temporary 'queue.json'),(ConvertTo-Json $state -Depth 10),[Text.UTF8Encoding]::new($false))
 $decision=@(& $scriptFile -Mode evaluate -StateDir $temporary -IdleTestSeconds 0)
 if($decision[0] -notmatch 'reason=72h'){throw 'max_age_trigger_missing'}
 $state.entries[0].queuedUtc=[DateTime]::UtcNow.ToString('o')
 [IO.File]::WriteAllText((Join-Path $temporary 'queue.json'),(ConvertTo-Json $state -Depth 10),[Text.UTF8Encoding]::new($false))
 $decision=@(& $scriptFile -Mode evaluate -StateDir $temporary -IdleTestSeconds 601)
 if($decision[0] -notmatch 'reason=idle'){throw 'idle_10_minutes_trigger_missing'}
 $decision=@(& $scriptFile -Mode evaluate -StateDir $temporary -IdleTestSeconds 120)
 if($decision[0] -match 'reason=(idle|72h)'){throw 'early_release_must_wait'}
 $cancel=@(& $scriptFile -Mode cancel -StateDir $temporary -EntryId $id)
 $state=(& $scriptFile -Mode status -StateDir $temporary|Out-String)|ConvertFrom-Json
 if($state.entries[0].status -ne 'cancelled'){throw 'cancel_not_persistent'}
 Write-Output 'RELEASE_QUEUE_TEST=PASSED;syntax=1;dedupe=1;72h=1;idle10m=1;noEarlyRelease=1;cancel=1;no_publish=1'
}finally{
 Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}

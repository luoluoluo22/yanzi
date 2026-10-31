#Requires -Version 5.1
<#
Single-command release gate. It never silently merges dirty worktrees, changes versions,
publishes a second release for the same tag, or claims installation from CI success alone.
The separate private receipt agents verify both Windows devices before this exits 0.
#>
param(
    [string]$Version='',
    [switch]$StatusOnly,
    [switch]$SkipLocalTests,
    [switch]$DryRun,
    [string]$FixtureRoot='',
    [ValidateRange(2,180)][int]$MaxWaitMinutes=90,
    [ValidateRange(2,120)][int]$PollSeconds=15,
    [string]$ReportDirectory=(Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ReleaseOrchestrator'),
    [string]$ReceiptRepo='luoluoluo22/yanzi-deployment-receipts',
    [string[]]$ExtraTestCommands=@(),
    [switch]$RetryFailedWorkflow
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$script:publisher='luoluoluo22/yanzi'
$script:workflow='publish-yanzi-device-release.yml'
$script:deviceNames=@('PC-202409131508','CHINAMI-3IF988E')
$script:root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:deadline=[DateTime]::UtcNow.AddMinutes($MaxWaitMinutes)
$script:fixture=$FixtureRoot -ne ''
$script:report=$null

function Now-Utc {return [DateTime]::UtcNow.ToString('o')}
function Run-Checked([string]$file,[string[]]$arguments) {
    $output = @(& $file @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {throw "$file returned exit=$LASTEXITCODE"}
    return ($output -join [Environment]::NewLine)
}
function Save-Report {
    if (-not $script:report) {return}
    $script:report.updatedAtUtc=Now-Utc
    if ($DryRun) {return}
    New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
    $json=ConvertTo-Json -InputObject $script:report -Depth 9
    foreach ($dest in @($script:reportFile,(Join-Path $ReportDirectory 'latest.json'))) {
        $tmp="$dest.tmp.$PID"
        [IO.File]::WriteAllText($tmp,$json,(New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $tmp -Destination $dest -Force
    }
}
function Stage([string]$phase,[string]$message) {
    $script:report.phase=$phase
    $script:report.events+=@([ordered]@{atUtc=Now-Utc;phase=$phase;message=$message})
    if ($script:report.events.Count -gt 100) { $script:report.events=@($script:report.events|Select-Object -Last 100) }
    Write-Output "RELEASE_STAGE=$phase; $message"
    Save-Report
}
function Read-ProjectVersion {
    [xml]$project=Get-Content -LiteralPath (Join-Path $script:root 'src\OpenQuickHost\OpenQuickHost.csproj') -Encoding UTF8
    return [string]$project.Project.PropertyGroup.Version
}
function Assert-Git-Ready {
    $branch=(Run-Checked 'git' @('-C',$script:root,'branch','--show-current')).Trim()
    if ($branch -ne 'main') {throw "Release requires main, current branch=$branch"}
    $dirty=Run-Checked 'git' @('-C',$script:root,'status','--porcelain')
    if ($dirty.Trim()) {throw 'Release requires a clean working tree'}
    $head=(Run-Checked 'git' @('-C',$script:root,'rev-parse','HEAD')).Trim()
    $remote=Run-Checked 'git' @('-C',$script:root,'ls-remote','origin','refs/heads/main')
    $remoteHash=($remote -split '\s+')[0]
    if ($head -ne $remoteHash) {throw 'origin/main has not received this local commit'}
    return $head
}
function Read-Release {
    if ($script:fixture) {
        $p=Join-Path $FixtureRoot 'release.json'
        if (-not (Test-Path $p)) {return $null}
        return Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $prior=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $json=@(& gh api "repos/$script:publisher/releases/tags/v$Version" 2>$null)
    } finally { $ErrorActionPreference=$prior }
    if ($LASTEXITCODE -ne 0) {return $null}
    return ($json -join [Environment]::NewLine | ConvertFrom-Json)
}
function Assert-Release($release) {
    if (-not $release -or $release.tag_name -ne "v$Version" -or
        $release.draft -or $release.prerelease) {throw 'Release tag missing, wrong, or unpublished'}
    $name="Yanzi-win-Setup-$Version.exe"
    $assets=@($release.assets|Where-Object {$_.name -eq $name})
    if ($assets.Count -ne 1 -or [long]$assets[0].size -lt 10000000 -or
        [string]$assets[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
        throw 'Published installer missing or SHA-256 unverified'
    }
    $script:report.releaseUrl=if ($release.html_url) {[string]$release.html_url} else {
        "https://github.com/$script:publisher/releases/tag/v$Version"
    }
    $script:report.publishedAtUtc=[string]$release.published_at
    if (-not $script:report.publishedAtUtc) {throw 'Missing release publication timestamp'}
}
function Read-Workflow {
    if ($script:fixture) {
        $p=Join-Path $FixtureRoot 'workflow.json'
        if (Test-Path $p) {return Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json}
        return [pscustomobject]@{databaseId=0;status='completed';conclusion='success';headSha='fixture'}
    }
    $runs=Run-Checked 'gh' @('run','list','--repo',$script:publisher,'--workflow',$script:workflow,
        '--limit','20','--json','databaseId,event,status,conclusion,headSha,createdAt')
    $results=@($runs|ConvertFrom-Json)
    $hits=@($results|Where-Object {$_.headSha -eq $script:report.sourceCommit -and
        $_.event -eq 'workflow_dispatch'}|Sort-Object createdAt -Descending)
    if ($hits.Count -eq 0) {return $null}
    return $hits[0]
}
function Fetch-Receipt([string]$machine) {
    if ($script:fixture) {
        $p=Join-Path (Join-Path $FixtureRoot 'devices') "$machine.json"
        if (-not (Test-Path $p)) {return $null}
        return Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $endpoint="repos/$ReceiptRepo/contents/devices/$machine.json"
    $prior=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $res=@(& gh api $endpoint 2>$null)
    } finally { $ErrorActionPreference=$prior }
    if ($LASTEXITCODE -ne 0 -or @($res).Count -eq 0) {return $null}
    $obj=$res -join [Environment]::NewLine | ConvertFrom-Json
    if ($obj.encoding -ne 'base64') {throw "Unexpected receipt encoding for $machine"}
    $json=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
        ([string]$obj.content -replace '\s','')))
    return $json|ConvertFrom-Json
}
function Validate-Receipt([string]$machine,$r) {
    $out=[ordered]@{machine=$machine;result='pending';version='';state='missing';
        reportedAtUtc='';processRunning=$false;reason='No authenticated receipt'}
    if (-not $r) {return $out}
    $out.version=[string]$r.installedVersion
    $out.state=[string]$r.state
    $out.reportedAtUtc=[string]$r.checkedAtUtc
    $out.processRunning=($r.processRunning -eq $true)
    if ([string]$r.machine -cne $machine -or $r.schemaVersion -ne 1) {
        $out.result='invalid';$out.reason='Wrong device or receipt schema';return $out
    }
    try {
        $received=[DateTime]::Parse([string]$r.checkedAtUtc).ToUniversalTime()
        $published=[DateTime]::Parse([string]$script:report.publishedAtUtc).ToUniversalTime()
        if ($received -lt $published) {
            $out.reason='Receipt predates this release';return $out
        }
    } catch {
        $out.result='invalid';$out.reason='Invalid receipt timestamp';return $out
    }
    if (([DateTime]::UtcNow - [DateTime]::Parse([string]$r.checkedAtUtc).ToUniversalTime()).TotalHours -gt 12) {
        $out.reason='Receipt heartbeat is older than 12 hours';return $out
    }
    if ($r.state -eq 'failed') {
        $out.result='failed';$out.reason='Device deployment agent failed';return $out
    }
    if ($r.installedVersion -ne $Version -or $r.fileVersion -ne $Version) {
        $out.reason='Installed version has not reached target';return $out
    }
    if ($r.state -ne 'verified-running' -or -not $r.processRunning) {
        $out.reason='New version process not verified';return $out
    }
    $out.result='passed';$out.reason='Installed binary version and running process verified'
    return $out
}
function Verify-Devices {
    $all=$true
    foreach($machine in $script:deviceNames) {
        try {
            $receipt=Fetch-Receipt $machine
            $state=Validate-Receipt $machine $receipt
        } catch {
            $state=[ordered]@{machine=$machine;result='pending';version='';state='unavailable';
                reportedAtUtc='';processRunning=$false;reason='Receipt query unavailable'}
        }
        $script:report.devices[$machine]=$state
        if ($state.result -ne 'passed') {$all=$false}
    }
    Save-Report
    return $all
}
function Assert-PrivateRepo {
    if ($script:fixture) {return}
    if ($ReceiptRepo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {throw 'Bad receipt repo'}
    $visibility=(Run-Checked 'gh' @('api',"repos/$ReceiptRepo",'--jq','.private')).Trim()
    if ($visibility -ne 'true') {throw 'Receipt repository must remain PRIVATE'}
}
function Run-LocalChecks {
    if ($SkipLocalTests -or $StatusOnly -or $script:fixture) {return}
    Stage 'testing' 'Release build and device deployment test'
    $project='src\OpenQuickHost\OpenQuickHost.csproj'
    Push-Location $script:root
    try {
        $null=Run-Checked 'dotnet' @('build',$project,'-c','Release','--no-restore','-v:q')
        $null=Run-Checked 'powershell.exe' @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
            '-File',(Join-Path $script:root 'scripts\test-device-deploy-agent.ps1'))
        foreach($test in $ExtraTestCommands) {
            if ($test -notmatch '^[\w\\./: \-]+$') {throw 'Unsafe custom test command'}
            & powershell.exe -NoProfile -NonInteractive -Command $test | Out-Null
            if ($LASTEXITCODE -ne 0) {throw 'Custom test failed'}
        }
        $script:report.tests='passed'
    } finally {Pop-Location}
}
function Finish([string]$outcome,[string]$why,[int]$exitCode) {
    $script:report.outcome=$outcome
    $script:report.reason=$why
    Stage 'finished' "$outcome; $why"
    Write-Output "RELEASE_QA_RESULT=$outcome"
    if (-not $DryRun) {Write-Output "RELEASE_QA_REPORT=$script:reportFile"}
    exit $exitCode
}
try {
    if (-not $Version) {$Version=Read-ProjectVersion}
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {throw 'Invalid version format'}
    if (-not $script:fixture -and $Version -ne (Read-ProjectVersion)) {
        throw 'Requested version does not match source project'
    }
    if ($script:fixture -and -not $DryRun) {throw 'Fixture mode requires -DryRun'}
    $script:reportFile=Join-Path $ReportDirectory "v$Version.json"
    $previous=$null
    if (-not $DryRun -and (Test-Path -LiteralPath $script:reportFile)) {
        try {$previous=Get-Content -LiteralPath $script:reportFile -Raw|ConvertFrom-Json} catch {}
    }
    $script:report=[ordered]@{
        schemaVersion=1;version=$Version; startedAtUtc=Now-Utc;updatedAtUtc=''
        sourceCommit='';phase='created';outcome='pending';reason=''
        tests='not-run';workflowRunId='';releaseUrl='';publishedAtUtc=''
        devices=[ordered]@{};events=@()
    }
    foreach($machine in $script:deviceNames) {$script:report.devices[$machine]=@{result='pending'}}
    Stage 'preflight' 'Checking source, release and receipt channel'
    if (-not $script:fixture) {
        if ($StatusOnly) {
            $script:report.sourceCommit=(Run-Checked 'git' @('-C',$script:root,'rev-parse','HEAD')).Trim()
        } else { $script:report.sourceCommit=Assert-Git-Ready }
        Assert-PrivateRepo
    } else {$script:report.sourceCommit='fixture'}
    $release=Read-Release
    if (-not $release -and $StatusOnly) {Finish 'pending' 'Requested release is not published' 2}
    if (-not $release -and -not $StatusOnly) {
        Run-LocalChecks
        $workflow=Read-Workflow
        if ($workflow -and $workflow.status -eq 'completed' -and $workflow.conclusion -ne 'success' -and
            -not $RetryFailedWorkflow) {throw 'Existing CI run failed; specify -RetryFailedWorkflow after fixing'}
        if (-not $workflow -or ($RetryFailedWorkflow -and $workflow.status -eq 'completed' -and
            $workflow.conclusion -ne 'success')) {
            if ($DryRun) {Finish 'pending' 'Dry-run did not publish' 2}
            Stage 'publishing' 'Dispatching release workflow once'
            $null=Run-Checked 'powershell.exe' @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
                '-File',(Join-Path $script:root 'scripts\deploy-to-devices.ps1'),
                '-Version',$Version,'-NoWait')
        }
        Stage 'publishing' 'Waiting for matching main SHA to publish'
        while ([DateTime]::UtcNow -lt $script:deadline) {
            $release=Read-Release
            if ($release) {break}
            $workflow=Read-Workflow
            if ($workflow) {
                $script:report.workflowRunId=[string]$workflow.databaseId
                if ($workflow.status -eq 'completed' -and $workflow.conclusion -ne 'success') {
                    throw "Workflow concluded $($workflow.conclusion)"
                }
            }
            Save-Report
            Start-Sleep -Seconds $PollSeconds
        }
        if (-not $release) {Finish 'pending' 'CI did not finish within deadline' 2}
    }
    Assert-Release $release
    Stage 'published' 'Verified official release and SHA-256 asset metadata'
    if ($DryRun) {
        if (Verify-Devices) {Finish 'passed' 'Fixture: two verified devices' 0}
        Finish 'pending' 'Fixture: not all devices verified' 2
    }
    Stage 'installing' 'Waiting for authenticated device receipts, not assuming CI means installation'
    while ([DateTime]::UtcNow -lt $script:deadline) {
        if (Verify-Devices) {Finish 'passed' 'Both devices have current version and running process' 0}
        Start-Sleep -Seconds $PollSeconds
    }
    Finish 'pending' 'Device verification timed out; see both device results' 2
} catch {
    if ($script:report) {
        $script:report.reason=$_.Exception.Message
        $script:report.outcome='failed'
        Stage 'error' $_.Exception.Message
    }
    Write-Error ("RELEASE_QA_FAILED="+$_.Exception.Message)
    exit 1
}

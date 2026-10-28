param(
    [string]$RepositoryRoot = "",
    [Parameter(Mandatory=$true)][string]$VerificationScript,
    [ValidateRange(1,120)][int]$TimeoutMinutes = 30,
    [ValidateRange(5,60)][int]$PollSeconds = 20,
    [switch]$Apply,
    [switch]$Offline,
    [switch]$AllowProductionDeploy
)
$ErrorActionPreference = 'Stop'
if ($Offline -and $Apply) { throw 'Offline cannot be combined with Apply.' }
if (!$RepositoryRoot) { $RepositoryRoot = Join-Path $PSScriptRoot '..' }
$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
function InvokeGitChecked([string[]]$Arguments) {
    $result = @(& git.exe -C $root @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "Git command failed: $(($Arguments) -join ' ')" }
    return $result
}
function Github([string[]]$Arguments) {
    $result = @(& gh @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $(($Arguments) -join ' ')" }
    return $result
}
$top = (@(InvokeGitChecked @('rev-parse','--show-toplevel')) | Select-Object -First 1).Trim()
if (![IO.Path]::GetFullPath($top).TrimEnd('\').Equals($root,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'RepositoryRoot must be a Git worktree root.'
}
$branch = (@(InvokeGitChecked @('branch','--show-current')) | Select-Object -First 1).Trim()
if ($branch -in @('main','master') -or $branch -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$') {
    throw 'A named task branch, not main, is required.'
}
$dirty = @(InvokeGitChecked @('status','--porcelain','--untracked-files=normal'))
if (@($dirty | Where-Object { $_ }).Count) { throw 'Uncommitted or untracked files exist; commit only reviewed task changes.' }
$testFile = [IO.Path]::GetFullPath($VerificationScript)
if (!$testFile.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or
    !(Test-Path $testFile -PathType Leaf) -or [IO.Path]::GetExtension($testFile) -ne '.ps1') {
    throw 'VerificationScript must be an existing .ps1 inside the task worktree.'
}
if (!$Offline) { $null = InvokeGitChecked @('fetch','origin','main') }
$base = 'refs/remotes/origin/main'
$null = InvokeGitChecked @('rev-parse','--verify',$base)
$head = (@(InvokeGitChecked @('rev-parse','HEAD')) | Select-Object -First 1).Trim()
$main = (@(InvokeGitChecked @('rev-parse',$base)) | Select-Object -First 1).Trim()
& git -C $root merge-base --is-ancestor $base HEAD
if ($LASTEXITCODE -ne 0) { throw 'Task branch is not based on latest main; reconcile in an isolated worktree.' }
if ($head -eq $main) { throw 'No new commits to integrate.' }
$files = @(InvokeGitChecked @('diff','--name-only',"$base...HEAD",'--'))
if (!$files.Count) { throw 'Task contains no file changes.' }
$sensitive = @($files | Where-Object {
    $_ -match '(^|/)(\.env(\.|$)|credentials|secrets|private-key|node_modules|\.artifacts|ExtensionStorage)(/|\.|$)' -or
    $_ -match '\.(pfx|pem|p12|key)$'
})
if ($sensitive.Count) { throw "Protected files blocked: $($sensitive -join ', ')" }
$production = @($files | Where-Object {
    $_ -match '^cloudflare/' -or $_ -in @('wrangler.jsonc','.github/workflows/deploy-cloudflare.yml')
})
if ($production.Count -and !$AllowProductionDeploy) { throw 'Cloudflare production changes require explicit deployment approval; auto merge blocked.' }
$null = InvokeGitChecked @('diff','--check',"$base...HEAD",'--')
Write-Output "INTEGRATION_PLAN branch=$branch main=$main task=$head changedFiles=$($files.Count) cloudflareFiles=$($production.Count)"
if (!$Apply) {
    Write-Output 'PLAN_ONLY: no push, PR, merge, deployment or release.'
    return
}
$added = @(InvokeGitChecked @('diff','--unified=0',"$base...HEAD",'--') | Where-Object { $_ -like '+*' -and $_ -notlike '+++*' })
if (($added -join [Environment]::NewLine) -match '(?i)(ghp_[A-Za-z0-9]{15,}|github_pat_[A-Za-z0-9_]{25,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY)') {
    throw 'Possible credential detected in the diff.'
}
Write-Output 'RUNNING_TASK_VERIFICATION'
& powershell -NoProfile -ExecutionPolicy Bypass -File $testFile
if ($LASTEXITCODE -ne 0) { throw 'Task verification failed. Keep branch and repair.' }
$null = InvokeGitChecked @('push','--set-upstream','origin',$branch)
$prNumber = ((@(Github @('pr','list','--repo','luoluoluo22/yanzi','--base','main','--head',$branch,'--state','open','--json','number','--jq','.[0].number'))) -join '').Trim()
if (!$prNumber -or $prNumber -eq 'null') {
    $title = (@(InvokeGitChecked @('log','-1','--format=%s')) | Select-Object -First 1).Trim()
    $body = [string]::Join([Environment]::NewLine, @('Agent task integration',"Source: $head","Verification: $($testFile.Substring($root.Length+1))","Changed files: $($files.Count)",'No release or installation requested.'))
    $null = Github @('pr','create','--repo','luoluoluo22/yanzi','--base','main','--head',$branch,'--title',$title,'--body',$body)
    $prNumber = ((@(Github @('pr','list','--repo','luoluoluo22/yanzi','--base','main','--head',$branch,'--state','open','--json','number','--jq','.[0].number'))) -join '').Trim()
}
if ($prNumber -notmatch '^\d+$') { throw 'Unable to locate task PR after push. Investigate before claiming merged.' }
Write-Output "INTEGRATION_PR_OPEN number=$prNumber"
$expires = (Get-Date).AddMinutes($TimeoutMinutes)
do {
    $pr = ((@(Github @('pr','view',$prNumber,'--repo','luoluoluo22/yanzi','--json','state,isDraft,mergeable,headRefOid,baseRefName'))) -join '') | ConvertFrom-Json
    if ($pr.state -eq 'MERGED') { break }
    if ($pr.state -ne 'OPEN' -or $pr.isDraft -or $pr.baseRefName -ne 'main' -or $pr.headRefOid -ne $head) {
        throw "PR #$prNumber was changed or closed; stop integration."
    }
    if ($pr.mergeable -eq 'CONFLICTING') { throw "PR #$prNumber has merge conflicts. Resolve them and rerun." }
    # Exit code 8 means checks pending; 1 means checks failed, both may return JSON.
    $checkRaw = @(& gh pr checks $prNumber --repo luoluoluo22/yanzi --json name,bucket)
    if ($LASTEXITCODE -notin @(0,1,8)) { throw "PR #$prNumber checks API unavailable." }
    $checks = (($checkRaw) -join '') | ConvertFrom-Json
    $items = @($checks)
    $desktop = @($items | Where-Object { $_.name -match 'desktop-and-worker' })
    $portable = @($items | Where-Object { $_.name -match 'portable-contracts' })
    if ($desktop.Count -and $portable.Count) {
        $bad = @($items | Where-Object { $_.bucket -in @('fail','cancel') })
        if ($bad.Count) { throw "PR #$prNumber CI failed: $(($bad.name) -join ', ')" }
        $waiting = @($items | Where-Object { $_.bucket -notin @('pass','skipping') })
        if (!$waiting.Count -and $pr.mergeable -eq 'MERGEABLE') { break }
    }
    Write-Output "AWAITING_CI pr=$prNumber checks=$($items.Count) mergeable=$($pr.mergeable)"
    if ((Get-Date) -ge $expires) { throw "PR #$prNumber pending beyond timeout; rerun later. Task not merged." }
    Start-Sleep -Seconds $PollSeconds
} while ($true)
if ($pr.state -ne 'MERGED') { $null = Github @('pr','merge',$prNumber,'--repo','luoluoluo22/yanzi','--squash','--delete-branch') }
$done = ((@(Github @('pr','view',$prNumber,'--repo','luoluoluo22/yanzi','--json','state,mergeCommit,mergedAt,url'))) -join '') | ConvertFrom-Json
if ($done.state -ne 'MERGED' -or !$done.mergedAt -or !$done.mergeCommit.oid) { throw "PR #$prNumber not confirmed merged." }
$null = InvokeGitChecked @('fetch','origin','main')
& git -C $root merge-base --is-ancestor $done.mergeCommit.oid refs/remotes/origin/main
if ($LASTEXITCODE -ne 0) { throw 'Merged commit not yet in freshly fetched main.' }
Write-Output "AGENT_INTEGRATION_MERGED pr=$prNumber main=$($done.mergeCommit.oid) url=$($done.url) releasePerformed=false"

param(
  [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
  [Parameter(Mandatory=$true)][string]$ReleaseWorktree,
  [Parameter(Mandatory=$true)][string]$BaseCommit,
  [Parameter(Mandatory=$true)][string[]]$ApprovedFiles,
  [Parameter(Mandatory=$true)][string[]]$Artifacts,
  [Parameter(Mandatory=$true)][string]$VerificationScript,
  [Parameter(Mandatory=$true)][string]$ReleaseNotesFile,
  [string]$Repo = 'luoluoluo22/yanzi',
  [switch]$Apply
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ReleaseWorktree).TrimEnd('\')
$main=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
if ($root.Equals($main,[StringComparison]::OrdinalIgnoreCase)) { throw 'Releases must use an isolated worktree, not the dirty main worktree.' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Release worktree not found.' }
$gitTop=(& git -C $root rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or -not [IO.Path]::GetFullPath($gitTop).TrimEnd('\').Equals($root,[StringComparison]::OrdinalIgnoreCase)) { throw 'Release source must be an actual Git worktree root.' }
$branch=(& git -C $root branch --show-current).Trim()
if ($branch -notmatch '^ai-loop/[a-zA-Z0-9/_-]+$') { throw 'Release source must be on an explicitly named ai-loop branch.' }
$dirty=@(& git -C $root status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or @($dirty|Where-Object{$_}).Count -ne 0) { throw 'Release worktree contains unstaged, staged, or untracked changes.' }
$head=(& git -C $root rev-parse HEAD).Trim()
& git -C $root merge-base --is-ancestor $BaseCommit $head
if ($LASTEXITCODE -ne 0 -or $BaseCommit -eq $head) { throw 'Expected a reviewed commit above a known base.' }
$changed=@(& git -C $root diff --name-only $BaseCommit $head --)
$changed=@($changed|Where-Object{$_})
$allow=@($ApprovedFiles|ForEach-Object{$_.Replace('\','/')})
$changedNormalized=@($changed|ForEach-Object{$_.Replace('\','/')})
if (-not $changedNormalized.Count) { throw 'No reviewed code changes to release.' }
foreach($f in $changedNormalized) {
    if ($f -notin $allow) { throw "Unapproved changed file: $f" }
    if ($f -match '(?i)(^|/)(\.env|\.github/secrets|credentials|secrets|token|private-key|\.artifacts|node_modules|\.tmp)(/|\.|$)' -or $f -match '(?i)\.(pfx|pem|key|p12)$') { throw "Protected release file: $f" }
}
& git -C $root diff --check $BaseCommit $head --
if ($LASTEXITCODE -ne 0) { throw 'Source diff check failed.' }
$diffText=(& git -C $root diff --unified=0 $BaseCommit $head -- $changedNormalized) -join [Environment]::NewLine
if ($diffText -match '(?i)(ghp_[A-Za-z0-9]{15,}|github_pat_[A-Za-z0-9_]{25,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY)') { throw 'Source diff contains a probable credential.' }

$project=Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj'
if (-not (Test-Path -LiteralPath $project)) { throw 'Release project missing.' }
[xml]$projectXml=Get-Content -LiteralPath $project -Encoding UTF8
if ($projectXml.Project.PropertyGroup.Version.Trim() -ne $Version) { throw 'Release source version and requested tag do not match.' }

$notes=[IO.Path]::GetFullPath($ReleaseNotesFile)
if (-not (Test-Path -LiteralPath $notes -PathType Leaf)) { throw 'Release notes missing.' }
$notesContent=Get-Content -LiteralPath $notes -Raw -Encoding UTF8
if ($notesContent.Trim().Length -lt 45 -or $notesContent -match '请在此编辑更新说明') { throw 'Release notes are incomplete.' }
if ($notesContent -match '(?i)ghp_[A-Za-z0-9]{15,}|github_pat_[A-Za-z0-9_]{25,}|sk-[A-Za-z0-9]{20,}') { throw 'Release notes include credentials.' }

$test=[IO.Path]::GetFullPath($VerificationScript)
if (-not (Test-Path -LiteralPath $test -PathType Leaf) -or [IO.Path]::GetExtension($test) -ne '.ps1') { throw 'Repeatable PS verification script required.' }
if (-not $test.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -and -not $test.StartsWith($main+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Verification script outside approved source roots.' }

$files=@($Artifacts|ForEach-Object{[IO.Path]::GetFullPath($_)})
$allowedArtifactRoot=[IO.Path]::GetFullPath((Join-Path $root '.artifacts\installer')).TrimEnd('\')+'\'
foreach($asset in $files) {
    if (-not $asset.StartsWith($allowedArtifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Release asset must come from the isolated reviewed worktree installer output.' }
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Release artifact missing: $asset" }
    $name=[IO.Path]::GetFileName($asset)
    $isMetadata=$name -in @('assets.win.json','releases.win.json','RELEASES')
    $minimumBytes=if ($isMetadata) { 32 } else { 4096 }
    if ((Get-Item -LiteralPath $asset).Length -lt $minimumBytes) { throw "Release artifact too small: $asset" }
    if ($name -in @('assets.win.json','releases.win.json')) {
        try { $null=(Get-Content -LiteralPath $asset -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop) }
        catch { throw "Release metadata is not valid JSON: $asset" }
    }
    if ($name -in @('releases.win.json','RELEASES')) {
        if (-not (Select-String -LiteralPath $asset -Pattern ([regex]::Escape($Version)) -Quiet)) {
            throw "Release metadata does not reference version $Version : $asset"
        }
    }
}
if (@($files|Where-Object{[IO.Path]::GetFileName($_) -eq "Yanzi-win-Setup-$Version.exe"}).Count -ne 1) { throw 'Version-specific Windows installer is required. No recycled or wrong-version assets.' }
$tag='v'+$Version
$releaseBranch='ai-loop/release/v'+$Version
$lockPath=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\ai-loop-release-locks\github-release.lock'
New-Item -Path (Split-Path $lockPath) -ItemType Directory -Force | Out-Null

if (-not $Apply) {
    Write-Output 'PUBLIC_RELEASE_PLAN_ONLY=True'
    Write-Output "VERSION=$Version"
    Write-Output "SOURCE_COMMIT=$head"
    Write-Output "BRANCH=$releaseBranch"
    Write-Output "REVIEWED_FILE_COUNT=$($changedNormalized.Count)"
    Write-Output "ARTIFACT_COUNT=$($files.Count)"
    Write-Output 'No repository push, tag creation, or public publication performed.'
    return
}

$lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    # Run verification again immediately before publication.
    & $test
    if (-not $? -or ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0)) { throw 'Release verification failed.' }
    $previousErrorActionPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & gh release view $tag --repo $Repo --json tagName 2>$null | Out-Null
        $releaseExists=($LASTEXITCODE -eq 0)
    } finally { $ErrorActionPreference=$previousErrorActionPreference }
    if ($releaseExists) { throw 'Version already published. Refuse to overwrite assets or edit existing release.' }

    # Never git add . and never push to main automatically. This is a dedicated reviewed branch.
    & git -C $root push origin ($head+':refs/heads/'+$releaseBranch)
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed release commit push failed. No GitHub release created.' }

    $args=@('release','create',$tag,'--repo',$Repo,'--target',$releaseBranch,'--title',('Yanzi '+$Version),'--notes-file',$notes)
    $args+=@($files)
    & gh @args
    if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation/upload failed. Investigate the pushed review branch.' }
    $result=& gh release view $tag --repo $Repo --json url,isDraft,assets,tagName
    if ($LASTEXITCODE -ne 0) { throw 'Release created but remote verification failed.' }
    $status=$result | ConvertFrom-Json
    if ($status.isDraft -or @($status.assets).Count -lt @($files).Count) { throw 'Published release is missing expected visible assets.' }
    Write-Output ('PUBLIC_RELEASE_ACCEPTED='+$status.url)
    Write-Output ('SOURCE_COMMIT='+$head)
} finally {
    $lock.Dispose()
}

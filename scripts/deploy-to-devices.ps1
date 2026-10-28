#Requires -Version 5.1
# From the HOME repository after merge, publish the verified Windows release.
# Office receivers independently poll for the official published release.
param(
    [string] $Version = '',
    [switch] $NoWait,
    [ValidateRange(1,90)][int] $MaxWaitMinutes = 60
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$proj = Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj'
[xml]$project = Get-Content -LiteralPath $proj -Encoding UTF8
$currentVersion = $project.Project.PropertyGroup.Version
if (-not $Version) { $Version = $currentVersion }
if ($Version -ne $currentVersion -or $Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must equal the source project version: $currentVersion"
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) required' }
$branch = (& git -C $root branch --show-current).Trim()
if ($branch -ne 'main') { throw "Publish requires main branch, not $branch" }
if (-not [string]::IsNullOrWhiteSpace([string](& git -C $root status --porcelain))) { throw 'Uncommitted files: publish requires clean main' }
$local = (& git -C $root rev-parse HEAD).Trim()
$remote = (& git -C $root ls-remote origin refs/heads/main)
if ($LASTEXITCODE -ne 0 -or -not $remote) { throw 'Cannot verify remote main' }
$remoteHash = ($remote -split '\s+')[0]
if ($local -ne $remoteHash) { throw 'Local main differs from origin/main; merge/push first' }
$workflow='publish-yanzi-device-release.yml'
$repo='luoluoluo22/yanzi'
Write-Output "SOURCE_CONFIRMED=$local"
& gh workflow run $workflow --repo $repo --ref main --field "version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Failed to dispatch release workflow' }
Write-Output "PUBLISH_DISPATCHED=v$Version"
if ($NoWait) { return }
$deadline = [DateTime]::UtcNow.AddMinutes($MaxWaitMinutes)
Start-Sleep -Seconds 4
$runId=$null
while ([DateTime]::UtcNow -lt $deadline) {
    $runs = & gh run list --repo $repo --workflow $workflow --limit 10 --json databaseId,event,status,conclusion,headSha,createdAt | ConvertFrom-Json
    $match = @($runs | Where-Object { $_.headSha -eq $local -and $_.event -eq 'workflow_dispatch' } | Sort-Object createdAt -Descending)
    if ($match.Count -gt 0) { $runId=[string]$match[0].databaseId; break }
    Start-Sleep -Seconds 5
}
if (-not $runId) { throw 'Workflow run not found' }
Write-Output "PUBLISH_RUN=https://github.com/$repo/actions/runs/$runId"
while ([DateTime]::UtcNow -lt $deadline) {
    $info = & gh run view $runId --repo $repo --json status,conclusion | ConvertFrom-Json
    if ($info.status -eq 'completed') {
        if ($info.conclusion -ne 'success') { throw "Release workflow failed: $($info.conclusion)" }
        $assets = & gh release view "v$Version" --repo $repo --json isDraft,assets | ConvertFrom-Json
        if ($assets.isDraft -or -not (@($assets.assets.name) -contains "Yanzi-win-Setup-$Version.exe")) {
            throw 'GitHub release is still draft or missing installer'
        }
        Write-Output "PUBLISHED=v$Version"
        Write-Output "OFFICE_RECEIVER=checks within registered scheduled interval; consult device status for installation outcome"
        return
    }
    Start-Sleep -Seconds 12
}
throw 'Release workflow still running when local wait deadline ended; inspect GitHub Actions'

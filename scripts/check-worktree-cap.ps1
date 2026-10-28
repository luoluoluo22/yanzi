param(
    [ValidateRange(2, 30)][int]$MaximumWorktrees = 5,
    [switch]$Strict
)
$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) { throw 'Run this command from a Git repository.' }
$root = ($root | Select-Object -First 1).Trim()
$lines = @(git -C $root worktree list --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate Git worktrees.' }
$trees = @(
    foreach ($line in $lines) {
        if ($line -match '^worktree (.+)$') { $Matches[1] }
    }
)
Write-Output "YANZI_WORKTREE_COUNT=$($trees.Count); maximum=$MaximumWorktrees"
foreach ($path in $trees) {
    $branch = (git -C $path branch --show-current 2>$null | Select-Object -First 1)
    if (-not $branch) { $branch = '(detached)' }
    $status = @(git -C $path status --porcelain -uall 2>$null)
    $state = if ($LASTEXITCODE -ne 0) { 'unknown' } elseif ($status.Count) { 'dirty' } else { 'clean' }
    Write-Output "WORKTREE state=$state branch=$branch path=$path"
}
if ($trees.Count -gt $MaximumWorktrees) {
    $excess = $trees.Count - $MaximumWorktrees
    Write-Warning "Too many worktrees ($excess over limit). Reconcile against origin/main; archive changes before removing stale directories."
    if ($Strict) { exit 2 }
}
Write-Output 'WORKTREE_CAP_CHECK_COMPLETE'

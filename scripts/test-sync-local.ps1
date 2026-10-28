param(
    [int]$Port = 8799
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

& pwsh -NoProfile -File (Join-Path $repoRoot "scripts/test-network-retry.ps1")
if ($LASTEXITCODE -ne 0) { throw "Network retry test failed." }

foreach ($mode in @("false", "true")) {
    & pwsh -NoProfile -File (Join-Path $repoRoot "scripts/test-cloud-object-sync.ps1") -Port $Port -AuthorityMode $mode
    if ($LASTEXITCODE -ne 0) { throw "Local sync protocol test failed: authoritative=$mode." }
}

Write-Output "Local sync automation passed."

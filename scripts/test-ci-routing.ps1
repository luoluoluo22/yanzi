#Requires -Version 5.1
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & python scripts/test-ci-change-classifier.py
    if($LASTEXITCODE -ne 0){throw 'CI classification unit/policy tests failed'}
    & python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -q
    if($LASTEXITCODE -ne 0){throw 'Developer tool regression failed'}
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/test-agent-finish-policy.ps1
    if($LASTEXITCODE -ne 0){throw 'Existing PR integration policy failed'}
    $head=(& git rev-parse HEAD).Trim()
    $base=(& git rev-parse origin/main).Trim()
    if($head -ne $base){
        $classification=(& python scripts/classify-ci-change.py --event pull_request --base $base --head $head) -join [Environment]::NewLine
        if($LASTEXITCODE -ne 0 -or $classification -notmatch 'tier=full'){
            throw 'The CI workflow/policy change must take full regression'
        }
    }
    Write-Output 'CI_TIERING_VERIFICATION=PASS'
} finally {Pop-Location}

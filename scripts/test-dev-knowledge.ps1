#Requires -Version 5.1
# Test candidate discovery and an actual opt-in isolated QA evidence writeback.
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -v
    if($LASTEXITCODE -ne 0){throw 'Developer knowledge unit tests failed'}
    $coverage=(& python tools/dev-navigator/knowledge_cli.py coverage --query 'SettingsWindow' --limit 5 --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'UI discovery lookup failed'}
    $found=$coverage|ConvertFrom-Json
    if($found.matchedCandidates -lt 1){throw 'Expected grounded UI candidates'}
    $json=(& python tools/dev-navigator/preflight.py --query ai-model-menu --expect scrollable-models --run-targeted --run-hidden-ui --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Isolated targeted or hidden UI QA failed'}
    $plan=$json|ConvertFrom-Json
    if(@($plan.qaRecorded).Count -ne 2){throw 'Expected both tests to generate evidence'}
    if(@($plan.qaRecorded|Where-Object {$_.status -ne 'passed'}).Count -gt 0){throw 'Test reported non-passing QA'}
    if($plan.qaHistory.'ai-model-menu'.installedAppUi -ne 'not-verified'){throw 'Synthetic QA misrepresented installed UI'}
    $history=(& python tools/dev-navigator/knowledge_cli.py qa --feature ai-model-menu --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'QA ledger read failed'}
    $ledger=$history|ConvertFrom-Json
    if(@($ledger.features.'ai-model-menu'.records).Count -lt 2){throw 'Recorded evidence missing'}
    $repoHead=(& git rev-parse HEAD).Trim()
    if(@($plan.qaRecorded|Where-Object {$_.commit -ne $repoHead}).Count -gt 0){throw 'QA source provenance wrong'}
    Write-Output "UI_UNMAPPED_FILTER_MATCHES=$($found.matchedCandidates)"
    Write-Output 'QA_ACTUAL_TESTS_RECORDED=2'
    Write-Output 'DEV_KNOWLEDGE_VERIFICATION=PASS'
} finally {Pop-Location}

#Requires -Version 5.1
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -v
    if($LASTEXITCODE -ne 0){throw 'Developer diagnostic unit tests failed'}
    $gemini=(& python tools/dev-navigator/diagnose.py --description 'Gemini 401 UNAUTHENTICATED CREDENTIALS_MISSING' --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Gemini triage command failed'}
    $model=(& python tools/dev-navigator/diagnose.py --description 'ai-model-menu' --screenshot-note 'model menu has 50 rows but cannot scroll' --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Model menu triage command failed'}
    $a=$gemini|ConvertFrom-Json
    $b=$model|ConvertFrom-Json
    if($a.features[0].id -ne 'ai-provider-settings'){throw 'Wrong Gemini auth routing'}
    if($b.features[0].id -ne 'ai-model-menu'){throw 'Wrong model menu routing'}
    if($a.status -ne 'triage-candidate-not-confirmed' -or $b.status -ne 'triage-candidate-not-confirmed'){
        throw 'Triage incorrectly claimed root cause confirmation'
    }
    if($b.input.screenshotImageAnalyzed){throw 'Screenshot pixels falsely claimed analyzed'}
    if(@($b.recentChanges).Count -lt 1){throw 'Recent code history not linked'}
    Write-Output 'DEV_DIAGNOSTIC_TESTS=PASS'
} finally {Pop-Location}

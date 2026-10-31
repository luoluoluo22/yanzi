#Requires -Version 5.1
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -v
    if($LASTEXITCODE -ne 0){throw 'Developer preflight Python tests failed'}
    $json=(& python tools/dev-navigator/preflight.py --query 'ai-model-menu' --ui-query 'AiChatModelMenuButton_Click' --expect '50 models scroll' --path 'src/Yanzi.UI.Wpf/YanziDropdownMenu.cs' --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Real-source developer preflight failed'}
    $plan=$json|ConvertFrom-Json
    if(@($plan.source.features|Where-Object {$_.id -eq 'ai-model-menu'}).Count -lt 1){throw 'Model feature missing'}
    if(@($plan.impact.affected|Where-Object {$_.id -eq 'ai-model-menu'}).Count -lt 1){throw 'Model impact missing'}
    if($plan.validation.hiddenUiSmoke.scenario -ne 'ai-chat-model'){throw 'Dedicated hidden UI scenario missing'}
    if($plan.validation.hiddenUiSmoke.automaticallyCoversFeature){throw 'Test coverage overclaimed'}
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-hidden.ps1 -Scenario ai-chat-model -Mode hidden
    if($LASTEXITCODE -ne 0){throw 'Isolated 50-model UI test failed'}
    Write-Output 'DEV_PREFLIGHT_TESTS=PASS'
} finally {Pop-Location}

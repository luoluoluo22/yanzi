#Requires -Version 5.1
# Deterministic, read-only developer navigator regression gate.
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    & python -m unittest discover -s tools/dev-navigator -p test_navigator.py -v
    if($LASTEXITCODE -ne 0){throw 'Navigator unit tests failed'}
    $findOutput=(& python tools/dev-navigator/navigator.py find --query ai-model-menu --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Navigator feature lookup failed'}
    $feature=@($findOutput|ConvertFrom-Json)[0]
    if($feature.id -ne 'ai-model-menu'){throw 'Wrong model-menu feature route'}
    $uiOutput=(& python tools/dev-navigator/navigator.py ui --query AiChatModelMenuButton_Click --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Navigator UI lookup failed'}
    $ui=@($uiOutput|ConvertFrom-Json)[0]
    if($ui.path -ne 'src/OpenQuickHost/MainWindow.xaml' -or
       @($ui.definitions|Where-Object {$_.path -eq 'src/OpenQuickHost/MainWindow.AiChat.cs'}).Count -lt 1){
        throw 'Model menu UI-to-handler route is incomplete'
    }
    $impactOutput=(& python tools/dev-navigator/navigator.py impact --path src/OpenQuickHost/AiModelMenuOrdering.cs --json) -join [Environment]::NewLine
    if($LASTEXITCODE -ne 0){throw 'Navigator impact lookup failed'}
    $impact=$impactOutput|ConvertFrom-Json
    if(@($impact.affected|Where-Object {$_.id -eq 'ai-model-menu'}).Count -lt 1 -or
       @($impact.suggestedTests|Where-Object {$_ -match 'ModelMenuScrollVerification'}).Count -lt 1){
        throw 'Model menu impact failed to suggest its regression test'
    }
    Write-Output 'DEV_NAVIGATOR_VERIFICATION=PASS'
} finally {Pop-Location}

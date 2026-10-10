#requires -version 5.1
<#
Reusable hidden-desktop WPF UI tests for Yanzi.
No global mouse or keyboard input. Does not write the clipboard.
#>
[CmdletBinding()]
param(
    [ValidateSet('all','shared-ui','capture-ocr-toast')]
    [string]$Scenario = 'all',
    [ValidateSet('hidden','offscreen')]
    [string]$Mode = 'hidden',
    [int]$TimeoutMs = 15000,
    [int]$MaxMemoryMb = 350,
    [string]$OutputDirectory = '.tmp/ui-test-runs',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    if ($Mode -eq 'offscreen' -and $Scenario -ne 'shared-ui') {
        throw 'OCR toast test cannot use offscreen mode: it would show a toast on the current desktop. Use hidden mode.'
    }
    $sampleProject = 'tools/yanzi-ui-test-samples/Yanzi.UiTestSamples.csproj'
    $runnerProject = 'tools/yanzi-ui-test-runner/Yanzi.UiTestRunner.csproj'
    if (-not $NoBuild) {
        & dotnet build $sampleProject -c Release --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'UiTest sample compilation failed.' }
        & dotnet build $runnerProject -c Release --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'UiTest runner compilation failed.' }
    }
    $assembly = (Resolve-Path 'tools/yanzi-ui-test-samples/bin/Release/net9.0-windows10.0.19041.0/Yanzi.UiTestSamples.dll').Path
    $runner = (Resolve-Path 'tools/yanzi-ui-test-runner/bin/Release/net9.0-windows/Yanzi.UiTestRunner.dll').Path
    $out = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    $cases = switch ($Scenario) {
        'all' { @('Yanzi.UiTestSamples.PublicControlsScenario','Yanzi.UiTestSamples.CaptureOcrToastScenario') }
        'shared-ui' { @('Yanzi.UiTestSamples.PublicControlsScenario') }
        'capture-ocr-toast' { @('Yanzi.UiTestSamples.CaptureOcrToastScenario') }
    }
    $passed = 0
    $failed = 0
    foreach($case in $cases) {
        Write-Output ('--- '+$case+' ---')
        & dotnet $runner --assembly $assembly --type $case --mode $Mode --out $out --timeout-ms $TimeoutMs --max-memory-mb $MaxMemoryMb
        if ($LASTEXITCODE -eq 0) { $passed++ } else { $failed++ }
    }
    Write-Output ('UI_TEST_SUITE_PASS='+$passed)
    Write-Output ('UI_TEST_SUITE_FAIL='+$failed)
    if ($failed -gt 0) {exit 1}
    Write-Output 'UI_TEST_SUITE_RESULT=PASS'
}
finally {Pop-Location}

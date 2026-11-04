#requires -version 5.1
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & dotnet build tests/DesktopPerformanceVerification -c Debug --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Desktop performance build failed.' }
    & dotnet run --project tests/DesktopPerformanceVerification -c Debug --no-build -- --dev
    if ($LASTEXITCODE -ne 0) { throw 'Desktop performance regression failed.' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-ai-chat-regression.ps1
    if ($LASTEXITCODE -ne 0) { throw 'AI chat regression failed.' }
    & dotnet build tools/yanzi-ui-test-runner -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Hidden desktop runner build failed.' }
    & dotnet tools/yanzi-ui-test-runner/bin/Release/net9.0-windows/Yanzi.UiTestRunner.dll --assembly tests/DesktopPerformanceVerification/bin/Debug/net9.0-windows/DesktopPerformanceVerification.dll --type NativePerformanceScenario --mode hidden --out .tmp/desktop-performance/native --timeout-ms 90000 --max-memory-mb 900
    if ($LASTEXITCODE -ne 0) { throw 'Native host performance scenario failed.' }
    Write-Output 'DESKTOP_PERFORMANCE_VERIFIED'
}
finally { Pop-Location }

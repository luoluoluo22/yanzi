#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'test-ai-tool-experience.ps1')
& (Join-Path $PSScriptRoot 'test-ai-chat-generation.ps1')
& (Join-Path $PSScriptRoot 'test-ai-chat-markdown.ps1')
& dotnet run --project (Join-Path $root 'tests\AiProviderRoutingVerification') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Combined provider routing regression failed.' }
& dotnet build (Join-Path $root 'tools\yanzi-ui-test-runner') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Native UI runner build failed.' }
& dotnet (Join-Path $root 'tools\yanzi-ui-test-runner\bin\Release\net9.0-windows\Yanzi.UiTestRunner.dll') --assembly (Join-Path $root 'tests\DesktopPerformanceVerification\bin\Debug\net9.0-windows\DesktopPerformanceVerification.dll') --type NativePerformanceScenario --mode hidden --out (Join-Path $root '.tmp\ai-combined\native') --timeout-ms 90000 --max-memory-mb 900
if ($LASTEXITCODE -ne 0) { throw 'Combined native host verification failed.' }

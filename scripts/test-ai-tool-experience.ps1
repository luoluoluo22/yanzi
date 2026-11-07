#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'test-ai-shell-tools.ps1')
& dotnet run --project (Join-Path $root 'tests\WpfUiMigrationSmoke\WpfUiMigrationSmoke.csproj') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw "Tool execution WPF regression failed: $LASTEXITCODE" }

& dotnet run --project (Join-Path $root 'tests\DesktopPerformanceVerification') -c Debug -- --dev
if ($LASTEXITCODE -ne 0) { throw "Transcript integration regression failed: $LASTEXITCODE" }

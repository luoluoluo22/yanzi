#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $root 'tests\AiChatDetachedWindowVerification\AiChatDetachedWindowVerification.csproj') -c Release --no-launch-profile -- --dev
if ($LASTEXITCODE -ne 0) { throw "Detached chat verification failed: $LASTEXITCODE" }
& (Join-Path $PSScriptRoot 'verify-ai-chat-regression.ps1')
& dotnet run --project (Join-Path $root 'tests\AiWindowResizeGeometryVerification\AiWindowResizeGeometryVerification.csproj') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw "AI window geometry verification failed: $LASTEXITCODE" }

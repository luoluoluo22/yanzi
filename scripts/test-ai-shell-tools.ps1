#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet run --project (Join-Path $root 'tests\AiShellToolsVerification\AiShellToolsVerification.csproj') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw "AI shell verification failed: $LASTEXITCODE" }
& dotnet run --project (Join-Path $root 'tests\GeminiNativeChatVerification\GeminiNativeChatVerification.csproj') -c Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw "Gemini compatibility regression failed: $LASTEXITCODE" }
& (Join-Path $PSScriptRoot 'verify-ai-chat-regression.ps1')
& dotnet build (Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed: $LASTEXITCODE" }

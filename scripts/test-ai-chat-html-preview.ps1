#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'tests\AiChatHtmlPreviewVerification\AiChatHtmlPreviewVerification.csproj'
& dotnet run --project $project -c Release --no-launch-profile --nologo
if ($LASTEXITCODE -ne 0) { throw "AI Chat HTML preview verification failed: $LASTEXITCODE" }

param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\AiChatHtmlVerification\AiChatHtmlVerification.csproj'
& dotnet run --project $project --configuration Release --no-launch-profile --nologo
if ($LASTEXITCODE -ne 0) { throw "AI Chat HTML verification failed (exit $LASTEXITCODE)." }

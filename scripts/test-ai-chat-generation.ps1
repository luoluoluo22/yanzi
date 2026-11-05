param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\AiChatGenerationVerification\AiChatGenerationVerification.csproj'
& dotnet run --project $project --configuration Release --no-launch-profile --nologo
if ($LASTEXITCODE -ne 0) { throw "AI Chat generation verification failed (exit $LASTEXITCODE)." }

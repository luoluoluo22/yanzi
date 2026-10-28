$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj'
foreach ($scenario in @('--ai-model-discovery', '--ai-chat-parallel', '--ai-chat-sse')) {
    dotnet run --project $project -c Release -- $scenario
    if ($LASTEXITCODE -ne 0) { throw "Verification failed: $scenario" }
}
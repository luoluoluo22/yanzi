param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/Yanzi.NetworkRetryVerification/Yanzi.NetworkRetryVerification.csproj"
$output = Join-Path $repoRoot "artifacts/network-retry-verification"

& dotnet run --project $project -p:SkipStopRunningApp=true -p:OutputPath="$output/" --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    throw "Network retry verification failed."
}

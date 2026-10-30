param()
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repo
try {
    dotnet run --project tests/LocalLlmApiSmoke/LocalLlmApiSmoke.csproj -v:q -clp:ErrorsOnly -nologo
    if ($LASTEXITCODE -ne 0) { throw "Local LLM API smoke failed: $LASTEXITCODE" }
    dotnet build src/OpenQuickHost/OpenQuickHost.csproj --no-restore -p:GenerateAppxPackageOnBuild=false -v:q -clp:ErrorsOnly -nologo
    if ($LASTEXITCODE -ne 0) { throw "OpenQuickHost build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}

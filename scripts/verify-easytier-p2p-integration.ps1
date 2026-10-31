$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  dotnet restore src/OpenQuickHost/OpenQuickHost.csproj --ignore-failed-sources -v:q
  if ($LASTEXITCODE -ne 0) { throw 'restore host failed' }
  dotnet build src/OpenQuickHost/OpenQuickHost.csproj --no-restore -v:q
  if ($LASTEXITCODE -ne 0) { throw 'compile host failed' }
  dotnet build tests/TemporaryTransferIntegration/TemporaryTransferIntegration.csproj -c Release -v:q
  if ($LASTEXITCODE -ne 0) { throw 'existing integration harness compile failed' }
  dotnet run --project tests/TemporaryTransferVerification/TemporaryTransferVerification.csproj -c Release -v:q
  if ($LASTEXITCODE -ne 0) { throw 'temporary transfer regression failed' }
  dotnet run --project tests/EasyTierP2pVerification/EasyTierP2pVerification.csproj -c Release -v:q
  if ($LASTEXITCODE -ne 0) { throw 'EasyTier P2P verification failed' }
  git diff --check
  if ($LASTEXITCODE -ne 0) { throw 'diff whitespace check failed' }
  Write-Output 'EASYTIER_P2P_INTEGRATION_VERIFIED'
} finally { Pop-Location }

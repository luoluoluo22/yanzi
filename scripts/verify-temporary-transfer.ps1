#Requires -Version 5.1
param([switch] $LiveSftp, [switch] $LivePublic)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$unit = Join-Path $root 'tests/TemporaryTransferVerification/TemporaryTransferVerification.csproj'
$hostproj = Join-Path $root 'src/OpenQuickHost/OpenQuickHost.csproj'
$integration = Join-Path $root 'tests/TemporaryTransferIntegration/TemporaryTransferIntegration.csproj'
& dotnet run --project $unit --configuration Release --no-launch-profile -v q
if ($LASTEXITCODE -ne 0) { throw 'TemporaryTransferVerification failed' }
& dotnet build $hostproj --configuration Release -nologo -v q -p:SkipStopRunningApp=true
if ($LASTEXITCODE -ne 0) { throw 'Host build failed' }
if ($LiveSftp) {
    & dotnet run --project $integration --configuration Release --no-launch-profile -v q -- sg-sftp
    if ($LASTEXITCODE -ne 0) { throw 'Live SFTP roundtrip failed' }
}
if ($LivePublic) {
    & dotnet run --project $integration --configuration Release --no-launch-profile -v q -- 83blue
    if ($LASTEXITCODE -ne 0) { throw 'Live public API roundtrip failed' }
}
Write-Output 'YANZI_TEMP_TRANSFER_VERIFIED'

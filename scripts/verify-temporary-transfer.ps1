#Requires -Version 5.1
param([switch] $LiveSftp, [switch] $LivePublic)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$unit = Join-Path $root 'tests/TemporaryTransferVerification/TemporaryTransferVerification.csproj'
$hostproj = Join-Path $root 'src/OpenQuickHost/OpenQuickHost.csproj'
$integration = Join-Path $root 'tests/TemporaryTransferIntegration/TemporaryTransferIntegration.csproj'
# Contract check: production capability schema must allow backupCount and a list
# of verified fallback receipts, not just the in-memory handler implementation.
$providerPath = Join-Path $root 'src/OpenQuickHost/YanziTemporaryTransferCapabilityProvider.cs'
$providerText = [IO.File]::ReadAllText($providerPath)
function Read-CapabilitySchema([string]$name) {
    $match = [regex]::Match($providerText, 'private const string ' + $name +
        '\s*=\s*"""(?<json>.*?)""";', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) { throw "Schema declaration missing: $name" }
    return ($match.Groups['json'].Value | ConvertFrom-Json)
}
$published = Read-CapabilitySchema 'PublishSchema'
$received = Read-CapabilitySchema 'ReceiveSchema'
if ($published.properties.backupCount.type -ne 'integer' -or
    $published.properties.backupCount.minimum -ne 0 -or
    $published.properties.backupCount.maximum -ne 2 -or
    $received.properties.receipts.type -ne 'array' -or
    $received.properties.receipts.items.type -ne 'object' -or
    -not (@($received.required) -contains 'receipt')) {
    throw 'Temporary transfer capability input schema is inconsistent with the handlers'
}
Write-Output 'TEMPORARY_TRANSFER_SCHEMA_VERIFIED'

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

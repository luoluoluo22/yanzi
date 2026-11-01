#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & dotnet run --project tests/ExtensionDataIndexBatchVerification/ExtensionDataIndexBatchVerification.csproj -c Release --no-restore --nologo |
        Select-String 'PASS: (batch index|40 binary|all 40 binary|second unchanged|standalone|divergent|remote divergent|index failure)|EXTENSION_DATA_INDEX_BATCH_PASS'
    if ($LASTEXITCODE -ne 0) { throw "Linked-source batch verification failed: $LASTEXITCODE" }

    $recovery = Get-Content src/OpenQuickHost/ExtensionStorageService.Recovery.cs -Encoding UTF8 -Raw
    $service = Get-Content src/OpenQuickHost/Sync/PersonalSyncService.ExtensionDataCatalog.cs -Encoding UTF8 -Raw
    $preflight = $recovery.IndexOf('await service.PublishExtensionDataKeysAsync(extensionId, indexKeys')
    $batchCall = $recovery.IndexOf('await service.PublishPreIndexedExtensionBinaryAssetAsync(')
    if ($preflight -lt 0 -or $batchCall -lt $preflight) {
        throw 'Pre-indexed binary asset publish is not gated on full index publication'
    }
    foreach ($text in @(
        'var indexKeys = states.Where',
        '.Concat(localFiles.Keys).Distinct',
        'binaryKeyIndexReadsAvoided++',
        'binaryKeyIndexReadsAvoided={binaryKeyIndexReadsAvoided}'
    )) {
        if (-not $recovery.Contains($text)) { throw "Recovery contract missing: $text" }
    }
    foreach ($text in @(
        'public Task<bool> PublishExtensionBinaryAssetIfAbsentAsync(',
        'PublishExtensionBinaryAssetCoreAsync(extensionId, key, data, publishIndex: true',
        'internal Task<bool> PublishPreIndexedExtensionBinaryAssetAsync(',
        'PublishExtensionBinaryAssetCoreAsync(extensionId, key, data, publishIndex: false',
        'if (publishIndex)',
        'if (!BinaryAssetContentMatches(existing, data))'
    )) {
        if (-not $service.Contains($text)) { throw "Service contract missing: $text" }
    }
    Write-Output 'EXTENSION_INDEX_BATCH_INTEGRATION_PASS'
}
finally {
    Pop-Location
}

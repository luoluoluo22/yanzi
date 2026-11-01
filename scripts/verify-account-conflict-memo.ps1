#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & dotnet run --project tests/AccountExtensionConflictMemoVerification/AccountExtensionConflictMemoVerification.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "Account extension memo regression failed ($LASTEXITCODE)" }

    $main = Get-Content src/OpenQuickHost/MainWindow.CloudSync.cs -Raw -Encoding UTF8
    $required = @(
        '_accountExtensionConflictMemo.TryGetSameVersionDecision(',
        '_accountExtensionConflictMemo.RememberSameVersionDecision(',
        '_accountExtensionConflictMemo.Invalidate(segments[0]);',
        '_accountExtensionConflictMemo.ShouldLogConflict(',
        'IsAccountExtensionNewer(candidate, item)',
        'CalculateLocalAccountArchiveSha256(live, item)',
        'BackupExtensionBeforeCloudUpgrade(live, item.ArchiveSha256, currentHash)'
    )
    foreach ($needle in $required) {
        if (-not $main.Contains($needle)) { throw "Account conflict memo integration missing: $needle" }
    }
    $memo = Get-Content src/OpenQuickHost/Sync/AccountExtensionConflictMemo.cs -Raw -Encoding UTF8
    if (-not $memo.Contains('TimeSpan.FromMinutes(5)') -or
        -not $memo.Contains('generationBeforeHash != CaptureGeneration(extensionId)')) {
        throw 'Memo TTL or watcher generation safety missing'
    }
    Write-Output 'ACCOUNT_EXTENSION_CONFLICT_MEMO_CONTRACT_PASS'
}
finally {
    Pop-Location
}

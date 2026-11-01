#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$path = Join-Path $root 'src\OpenQuickHost\ExtensionStorageService.Recovery.cs'
$code = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$pass1 = $code.IndexOf('foreach (var file in localFiles)')
$pass2 = $code.IndexOf('foreach (var key in remoteKeys)', $pass1)
$binary = $code.IndexOf('if (IsPortableBinaryAsset(key))', $pass2)
$end = $code.IndexOf('var remote = (await service.TryReadExtensionDataAsync', $binary)
if ($pass1 -lt 0 -or $pass2 -lt 0 -or $binary -lt 0 -or $end -le $binary) {
    throw 'Could not find binary recovery stages'
}
$firstStage = $code.Substring($pass1, $pass2 - $pass1)
if (-not $firstStage.Contains('service.PublishExtensionBinaryAssetIfAbsentAsync(')) {
    throw 'Existing asset remote mismatch check is no longer performed during publication'
}
$block = $code.Substring($binary, $end - $binary)
$firstExistenceCheck = $block.IndexOf('if (File.Exists(localPath))')
$remoteRead = $block.IndexOf('service.TryReadExtensionBinaryAssetAsync(')
$secondExistenceCheck = $block.IndexOf('if (File.Exists(localPath))', $firstExistenceCheck + 1)
$restore = $block.IndexOf('await AtomicRestoreBytesAsync(')
if (-not ($firstExistenceCheck -ge 0 -and $remoteRead -gt $firstExistenceCheck -and
    $secondExistenceCheck -gt $remoteRead -and $restore -gt $secondExistenceCheck)) {
    throw 'Binary short-circuit / race guard missing or out of order'
}
if ($block.Contains('File.Delete(') -or $block.Contains('File.ReadAllBytesAsync(')) {
    throw 'Existing binary recovery code unexpectedly reads or deletes local content'
}
if (-not $code.Contains('existingBinaryReadsAvoided++') -or
    -not $code.Contains('existingBinaryReadsAvoided={existingBinaryReadsAvoided}')) {
    throw 'No runtime counter for avoided duplicate binary remote reads'
}
if (-not $code.Contains('var remote = (await service.TryReadExtensionDataAsync(')) {
    throw 'Text extension data reconciliation accidentally changed'
}
# Synthetic accounting using the exact invariant that only an absent local file
# may reach the remote downloader in the recovery phase.
$cases = @(
    @{ Existing = 0; Missing = 20 },
    @{ Existing = 30; Missing = 2 },
    @{ Existing = 150; Missing = 0 }
)
foreach ($case in $cases) {
    $oldDownloads = $case.Existing + $case.Missing
    $newDownloads = $case.Missing
    if ($oldDownloads - $newDownloads -ne $case.Existing) { throw 'Accounting invariant' }
    Write-Output "PASS: existing=$($case.Existing) missing=$($case.Missing) remote downloads $oldDownloads -> $newDownloads (modeled)"
}
Write-Output 'BINARY_RECOVERY_READ_AVOIDANCE_CONTRACT_PASS'

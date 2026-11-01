#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & dotnet run --project tests/GiteeReadFlightVerification/GiteeReadFlightVerification.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "Gitee singleflight concurrency tests failed ($LASTEXITCODE)" }

    $backends = Get-Content src/OpenQuickHost/Sync/PersonalSyncBackends.cs -Encoding UTF8 -Raw
    $required = @(
        'GiteeReadFlightCoordinator.ReadAsync(',
        'ReadDirectBytesCoreAsync(path, owner, CancellationToken.None)',
        'GiteeReadFlightCoordinator.Invalidate(flightKey);',
        'CloudSyncDiagnostics.Log("GiteeBackend", "Read requested"'
    )
    foreach ($needle in $required) {
        if (-not $backends.Contains($needle)) { throw "Gitee backend integration missing: $needle" }
    }
    $coordinator = Get-Content src/OpenQuickHost/Sync/GiteeReadFlightCoordinator.cs -Encoding UTF8 -Raw
    if (-not $coordinator.Contains('return result?.ToArray();')) { throw 'Read result isolation missing' }
    if ($coordinator.Contains('TimeSpan.FromMinutes(') -or $coordinator.Contains('MemoryCache')) {
        throw 'Unexpected stale cache introduced'
    }
    Write-Output 'GITEE_READ_SINGLEFLIGHT_CONTRACT_PASS'
}
finally {
    Pop-Location
}

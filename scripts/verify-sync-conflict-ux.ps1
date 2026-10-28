$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repositoryRoot
try {
    & dotnet build 'src/OpenQuickHost/OpenQuickHost.csproj' -c Release --no-restore -p:TreatWarningsAsErrors=false -v:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Desktop host build failed.' }
    & dotnet run --project 'src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj' -c Release --no-restore -- --sync-ux-safety
    if ($LASTEXITCODE -ne 0) { throw 'Sync conflict UX verification failed.' }
    Write-Output 'SYNC_CONFLICT_UX_VERIFIED'
}
finally {
    Pop-Location
}

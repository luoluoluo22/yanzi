#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    dotnet run --project tests/SearchUsageMemoryVerification -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Search usage caching and persistence regression failed.' }
    dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Windows host build failed.' }
    dotnet run --project tests/DesktopPerformanceVerification -c Debug -- --dev
    if ($LASTEXITCODE -ne 0) { throw 'Desktop performance regression failed.' }
    Write-Output 'SEARCH_USAGE_PERFORMANCE_VERIFIED'
}
finally { Pop-Location }

#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $output = @(& dotnet run --project tests/SearchHotpathVerification -c Release)
    if ($LASTEXITCODE -ne 0 -or $output -notcontains 'SEARCH_HOTPATH_VERIFIED') {
        throw 'Search cache and pinned scope regression failed.'
    }
    dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Windows host build failed.' }
    $desktop = @(& dotnet run --project tests/DesktopPerformanceVerification -c Debug -- --dev)
    if ($LASTEXITCODE -ne 0 -or $desktop -notcontains 'PASS targeted running lookup cleans expired entry') {
        throw 'Existing desktop regression failed or ended before startup assertions.'
    }
    Write-Output 'SEARCH_HOTPATH_INTEGRATION_VERIFIED'
}
finally { Pop-Location }

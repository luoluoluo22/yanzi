#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $result = @(& dotnet run --project tests/DesktopPerformanceVerification -c Debug -- --dev)
    if ($LASTEXITCODE -ne 0) { throw 'Desktop regression exited nonzero.' }
    $joined = $result -join [Environment]::NewLine
    foreach ($expected in @(
        'PASS targeted running lookup detects active extension',
        'PASS targeted running lookup rejects missing extension',
        'PASS running lookup does not probe unrelated managed extensions',
        'PASS targeted running lookup rejects expired extension',
        'PASS targeted running lookup cleans expired entry'
    )) {
        if (!$joined.Contains($expected)) { throw "Missing actual assertion: $expected" }
    }
    dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Windows host build failed.' }
    Write-Output 'RUNNING_STATE_LOOKUP_VERIFIED'
}
finally { Pop-Location }

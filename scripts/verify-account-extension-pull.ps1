$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    & dotnet run --project 'src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj' -c Debug -- --account-extension-pull-policy
    if ($LASTEXITCODE -ne 0) { throw 'Account extension pull protection regression failed.' }
    Write-Output 'ACCOUNT_EXTENSION_PULL_VERIFICATION_OK'
}
finally {
    Pop-Location
}

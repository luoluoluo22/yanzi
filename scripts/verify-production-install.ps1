#Requires -Version 5.1
param(
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [Parameter(Mandatory=$true)][string]$ExpectedDllSha256
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Yanzi\current'
$exe = Join-Path $root 'Yanzi.exe'
$dll = Join-Path $root 'Yanzi.dll'
foreach($file in @($exe,$dll)) { if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing production binary: $file"} }
$exeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
$version = if($ExpectedVersion -match '^\d+\.\d+\.\d+$') { "$ExpectedVersion.0" } else { $ExpectedVersion }
$sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$running = @(Get-CimInstance Win32_Process -Filter "Name='Yanzi.exe'" |
    Where-Object { [string]::Equals($_.ExecutablePath,$exe,[StringComparison]::OrdinalIgnoreCase) })
$evidence = [pscustomobject]@{
    host = $env:COMPUTERNAME
    installedPath = $exe
    expectedVersion = $version
    exeVersion = $exeVersion
    dllVersion = $dllVersion
    dllSha256 = $sha256
    runningPids = @($running | ForEach-Object { $_.ProcessId })
    isValid = (($exeVersion -eq $version) -and ($dllVersion -eq $version) -and
        ($sha256 -ieq $ExpectedDllSha256) -and ($running.Count -eq 1))
}
$evidence | ConvertTo-Json -Depth 4
if(-not $evidence.isValid) {throw "PRODUCTION_INSTALL_VERIFICATION_FAILED ($($env:COMPUTERNAME))"}
Write-Output "PRODUCTION_INSTALL_VERIFIED"

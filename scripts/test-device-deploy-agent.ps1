#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$files = @(
    (Join-Path $PSScriptRoot 'device-deploy-agent.ps1'),
    (Join-Path $PSScriptRoot 'install-device-deploy-agent.ps1'),
    (Join-Path $PSScriptRoot 'deploy-to-devices.ps1')
)
foreach($file in $files) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors) | Out-Null
    if(@($errors).Count -gt 0) { throw "PowerShell parser errors in $file : $errors" }
}
$agent = $files[0]
$result = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $agent -Mode check 2>&1)
if($LASTEXITCODE -ne 0) { throw "Safe check failed: $result" }
if(-not (@($result | Where-Object {$_ -match '^DEPLOY_(CURRENT|AVAILABLE)='}).Count -eq 1)) {
    throw "Safe version check didn't return stable status: $result"
}
$result2 = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $agent -Mode check 2>&1)
if($LASTEXITCODE -ne 0) { throw "Repeat check failed: $result2" }
$details = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $agent -Mode status | Out-String) | ConvertFrom-Json
if(-not $details.machine -or $details.state -notin @('current','available')) {
    throw 'Expected machine state after repeat safe check'
}
Write-Output "DEVICE_DEPLOY_TEST=PASSED; parse=3; repeatCheck=2; machine=$($details.machine); state=$($details.state)"

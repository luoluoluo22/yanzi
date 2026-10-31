#Requires -Version 5.1
# Schedule a separate, non-invasive private receipt publisher every two minutes.
param(
    [string]$Source = (Join-Path $PSScriptRoot 'device-deploy-receipt.ps1'),
    [ValidateRange(1,60)][int]$IntervalMinutes = 2,
    [switch]$SkipInitialReport
)
$ErrorActionPreference='Stop'
$destDir=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent'
$dest=Join-Path $destDir 'device-deploy-receipt.ps1'
New-Item -ItemType Directory -Path $destDir -Force | Out-Null
if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {throw 'Receipt script not found'}
$sourceFull=[IO.Path]::GetFullPath($Source)
if (-not [string]::Equals($sourceFull,[IO.Path]::GetFullPath($dest),
    [StringComparison]::OrdinalIgnoreCase)) {Copy-Item -LiteralPath $Source -Destination $dest -Force}
$task='Yanzi-Deployment-Receipt'
$action='powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$dest+'"'
& schtasks.exe /Create /F /TN $task /SC MINUTE /MO $IntervalMinutes /RL LIMITED /TR $action | Out-Null
if ($LASTEXITCODE -ne 0) {throw 'Unable to register deployment receipt task'}
& schtasks.exe /Query /TN $task /FO LIST | Out-Null
if ($LASTEXITCODE -ne 0) {throw 'Receipt scheduled task verification failed'}
if (-not $SkipInitialReport) {
    & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $dest
    if ($LASTEXITCODE -ne 0) {throw 'Initial receipt upload failed'}
}
Write-Output "RECEIPT_TASK_INSTALLED=$task"

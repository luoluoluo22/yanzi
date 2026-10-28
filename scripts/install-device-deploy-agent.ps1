#Requires -Version 5.1
# Install the cross-device receiver as a Windows per-user scheduled task.
param(
    [string] $AgentSource = (Join-Path $PSScriptRoot 'device-deploy-agent.ps1'),
    [ValidateRange(2,60)][int] $IntervalMinutes = 3,
    [switch] $SkipInitialCheck
)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent'
$installed = Join-Path $base 'device-deploy-agent.ps1'
$task = 'Yanzi-CrossDevice-Deployment'
New-Item -ItemType Directory -Path $base -Force | Out-Null
if (-not (Test-Path -LiteralPath $AgentSource)) { throw "Missing agent: $AgentSource" }
$src = [IO.Path]::GetFullPath($AgentSource)
if (-not [string]::Equals($src,[IO.Path]::GetFullPath($installed),[StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $src -Destination $installed -Force
}
# Source is generated within the trusted Yanzi repo / its versioned installed file.
$command = 'powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$installed+'" -Mode apply'
& schtasks.exe /Create /F /TN $task /SC MINUTE /MO $IntervalMinutes /RL LIMITED /TR $command | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to register $task (exit=$LASTEXITCODE)" }
& schtasks.exe /Query /TN $task /FO LIST | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Task registration verification failed: $task" }
Write-Output "AGENT_INSTALLED=$installed"
Write-Output "TASK_NAME=$task"
Write-Output "INTERVAL_MINUTES=$IntervalMinutes"
if (-not $SkipInitialCheck) {
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $installed -Mode check
    if ($LASTEXITCODE -ne 0) { throw 'Initial safe version check failed' }
}
Write-Output 'DEPLOY_AGENT_READY=1'

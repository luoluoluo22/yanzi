param(
  [ValidateSet('full','check','test','http','status','cancel')]
  [string]$Action = 'full',
  [string]$RunId,
  [switch]$Wait
)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
Push-Location $projectDir
try {
  $arguments = @((Join-Path $PSScriptRoot 'dev-pipeline-cli.js'), $Action)
  if ($RunId) { $arguments += @('--run-id', $RunId) }
  if ($Wait) { $arguments += '--wait' }
  & node @arguments
  if ($LASTEXITCODE -ne 0) { throw "Development pipeline failed (exit $LASTEXITCODE)." }
} finally { Pop-Location }

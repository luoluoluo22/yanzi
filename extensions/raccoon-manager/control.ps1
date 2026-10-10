param([ValidateSet('start','restart')][string]$Action='start')
$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'managed-start.ps1'
& $script -Action $Action
if($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
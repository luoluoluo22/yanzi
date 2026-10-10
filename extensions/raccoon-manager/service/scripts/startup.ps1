param([switch]$Recover)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$runtimeDir = if ($env:RACCOON_RUNTIME_DIR) { $env:RACCOON_RUNTIME_DIR } else { Join-Path $projectDir '.raccoon-runtime' }
try {
  if ($Recover -and (Test-Path -LiteralPath (Join-Path $runtimeDir 'service-paused'))) { exit 0 }
  & (Join-Path $PSScriptRoot 'local.ps1') start
  $useCloudflare = (Get-Content -LiteralPath (Join-Path $runtimeDir '.env')) -match '^RACCOON_TUNNEL_PROVIDER=cloudflare\s*$'
  if ($useCloudflare -and -not $env:RACCOON_TUNNEL_ALREADY_ACTIVE) {
    if (-not ($Recover -and (Test-Path -LiteralPath (Join-Path $runtimeDir 'tunnel-paused')))) {
      & (Join-Path $PSScriptRoot 'cloudflare.ps1') start -Recover:$Recover
    }
  } elseif ((Test-Path -LiteralPath (Join-Path $runtimeDir 'tunnel.json')) -and (Test-Path -LiteralPath (Join-Path $runtimeDir 'openai-runtime-key.txt'))) {
    & (Join-Path $PSScriptRoot 'tunnel.ps1') connect
  }
} catch {
  New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
  "$(Get-Date -Format o) $($_.Exception.Message)" | Add-Content -LiteralPath (Join-Path $runtimeDir 'startup.log')
  exit 1
} finally {
  Push-Location $projectDir
  try { & node (Join-Path $PSScriptRoot 'connection-status.js') | Out-Null }
  finally { Pop-Location }
}

param(
  [ValidateSet('install','connect','status','stop')][string]$Action = 'status',
  [string]$TunnelId,
  [string]$KeyFile
)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$runtimeDir = if ($env:RACCOON_RUNTIME_DIR) { $env:RACCOON_RUNTIME_DIR } else { Join-Path $projectDir '.raccoon-runtime' }
$toolsDir = Join-Path $runtimeDir 'tools'
$binary = Join-Path $toolsDir 'tunnel-client\tunnel-client.exe'
if ($Action -eq 'install') {
  New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
  $release = Invoke-RestMethod 'https://api.github.com/repos/openai/tunnel-client/releases/latest'
  $asset = $release.assets | Where-Object { $_.name -match '^tunnel-client-v.*-windows-amd64.zip$' }
  if (-not $asset) { throw 'Official release has no Windows x64 asset.' }
  $zipFile = Join-Path $toolsDir 'tunnel-client.zip'
  $hashFile = Join-Path $toolsDir 'SHA256SUMS.txt'
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipFile -UseBasicParsing
  $hashAsset = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' }
  Invoke-WebRequest -Uri $hashAsset.browser_download_url -OutFile $hashFile -UseBasicParsing
  $actual = (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash.ToLowerInvariant()
  $expected = Get-Content -LiteralPath $hashFile | Where-Object { $_.EndsWith($asset.name) }
  if (-not $expected -or -not $expected.StartsWith($actual)) { throw 'SHA-256 verification failed.' }
  Expand-Archive -LiteralPath $zipFile -DestinationPath (Split-Path -Parent $binary) -Force
  Write-Output "Installed official tunnel-client $($release.tag_name), SHA-256 verified."
  exit 0
}
if (-not (Test-Path -LiteralPath $binary)) { throw 'Run scripts/tunnel.ps1 install first.' }
if ($Action -eq 'status' -or $Action -eq 'stop') {
  if ($Action -eq 'stop') { & $binary runtimes stop raccoon-local --json; exit $LASTEXITCODE }
  $state = & $binary runtimes status raccoon-local --json | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect tunnel runtime.' }
  $state | Select-Object tunnel_id,process_running,healthy,ready,runtime_state,remote_error,ui_url | ConvertTo-Json
  if (-not ($state.process_running -and $state.healthy -and $state.ready)) { exit 1 }
  exit 0
}
if (-not $TunnelId) {
  $savedConfig = Join-Path $runtimeDir 'tunnel.json'
  if (Test-Path -LiteralPath $savedConfig) { $TunnelId = (Get-Content -LiteralPath $savedConfig -Raw | ConvertFrom-Json).tunnelId }
}
if (-not $TunnelId -or $TunnelId -notmatch '^tunnel_[a-zA-Z0-9]+$') { throw 'Supply the tunnel ID from OpenAI Platform.' }
if (-not $KeyFile) { $KeyFile = Join-Path $runtimeDir 'openai-runtime-key.txt' }
if (-not (Test-Path -LiteralPath $KeyFile)) { throw "Save your runtime API key locally at $KeyFile (do not send it in chat)." }
$keyPath = (Resolve-Path -LiteralPath $KeyFile).Path
$keyValue = [System.IO.File]::ReadAllText($keyPath).Trim()
if ($keyValue.Length -lt 20 -or $keyValue -match '\s') { throw 'The runtime key file must contain only the API key.' }
$keyValue = $null
$envFile = Join-Path $runtimeDir '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Start the local service first.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envFile) {
  if ($line -match '^([A-Z_]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
$port = if ($settings.ContainsKey('RACCOON_PORT')) { $settings['RACCOON_PORT'] } else { '3766' }
$endpoint = "http://127.0.0.1:$port/mcp"
if (-not $settings['RACCOON_TOKEN']) { throw 'Local HTTP token is required for this setup.' }
$env:MCP_EXTRA_HEADERS = "Authorization: env:RACCOON_AUTHORIZATION"
$env:MCP_DISCOVERY_EXTRA_HEADERS = "Authorization: env:RACCOON_AUTHORIZATION"
$env:RACCOON_AUTHORIZATION = "Bearer $($settings['RACCOON_TOKEN'])"
try {
  & $binary doctor --control-plane.tunnel-id $TunnelId --control-plane.api-key "file:$keyPath" --mcp.server-url $endpoint --health.listen-addr '127.0.0.1:0' --explain
  if ($LASTEXITCODE -ne 0) { throw 'Tunnel checks failed; fix account permission/network/configuration before connecting.' }
  $connection = & $binary runtimes connect --alias raccoon-local --tunnel-id $TunnelId --mcp-server-url $endpoint --runtime-api-key "file:$keyPath" --profile-dir (Join-Path $runtimeDir 'profiles') --json | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw 'Tunnel runtime failed to start.' }
  $state = & $binary runtimes status raccoon-local --json | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect started tunnel runtime.' }
  $state | Select-Object tunnel_id,process_running,healthy,ready,runtime_state,remote_error,ui_url | ConvertTo-Json
  if (-not ($state.process_running -and $state.healthy -and $state.ready)) { throw 'Tunnel is running but is not healthy and ready yet.' }
} finally {
  Remove-Item Env:RACCOON_AUTHORIZATION -ErrorAction SilentlyContinue
  Remove-Item Env:MCP_EXTRA_HEADERS -ErrorAction SilentlyContinue
  Remove-Item Env:MCP_DISCOVERY_EXTRA_HEADERS -ErrorAction SilentlyContinue
}

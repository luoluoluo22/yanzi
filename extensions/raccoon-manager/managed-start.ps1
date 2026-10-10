param(
    [ValidateSet('start', 'restart', 'status')][string]$Action = 'start',
    [switch]$Recover
)
$ErrorActionPreference = 'Stop'
$extension = $PSScriptRoot
$service = & (Join-Path $extension 'stage-service.ps1')
$runtime = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\McpRuntime\raccoon'
New-Item -Path $runtime -ItemType Directory -Force | Out-Null

$env:RACCOON_RUNTIME_DIR = $runtime
$env:RACCOON_ENV_FILE = Join-Path $runtime '.env'
$env:RACCOON_SETTINGS_FILE = Join-Path $runtime 'settings.json'
$env:RACCOON_SECRET_FILE = Join-Path $runtime 'secrets.json'
$env:RACCOON_OAUTH_STATE_FILE = Join-Path $runtime 'oauth-state.json'
$env:RACCOON_SOURCE_BACKUP_DIR = Join-Path $runtime 'source-backups'
$env:RACCOON_PIPELINE_DIR = Join-Path $runtime 'pipelines'

function Get-Health([int]$Port) {
    try {
        return Invoke-RestMethod -Uri ("http://127.0.0.1:$Port/health") -TimeoutSec 2
    } catch { return $null }
}

function Test-PortOccupied([int]$Port) {
    # Query the kernel's actual LISTEN sockets. Connect timeouts may report a
    # false positive for every candidate port on some Windows configurations.
    $listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    foreach ($endpoint in $listeners) {
        if ($endpoint.Port -eq $Port) { return $true }
    }
    return $false
}

function Find-FreeRaccoonPort {
    foreach ($candidate in 3767..3799) {
        if (-not (Test-PortOccupied $candidate)) { return $candidate }
    }
    throw 'The Raccoon port range (3767-3799) is occupied. Cannot provision MCP safely.'
}

$envFile = $env:RACCOON_ENV_FILE
if (!(Test-Path -LiteralPath $envFile)) {
    $port = if (Test-PortOccupied 3766) { Find-FreeRaccoonPort } else { 3766 }
    $secretBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($secretBytes) }
    finally { $rng.Dispose() }
    $secret = [Convert]::ToBase64String($secretBytes)
    @(
        "RACCOON_ROOT=$env:USERPROFILE",
        'RACCOON_TRANSPORT=http',
        'RACCOON_HOST=127.0.0.1',
        "RACCOON_PORT=$port",
        "RACCOON_TOKEN=$secret",
        'RACCOON_ENABLE_SHELL=0',
        'RACCOON_READ_ONLY=0',
        "RACCOON_AUDIT_LOG=$runtime\audit.jsonl"
    ) | Set-Content -LiteralPath $envFile -Encoding UTF8
}

# Never mistake a different MCP's /health response for the Raccoon service.
$entries = @(Get-Content -LiteralPath $envFile -Encoding UTF8)
$portSetting = @($entries | Where-Object { $_ -match '^RACCOON_PORT=\d+\s*$' } | Select-Object -First 1)
$port = 3766
if ($portSetting.Count -gt 0) {
    $parsedPort = [int](($portSetting[0] -split '=', 2)[1].Trim())
    if ($parsedPort -lt 1 -or $parsedPort -gt 65535) { throw 'Invalid RACCOON_PORT in .env.' }
    $port = $parsedPort
}
$health = Get-Health $port
$ours = $health -and $health.ok -eq $true -and $health.name -eq 'raccoon-mcp'

if (!$ours -and (Test-PortOccupied $port)) {
    # Existing published tunnels may require a fixed port; do not silently invalidate them.
    $tunnelConfigured = @($entries | Where-Object {
        $_ -match '^RACCOON_TUNNEL_PROVIDER=\S+' -or
        ($_ -match '^RACCOON_PUBLIC_URL=(.+)$' -and $Matches[1].Trim().Length -gt 0)
    }).Count -gt 0
    if ($tunnelConfigured) {
        throw "Port $port is occupied by another service; change the configured tunnel and port explicitly."
    }

    $newPort = Find-FreeRaccoonPort
    $changed = @($entries | Where-Object { $_ -notmatch '^RACCOON_PORT=' }) + "RACCOON_PORT=$newPort"
    $scratch = "$envFile.port-update-$PID"
    try {
        $changed | Set-Content -LiteralPath $scratch -Encoding UTF8
        Move-Item -LiteralPath $scratch -Destination $envFile -Force
    } finally {
        Remove-Item -LiteralPath $scratch -Force -ErrorAction SilentlyContinue
    }
    Write-Output "Port $port was occupied; Raccoon will use 127.0.0.1:$newPort."
    $port = $newPort
    $ours = $false
}

if ($Action -eq 'status') {
    & (Join-Path $service 'scripts\local.ps1') status
    exit $LASTEXITCODE
}

if ($Action -eq 'restart') {
    & (Join-Path $service 'scripts\local.ps1') stop
    $ours = $false
}
if ($ours) {
    Write-Output 'Existing Raccoon MCP service healthy; avoiding duplicate port binding.'
    exit 0
}

$legacyTunnel = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'cloudflared.exe' -and $_.CommandLine -match 'raccoon-mcp' -and $_.CommandLine -match 'token-file'
})
if ($legacyTunnel.Count -gt 0) { $env:RACCOON_TUNNEL_ALREADY_ACTIVE = '1' }

& (Join-Path $service 'scripts\startup.ps1') -Recover:$Recover
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

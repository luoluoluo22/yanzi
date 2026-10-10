param([ValidateSet('start','restart','status')][string]$Action='start',[switch]$Recover)
$ErrorActionPreference = 'Stop'
$extension = Split-Path -Parent $MyInvocation.MyCommand.Path
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
if (!(Test-Path $env:RACCOON_ENV_FILE)) {
    $secret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    @("RACCOON_ROOT=$env:USERPROFILE",'RACCOON_TRANSPORT=http','RACCOON_HOST=127.0.0.1','RACCOON_PORT=3766',"RACCOON_TOKEN=$secret",'RACCOON_ENABLE_SHELL=0','RACCOON_READ_ONLY=0',"RACCOON_AUDIT_LOG=$runtime\audit.jsonl") |
        Set-Content $env:RACCOON_ENV_FILE -Encoding UTF8
}
if ($Action -eq 'status') { & (Join-Path $service 'scripts\local.ps1') status; exit $LASTEXITCODE }
if ($Action -eq 'restart') { & (Join-Path $service 'scripts\local.ps1') stop }
# Respect an already-running original service until a maintenance cutover.
try {
  $h = Invoke-RestMethod -Uri 'http://127.0.0.1:3766/health' -TimeoutSec 2
  if ($h.ok) { Write-Output 'Existing Raccoon service healthy; avoiding duplicate port binding.'; exit 0 }
} catch {}
$legacyTunnel = @(Get-CimInstance Win32_Process | Where-Object {
  $_.Name -eq 'cloudflared.exe' -and $_.CommandLine -match 'raccoon-mcp' -and $_.CommandLine -match 'token-file'
})
if($legacyTunnel.Count -gt 0){$env:RACCOON_TUNNEL_ALREADY_ACTIVE='1'}
& (Join-Path $service 'scripts\startup.ps1') -Recover:$Recover
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
param([string]$InputText = '', [string]$ContextPath = '')
$ErrorActionPreference = 'Stop'
$bridgeOrigin = 'http://127.0.0.1:53921'
$bridgeDirectory = $PSScriptRoot
$bridgeData = Join-Path $env:LOCALAPPDATA 'OpenQuickHost/ExtensionStorage/chatgpt-bridge'
New-Item -ItemType Directory -Path $bridgeData -Force | Out-Null
$healthy = $false
try {
    $health = Invoke-RestMethod "$bridgeOrigin/health" -TimeoutSec 2
    if ($health.service -ne 'yanzi-chatgpt-bridge') { throw 'Port 53921 is occupied by another service.' }
    $healthy = $true
} catch { }
if (-not $healthy) {
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    Start-Process -FilePath $nodePath -ArgumentList @('"' + (Join-Path $bridgeDirectory 'server.mjs') + '"') -WorkingDirectory $bridgeDirectory -WindowStyle Hidden -RedirectStandardOutput (Join-Path $bridgeData 'service.stdout.log') -RedirectStandardError (Join-Path $bridgeData 'service.stderr.log') | Out-Null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        try { $health = Invoke-RestMethod "$bridgeOrigin/health" -TimeoutSec 1; if ($health.service -eq 'yanzi-chatgpt-bridge') { $healthy = $true; break } } catch { }
    }
    if (-not $healthy) { throw 'ChatGPT bridge did not start. See service.stderr.log in the extension storage directory.' }
}
if ($env:YANZI_LAUNCH_SOURCE -notmatch 'startup|app_launch') {
    Start-Process $bridgeOrigin -WindowStyle Hidden
}
Write-Output 'ChatGPT bridge is running at http://127.0.0.1:53921'

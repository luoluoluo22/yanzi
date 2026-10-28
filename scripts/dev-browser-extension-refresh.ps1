param(
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = "Stop"

$settingsPath = Join-Path $env:LOCALAPPDATA "OpenQuickHost\appsettings.local.json"
if (-not (Test-Path $settingsPath)) {
    throw "Yanzi settings not found: $settingsPath"
}

$settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$port = [int]($settings.agentApiPort)
if ($port -le 0) { $port = 53919 }

$token = [string]$settings.agentApiToken
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Local Agent API token is not configured."
}

$baseUrl = "http://127.0.0.1:$port"
$headers = @{ "X-Yanzi-Token" = $token }
$deadline = (Get-Date).AddSeconds([Math]::Max(1, $WaitSeconds))

$status = $null
do {
    try {
        $status = Invoke-RestMethod -UseBasicParsing -Uri "$baseUrl/v1/browser/status" -Headers $headers -Method Get -TimeoutSec 3
        if ($status.extensionConnected) { break }
    }
    catch {
        $status = $null
    }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

if (-not $status) {
    throw "Local Agent API is unavailable at $baseUrl."
}
if (-not $status.extensionConnected) {
    throw "Browser extension is not connected. State: $($status.state)"
}

$response = Invoke-RestMethod -UseBasicParsing -Uri "$baseUrl/v1/browser/reload" -Headers $headers -Method Post -TimeoutSec 10
if (-not $response.ok -or $response.status -ne "accepted") {
    throw "Browser extension reload request was not accepted."
}

Write-Output ("reloadAccepted requestId={0} browser={1}" -f $response.requestId, $response.browser)

$reconnected = $false
$deadline = (Get-Date).AddSeconds([Math]::Max(1, $WaitSeconds))
do {
    Start-Sleep -Milliseconds 500
    try {
        $status = Invoke-RestMethod -UseBasicParsing -Uri "$baseUrl/v1/browser/status" -Headers $headers -Method Get -TimeoutSec 3
        if ($status.extensionConnected) {
            $reconnected = $true
            break
        }
    }
    catch {}
} while ((Get-Date) -lt $deadline)

if (-not $reconnected) {
    throw "Browser extension did not reconnect after reload."
}

Write-Output ("extensionReconnected browser={0} state={1}" -f $status.connectedBrowser, $status.state)

param(
    [string]$ExistingRoot = "$env:LOCALAPPDATA\OpenQuickHost",
    [int]$OnlineWindowSeconds = 120,
    [switch]$RequireOnlineDesktop
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sessionPath = Join-Path $ExistingRoot "syncsession.json"
$settingsPath = Join-Path $ExistingRoot "syncsettings.json"

if (-not (Test-Path $sessionPath)) {
    throw "Sync session was not found: $sessionPath"
}
if (-not (Test-Path $settingsPath)) {
    throw "Sync settings were not found: $settingsPath"
}

$session = Get-Content $sessionPath -Raw -Encoding UTF8 | ConvertFrom-Json
$settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$token = [string]$session.accessToken
$baseUrl = ([string]$settings.baseUrl).TrimEnd("/")

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Sync access token is missing."
}
if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    throw "Sync base URL is missing."
}

Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false
$client = New-Object System.Net.Http.HttpClient($handler)

try {
    $client.Timeout = [TimeSpan]::FromSeconds(20)
    $client.DefaultRequestHeaders.Authorization =
        New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $token)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("YanziClient-Desktop/device-presence-test")
    $client.DefaultRequestHeaders.Add("X-Yanzi-Client", "desktop")

    $response = $client.GetAsync("$baseUrl/v1/me/devices").GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        throw "GET /v1/me/devices failed with HTTP $([int]$response.StatusCode)."
    }

    $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $serverNowText = [string]$payload.serverNow
    if ([string]::IsNullOrWhiteSpace($serverNowText)) {
        throw "Device presence response is missing serverNow."
    }

    $serverNow = [DateTimeOffset]::Parse($serverNowText)
    $items = @($payload.items)
    $desktopItems = @($items | Where-Object { [string]$_.platform -eq "desktop" })

    foreach ($item in $items) {
        if ($null -eq $item.PSObject.Properties["online"]) {
            throw "Device presence response is missing the online field."
        }

        $lastSeenAt = [DateTimeOffset]::Parse([string]$item.lastSeenAt)
        $ageMs = [Math]::Max(
            0.0,
            ($serverNow.ToUniversalTime() - $lastSeenAt.ToUniversalTime()).TotalMilliseconds)
        $expectedOnline = $ageMs -le ($OnlineWindowSeconds * 1000)
        if ([bool]$item.online -ne $expectedOnline) {
            throw "Device online state does not match serverNow/lastSeenAt."
        }
    }

    if ($RequireOnlineDesktop -and -not @($desktopItems | Where-Object { [bool]$_.online }).Count) {
        throw "No online desktop device was reported."
    }

    Write-Host ("DEVICE_PRESENCE_CONTRACT=PASSED")
    Write-Host ("SERVER_NOW_PRESENT=True")
    Write-Host ("DEVICE_COUNT=" + $items.Count)
    Write-Host ("DESKTOP_COUNT=" + $desktopItems.Count)
    Write-Host ("ONLINE_DESKTOP_COUNT=" + @($desktopItems | Where-Object { [bool]$_.online }).Count)
}
finally {
    $client.Dispose()
    $handler.Dispose()
}

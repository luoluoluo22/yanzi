param(
    [string]$TargetDeviceId = "android-f85961cb-a306-46aa-8125-33f9d8614a7e",
    [string]$Text = "",
    [string]$TextFile = "",
    [string]$ConversationId = ""
)

if ($TextFile -and (Test-Path $TextFile)) {
    $Text = [System.IO.File]::ReadAllText($TextFile, [System.Text.Encoding]::UTF8)
}

$ErrorActionPreference = "Stop"

$settingsPath = Join-Path $env:LOCALAPPDATA "OpenQuickHost\appsettings.local.json"
$settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$port = [int]$settings.agentApiPort
$token = [string]$settings.agentApiToken

$url = "http://127.0.0.1:$port/v1/capabilities/invoke"
$headers = @{
    "X-Yanzi-Token" = $token
    "Authorization" = "Bearer $token"
}

$payload = [ordered]@{
    target = $TargetDeviceId
    text = $Text
    requestId = "agy-" + [Guid]::NewGuid().ToString("N")
    waitForAck = $false
}
if ($ConversationId) {
    $payload["conversationId"] = $ConversationId
}

$requestObj = [ordered]@{
    name = "chat.send"
    payload = $payload
}

$json = ConvertTo-Json -InputObject $requestObj -Depth 5
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

$response = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -ContentType "application/json; charset=utf-8" -Body $bytes
$response | ConvertTo-Json -Depth 5

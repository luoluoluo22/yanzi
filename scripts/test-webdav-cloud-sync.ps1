param(
    [string]$DataRoot = "$env:LOCALAPPDATA\OpenQuickHost",
    [string]$ConfigId = "yanzi-webdav-settings"
)

$ErrorActionPreference = "Stop"

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$BaseUrl,
        [string]$Path,
        [string]$Token
    )

    $headers = @{
        Authorization = "Bearer $Token"
    }

    return Invoke-RestMethod -Method $Method -Uri ($BaseUrl.TrimEnd('/') + $Path) -Headers $headers
}

$syncSettingsPath = Join-Path $DataRoot "syncsettings.json"
$sessionPath = Join-Path $DataRoot "syncsession.json"

$syncSettings = Read-JsonFile -Path $syncSettingsPath
if ($null -eq $syncSettings -or [string]::IsNullOrWhiteSpace($syncSettings.baseUrl)) {
    throw "Missing valid syncsettings.json: $syncSettingsPath"
}

$session = Read-JsonFile -Path $sessionPath
if ($null -eq $session -or [string]::IsNullOrWhiteSpace($session.accessToken)) {
    throw "Missing valid syncsession.json: $sessionPath"
}

$baseUrl = $syncSettings.baseUrl
$token = $session.accessToken

Write-Host "== Local Files =="
Write-Host "DataRoot: $DataRoot"
Write-Host "BaseUrl : $baseUrl"
Write-Host "Session : $sessionPath"
Write-Host ""

Write-Host "== Local Session =="
$session | ConvertTo-Json -Depth 6
Write-Host ""

Write-Host "== /v1/auth/me =="
$me = Invoke-Api -Method "GET" -BaseUrl $baseUrl -Path "/v1/auth/me" -Token $token
$me | ConvertTo-Json -Depth 6
Write-Host ""

Write-Host "== /v1/me/extensions =="
$userExtensions = Invoke-Api -Method "GET" -BaseUrl $baseUrl -Path "/v1/me/extensions" -Token $token
$userExtensions | ConvertTo-Json -Depth 8
Write-Host ""

$configRecord = $null
if ($null -ne $userExtensions.items) {
    $configRecord = $userExtensions.items | Where-Object { $_.extension_id -eq $ConfigId } | Select-Object -First 1
}

Write-Host "== Filtered Config Record ($ConfigId) =="
if ($null -eq $configRecord) {
    Write-Host "Config record not found."
}
else {
    $configRecord | ConvertTo-Json -Depth 8
}
Write-Host ""

Write-Host "== /v1/sync/webdav-config =="
try {
    $webDavConfig = Invoke-Api -Method "GET" -BaseUrl $baseUrl -Path "/v1/sync/webdav-config" -Token $token
    $webDavConfig | ConvertTo-Json -Depth 8
}
catch {
    Write-Host "Request failed: $($_.Exception.Message)"
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message
    }
}

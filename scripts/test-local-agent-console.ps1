# Local console authentication tests; no raw token or cookie is logged.
$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw | ConvertFrom-Json
$base = "http://127.0.0.1:$($settings.agentApiPort)"
$token = [string]$settings.agentApiToken
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Native API token is missing.' }

function Expect-HttpFailure([scriptblock]$Action, [int]$ExpectedStatus) {
    try { & $Action | Out-Null; throw "Expected HTTP $ExpectedStatus, but request succeeded." }
    catch [System.Net.WebException] {
        $status = [int]$_.Exception.Response.StatusCode
        if ($status -ne $ExpectedStatus) { throw "Expected HTTP $ExpectedStatus, got $status" }
        return $status
    }
}

$unauthorized = Expect-HttpFailure {
    Invoke-WebRequest -UseBasicParsing -Uri "$base/v1/console/session" -TimeoutSec 8
} 401
Write-Output "Unauthenticated session: HTTP $unauthorized"

$docs = Invoke-WebRequest -UseBasicParsing -Uri "$base/docs" -TimeoutSec 8
if ($docs.StatusCode -ne 200 -or
    -not $docs.Content.Contains('小程序界面测试') -or
    -not $docs.Content.Contains('验证并记住此浏览器') -or
    $docs.Content.Contains('value="taskbar-calendar"') -or
    $docs.Content.Contains('cdnjs.cloudflare.com') -or
    $docs.Content.Contains($token) -or
    -not $docs.Headers['Content-Security-Policy'].Contains("default-src 'none'")) {
    throw 'Console HTML localization, dynamic defaults or security checks failed.'
}
Write-Output 'Docs: Chinese UI, no default calendar, no external scripts, CSP enabled.'

$badSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$invalid = Expect-HttpFailure {
    Invoke-WebRequest -UseBasicParsing -Uri "$base/v1/console/login" -Method Post -ContentType 'application/json' -Headers @{'X-Yanzi-Token'='definitely-invalid'} -Body '{}' -WebSession $badSession -TimeoutSec 8
} 401
Write-Output "Invalid login: HTTP $invalid"

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-WebRequest -UseBasicParsing -Uri "$base/v1/console/login" -Method Post -ContentType 'application/json' -Headers @{'X-Yanzi-Token'=$token} -Body '{}' -WebSession $session -TimeoutSec 8
$setCookie = [string]$login.Headers['Set-Cookie']
if ($login.StatusCode -ne 200 -or -not $setCookie.Contains('HttpOnly') -or -not $setCookie.Contains('SameSite=Strict') -or -not $setCookie.Contains('Max-Age=31536000')) { throw 'Signed cookie attributes are incorrect.' }
Write-Output 'Console login: HTTP 200, signed HttpOnly SameSite cookie (365 days).'

$status = Invoke-RestMethod -Uri "$base/v1/console/session" -WebSession $session -TimeoutSec 8
if (-not $status.authenticated -or -not $status.remembered) { throw 'Remembered browser session was not accepted.' }
$programs = Invoke-RestMethod -Uri "$base/v1/extensions" -WebSession $session -TimeoutSec 8
if ($null -eq $programs.items) { throw 'Program list response is missing.' }
Write-Output "Remembered-cookie program discovery: HTTP 200, count=$(@($programs.items).Count)."
if (@($programs.items | Where-Object { $null -eq $_.capabilities }).Count -gt 0) { throw 'Program capability association is missing.' }
$catalog = Invoke-RestMethod -Uri "$base/v1/agent/catalog" -WebSession $session -TimeoutSec 8
if ($catalog.schemaVersion -ne '1.0' -or $catalog.extensions.Count -ne $programs.items.Count) { throw 'Unified catalog does not match installed programs.' }
$openapi = Invoke-RestMethod -Uri "$base/v1/agent/openapi.json" -WebSession $session -TimeoutSec 8
if ($openapi.openapi -ne '3.1.0' -or ($openapi | ConvertTo-Json -Depth 30).Contains($token)) { throw 'OpenAPI credential or version check failed.' }
$time = Invoke-RestMethod -Uri "$base/v1/capabilities/invoke" -Method Post -ContentType 'application/json' -Body '{"name":"system.time.now","payload":{}}' -WebSession $session -TimeoutSec 8
if (-not $time.success -or -not $time.data.utc) { throw 'Cookie-authenticated console invocation failed.' }
Write-Output 'Cookie-authenticated AI catalog, OpenAPI and real time invocation: PASS.'

# Use a fresh session containing ONLY the signed cookie (no login headers).
$cookieOnly = New-Object Microsoft.PowerShell.Commands.WebRequestSession
foreach ($cookie in $session.Cookies.GetCookies([uri]"$base/v1/extensions")) {
    $cookieOnly.Cookies.Add([uri]"$base/v1/extensions", $cookie)
}
Write-Output "Cookie-only session count=$($cookieOnly.Cookies.GetCookies([uri]"$base/v1/extensions").Count)"
$forbidden = Expect-HttpFailure {
    Invoke-WebRequest -UseBasicParsing -Uri "$base/v1/extensions" -WebSession $cookieOnly -Headers @{Origin='https://untrusted.example'} -TimeoutSec 8
} 401
Write-Output "Cross-origin cookie replay rejected: HTTP $forbidden"

$logout = Invoke-RestMethod -Uri "$base/v1/console/logout" -Method Post -ContentType 'application/json' -Body '{}' -WebSession $session -TimeoutSec 8
if (-not $logout.signedOut) { throw 'Console sign-out failed.' }
$postLogout = New-Object Microsoft.PowerShell.Commands.WebRequestSession
foreach ($cookie in $session.Cookies.GetCookies([uri]"$base/v1/console/session")) {
    $postLogout.Cookies.Add([uri]"$base/v1/console/session", $cookie)
}
Write-Output "Cookies after logout=$($postLogout.Cookies.GetCookies([uri]"$base/v1/console/session").Count)"
$after = Expect-HttpFailure {
    Invoke-WebRequest -UseBasicParsing -Uri "$base/v1/console/session" -WebSession $postLogout -TimeoutSec 8
} 401
Write-Output "Logout: cookie cleared; session HTTP $after."
Write-Output 'PASS: login, persistent cookie, program discovery, cross-origin defense, logout.'


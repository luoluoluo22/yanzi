param(
    [int]$Port = 8801,
    [string]$Serial = "emulator-5554"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dev-test-worker.ps1")
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot "cloudflare\wrangler.toml"
$Adb = "F:\SDK\platform-tools\adb.exe"
$Package = "cc.luoluoluo.yanzi.mobile"
$Secret = "yanzi-android-object-sync-test"
$LogPath = Join-Path $env:TEMP "yanzi-android-object-sync-worker.log"
$ErrorPath = Join-Path $env:TEMP "yanzi-android-object-sync-worker.err"

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-TestToken([string]$SecretValue, [string]$UserId) {
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $expires = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 900
    $payloadJson = @{ sub = $UserId; username = "android-object-sync-test"; exp = $expires } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SecretValue))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}
if (-not (Test-Path $Adb)) { throw "adb not found: $Adb" }
if ($Serial -notmatch '^emulator-\d+$') {
    throw "This destructive integration test is restricted to an Android emulator. Serial: $Serial"
}
$connected = @(& $Adb devices | Where-Object { $_ -match "^$([regex]::Escape($Serial))\s+device\s*$" })
if ($connected.Count -eq 0) { throw "Android test device is not connected: $Serial" }

$packagePath = & $Adb -s $Serial shell pm path $Package
if (-not ($packagePath -match "^package:")) {
    throw "Yanzi debug app is not installed on $Serial."
}

$clearCommand = '"' + $Adb + '" -s ' + $Serial + ' shell pm clear ' + $Package + ' >nul 2>nul'
cmd.exe /d /c $clearCommand
if ($LASTEXITCODE -ne 0) { throw "Failed to reset Yanzi emulator app data." }

Stop-YanziLocalWorkerPort -Port $Port

Remove-Item -LiteralPath $LogPath, $ErrorPath -Force -ErrorAction SilentlyContinue
Push-Location $RepoRoot
try {
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $ConfigPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply local D1 migrations." }
}
finally {
    Pop-Location
}

$userId = "android-object-sync-$([Guid]::NewGuid().ToString('N'))"
$token = New-TestToken $Secret $userId
$workerArgs = @("wrangler", "dev", "--local", "--ip", "0.0.0.0", "--port", $Port, "--config", $ConfigPath, "--var", "AUTH_TOKEN_SECRET:$Secret", "--var", "SYNC_OBJECTS_AUTHORITATIVE:true")
$worker = Start-Process -FilePath "npx.cmd" -ArgumentList $workerArgs -WorkingDirectory $RepoRoot -WindowStyle Hidden -RedirectStandardOutput $LogPath -RedirectStandardError $ErrorPath -PassThru

try {
    $hostBaseUrl = "http://127.0.0.1:$Port"
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            if ((Invoke-RestMethod -Uri "$hostBaseUrl/health" -TimeoutSec 1).ok) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "Local Worker did not start." }
    $headers = @{ Authorization = "Bearer $token" }
    $stateKey = "integration.test"
    $sha = [Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($stateKey.ToLowerInvariant()))
    $stateHash = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
    $stateObjectId = "yanm.componentState.$stateHash"
    $writerComponentId = "object-sync-writer"
    $writerStateKey = "component:${writerComponentId}:auto"
    $writerHashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($writerStateKey.ToLowerInvariant()))
    $writerHash = -join ($writerHashBytes | ForEach-Object { $_.ToString("x2") })
    $writerStateObjectId = "yanm.componentState.$writerHash"
    $writerHtml = '<div>Object Sync Writer</div><script>setTimeout(function(){window.yanmHost.setState("auto","written-from-webview");},500);</script>'

    $layoutBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{
            settings = @{
                components = @(
                    @{
                        id = $writerComponentId
                        title = "Object Sync Writer"
                        type = "html"
                        html = $writerHtml
                    }
                )
                componentState = @{}
            }
        }
        updatedByDeviceId = "android-integration-test"
        updatedByDeviceName = "Android Integration Test"
    } | ConvertTo-Json -Depth 8 -Compress

    $stateBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{ stateKey = $stateKey; value = "from-object-sync" }
        updatedByDeviceId = "android-integration-test"
        updatedByDeviceName = "Android Integration Test"
    } | ConvertTo-Json -Depth 8 -Compress

    $indexBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{ stateObjectIds = @($stateObjectId) }
        updatedByDeviceId = "android-integration-test"
        updatedByDeviceName = "Android Integration Test"
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/yanm.layout" -Method Put -Headers $headers -ContentType "application/json" -Body $layoutBody | Out-Null
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$stateObjectId" -Method Put -Headers $headers -ContentType "application/json" -Body $stateBody | Out-Null
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/yanm.componentStateIndex" -Method Put -Headers $headers -ContentType "application/json" -Body $indexBody | Out-Null

    $mobileBaseUrl = "http://10.0.2.2:$Port"
    $deviceId = "android-integration-$([Guid]::NewGuid().ToString('N'))"
    $escapedBaseUrl = [Security.SecurityElement]::Escape($mobileBaseUrl)
    $escapedToken = [Security.SecurityElement]::Escape($token)
    $escapedDeviceId = [Security.SecurityElement]::Escape($deviceId)
    $expandedJson = [Security.SecurityElement]::Escape(("[`"{0}`"]" -f $writerComponentId))
    $prefsXml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>`r`n<map>`r`n    <string name=`"baseUrl`">$escapedBaseUrl</string>`r`n    <string name=`"token`">$escapedToken</string>`r`n    <string name=`"username`">android-object-sync-test</string>`r`n    <string name=`"deviceId`">$escapedDeviceId</string>`r`n    <string name=`"expandedComponentIds`">$expandedJson</string>`r`n</map>`r`n"

    $prefsTemp = Join-Path $env:TEMP "yanzi-android-test-prefs.xml"
    [IO.File]::WriteAllText($prefsTemp, $prefsXml, (New-Object Text.UTF8Encoding($false)))
    & $Adb -s $Serial shell am force-stop $Package | Out-Null
    $pushCommand = '"' + $Adb + '" -s ' + $Serial + ' push "' + $prefsTemp + '" /data/local/tmp/yanzi-test-prefs.xml >nul 2>nul'
    cmd.exe /d /c $pushCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Android test preferences." }
    & $Adb -s $Serial shell chmod 644 /data/local/tmp/yanzi-test-prefs.xml | Out-Null
    & $Adb -s $Serial shell run-as $Package mkdir -p shared_prefs | Out-Null
    & $Adb -s $Serial shell run-as $Package cp /data/local/tmp/yanzi-test-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null

    & $Adb -s $Serial shell am start -n "$Package/.MainActivity" | Out-Null
    Start-Sleep -Seconds 14

    $prefsDump = Join-Path $env:TEMP "yanzi-android-prefs-dump.xml"
    $dumpCommand = '"' + $Adb + '" -s ' + $Serial + ' exec-out run-as ' + $Package + ' cat shared_prefs/yanzi-mobile.xml > "' + $prefsDump + '"'
    cmd.exe /c $dumpCommand
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $prefsDump)) {
        throw "Failed to export Android SharedPreferences."
    }
    $prefsRaw = [IO.File]::ReadAllText($prefsDump, [Text.Encoding]::UTF8)
    [xml]$prefsDoc = $prefsRaw
    $cacheNode = @($prefsDoc.map.string | Where-Object { $_.name -eq "cacheYanmJson" }) | Select-Object -First 1
    $logNode = @($prefsDoc.map.string | Where-Object { $_.name -eq "diagnosticLog" }) | Select-Object -First 1
    if (-not $cacheNode) { throw "Android did not cache Yanm data from the local object-sync Worker." }

    $cachedYanm = [string]$cacheNode.'#text'
    if ($cachedYanm -notmatch '"integration\.test":"from-object-sync"') {
        throw "Android Yanm cache did not contain the expected component state. Cache: $cachedYanm"
    }

    $diagnostics = if ($logNode) { [string]$logNode.'#text' } else { "" }
    if ($diagnostics -notmatch "统一对象同步协议") {
        throw "Android did not report using the object-sync protocol. Diagnostics: $diagnostics"
    }

    $remote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects" -Headers $headers
    $writerObject = @($remote.objects | Where-Object { $_.objectId -eq $writerStateObjectId }) | Select-Object -First 1
    if (-not $writerObject) {
        throw "Android WebView state write did not create the expected sync object: $writerStateObjectId"
    }
    if ($writerObject.deleted -or $writerObject.payload.stateKey -ne $writerStateKey -or $writerObject.payload.value -ne "written-from-webview") {
        throw "Android WebView state write returned unexpected payload: $($writerObject | ConvertTo-Json -Depth 8 -Compress)"
    }
    if ($diagnostics -notmatch "燕幕组件状态写入使用统一对象同步协议" -and $diagnostics -notmatch "燕幕写入使用统一对象同步协议") {
        throw "Android did not report object-based Yanm write. Diagnostics: $diagnostics"
    }

    Write-Host "Android object-sync integration PASSED."
    Write-Host "User: $userId"
    Write-Host "Read state object: $stateObjectId"
    Write-Host "Written state object: $writerStateObjectId"
}
finally {
    if ($worker -and -not $worker.HasExited) {
        cmd.exe /d /c "taskkill /PID $($worker.Id) /T /F >nul 2>nul"
    }
    Stop-YanziLocalWorkerPort -Port $Port
}
param(
    [int]$Port = 8804,
    [string]$Serial = "emulator-5554"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot "cloudflare\wrangler.toml"
$Adb = "F:\SDK\platform-tools\adb.exe"
$Package = "cc.luoluoluo.yanzi.mobile"
$Secret = "yanzi-mobile-definition-test"
$WorkerLog = Join-Path $env:TEMP "yanzi-mobile-definition-worker.log"
$WorkerError = Join-Path $env:TEMP "yanzi-mobile-definition-worker.err"
$PrefsTemp = Join-Path $env:TEMP "yanzi-mobile-definition-prefs.xml"

. (Join-Path $PSScriptRoot "dev-test-worker.ps1")

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-Sha256Hex([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))
    return -join ($bytes | ForEach-Object { $_.ToString("x2") })
}

function New-TestToken([string]$SecretValue, [string]$UserId) {
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $expires = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 1200
    $payloadJson = @{ sub = $UserId; username = "mobile-definition-test"; exp = $expires } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SecretValue))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}
function Write-TestPrefs(
    [string]$BaseUrl,
    [string]$Token,
    [string]$DeviceId,
    [string]$ExtensionsJson) {

    $escapedBase = [Security.SecurityElement]::Escape($BaseUrl)
    $escapedToken = [Security.SecurityElement]::Escape($Token)
    $escapedDevice = [Security.SecurityElement]::Escape($DeviceId)
    $escapedExtensions = [Security.SecurityElement]::Escape($ExtensionsJson)
    $nl = [Environment]::NewLine
    $xml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>" + $nl +
        "<map>" + $nl +
        "    <string name=`"baseUrl`">$escapedBase</string>" + $nl +
        "    <string name=`"token`">$escapedToken</string>" + $nl +
        "    <string name=`"username`">mobile-definition-test</string>" + $nl +
        "    <string name=`"deviceId`">$escapedDevice</string>" + $nl +
        "    <string name=`"mobileExtensions`">$escapedExtensions</string>" + $nl +
        "</map>" + $nl

    [IO.File]::WriteAllText($PrefsTemp, $xml, (New-Object Text.UTF8Encoding($false)))
    $pushCommand = '"' + $Adb + '" -s ' + $Serial + ' push "' + $PrefsTemp + '" /data/local/tmp/yanzi-definition-prefs.xml >nul 2>nul'
    cmd.exe /d /c $pushCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Android test preferences." }

    & $Adb -s $Serial shell chmod 644 /data/local/tmp/yanzi-definition-prefs.xml | Out-Null
    & $Adb -s $Serial shell run-as $Package mkdir -p shared_prefs | Out-Null
    & $Adb -s $Serial shell run-as $Package cp /data/local/tmp/yanzi-definition-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null
}

function Start-MobileDefinitionSync {
    & $Adb -s $Serial shell am force-stop $Package | Out-Null
    & $Adb -s $Serial shell am start -a "cc.luoluoluo.yanzi.mobile.sync-mobile-extensions" -n "$Package/.MainActivity" | Out-Null
}

function Read-MobileExtensionsPrefs {
    $dumpPath = Join-Path $env:TEMP ("yanzi-mobile-definition-dump-" + [Guid]::NewGuid().ToString("N") + ".xml")
    $dumpCommand = '"' + $Adb + '" -s ' + $Serial + ' exec-out run-as ' + $Package + ' cat shared_prefs/yanzi-mobile.xml > "' + $dumpPath + '"'
    cmd.exe /d /c $dumpCommand
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dumpPath)) {
        throw "Failed to export Android SharedPreferences."
    }

    [xml]$doc = [IO.File]::ReadAllText($dumpPath, [Text.Encoding]::UTF8)
    $node = @($doc.map.string | Where-Object { $_.name -eq "mobileExtensions" }) | Select-Object -First 1
    if (-not $node) { return @() }

    $raw = [string]$node.'#text'
    if ([string]::IsNullOrWhiteSpace($raw)) { return @() }

    $decoded = $raw | ConvertFrom-Json
    if ($decoded -is [System.Array]) {
        $decoded | ForEach-Object { Write-Output $_ }
        return
    }
    Write-Output $decoded
}
if (-not (Test-Path $Adb)) { throw "adb not found: $Adb" }
if ($Serial -notmatch '^emulator-\d+$') {
    throw "This destructive integration test is restricted to an Android emulator. Serial: $Serial"
}
$connected = @(& $Adb devices | Where-Object { $_ -match "^$([regex]::Escape($Serial))\s+device\s*$" })
if ($connected.Count -eq 0) { throw "Android test device is not connected: $Serial" }
if (-not ((& $Adb -s $Serial shell pm path $Package) -match "^package:")) {
    throw "Yanzi debug app is not installed on $Serial."
}

Stop-YanziLocalWorkerPort -Port $Port
Remove-Item -LiteralPath $WorkerLog, $WorkerError -Force -ErrorAction SilentlyContinue
Push-Location $RepoRoot
try {
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $ConfigPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply local D1 migrations." }
}
finally { Pop-Location }

$userId = "mobile-definition-$([Guid]::NewGuid().ToString('N'))"
$token = New-TestToken $Secret $userId
$headers = @{ Authorization = "Bearer $token" }
$hostBaseUrl = "http://127.0.0.1:$Port"
$mobileBaseUrl = "http://10.0.2.2:$Port"
$workerArgs = @(
    "wrangler", "dev", "--local", "--ip", "0.0.0.0", "--port", $Port,
    "--config", $ConfigPath,
    "--var", "AUTH_TOKEN_SECRET:$Secret",
    "--var", "SYNC_OBJECTS_AUTHORITATIVE:true"
)
$worker = Start-Process -FilePath "npx.cmd" -ArgumentList $workerArgs -WorkingDirectory $RepoRoot -WindowStyle Hidden -RedirectStandardOutput $WorkerLog -RedirectStandardError $WorkerError -PassThru

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            if ((Invoke-RestMethod -Uri "$hostBaseUrl/health" -TimeoutSec 1).ok) {
                $ready = $true
                break
            }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "Local Worker did not start." }

    $localExtensions = @(
        [ordered]@{
            id = "mobile-def-alpha"; name = "Alpha"; version = "0.1.0";
            category = "test"; description = "alpha"; icon = "mdi:alpha-a";
            runtime = "mobile-js"; permissions = @();
            script = @{ source = "async function run(context){ context.mobile.done('alpha'); }" }
        },
        [ordered]@{
            id = "mobile-def-beta"; name = "Beta"; version = "0.1.0";
            category = "test"; description = "beta"; icon = "mdi:alpha-b";
            runtime = "mobile-js"; permissions = @();
            script = @{ source = "async function run(context){ context.mobile.done('beta'); }" }
        }
    )
    $localJson = ConvertTo-Json -InputObject $localExtensions -Depth 8 -Compress
    $deviceId = "android-definition-$([Guid]::NewGuid().ToString('N'))"

    & $Adb -s $Serial shell pm clear $Package | Out-Null
    Write-TestPrefs -BaseUrl $mobileBaseUrl -Token $token -DeviceId $deviceId -ExtensionsJson $localJson
    Start-MobileDefinitionSync

    $index = $null
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $index = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/mobileExtensions.index.v1" -Headers $headers -TimeoutSec 2
            if ($index.object -and $index.object.payload.objectIds.Count -eq 2) { break }
        }
        catch { $index = $null }
    } while ((Get-Date) -lt $deadline)

    if (-not $index -or -not $index.object -or $index.object.payload.objectIds.Count -ne 2) {
        throw "Local mobile definitions were not migrated to the object index."
    }

    $alphaObjectId = "mobileExtension.v1." + (Get-Sha256Hex "mobile-def-alpha")
    $betaObjectId = "mobileExtension.v1." + (Get-Sha256Hex "mobile-def-beta")
    $indexed = @($index.object.payload.objectIds)
    if ($indexed -notcontains $alphaObjectId -or $indexed -notcontains $betaObjectId) {
        throw "Migrated mobile extension index did not contain both expected object IDs."
    }
    & $Adb -s $Serial shell pm clear $Package | Out-Null
    $freshDeviceId = "android-definition-fresh-$([Guid]::NewGuid().ToString('N'))"
    Write-TestPrefs -BaseUrl $mobileBaseUrl -Token $token -DeviceId $freshDeviceId -ExtensionsJson "[]"
    Start-MobileDefinitionSync

    $restored = @()
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $restored = @(Read-MobileExtensionsPrefs)
            if ($restored.Count -eq 2) { break }
        }
        catch { $restored = @() }
    } while ((Get-Date) -lt $deadline)

    if ($restored.Count -ne 2) {
        throw "Fresh Android device did not restore both mobile extension definitions from account objects."
    }
    $restoredIds = @($restored | ForEach-Object { $_.id })
    if ($restoredIds -notcontains "mobile-def-alpha" -or $restoredIds -notcontains "mobile-def-beta") {
        throw "Fresh Android restore returned unexpected mobile extension IDs."
    }

    $alphaRemote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$alphaObjectId" -Headers $headers
    $betaRemote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$betaObjectId" -Headers $headers
    $alphaDefinition = [ordered]@{
        id = "mobile-def-alpha"; name = "Alpha Remote"; version = "0.2.0";
        category = "test"; description = "alpha-remote"; icon = "mdi:alpha-a";
        runtime = "mobile-js"; permissions = @();
        script = @{ source = "async function run(context){ context.mobile.done('alpha-remote'); }" }
    }
    $alphaBody = @{
        schemaVersion = 1
        expectedRevision = [long]$alphaRemote.object.revision
        deleted = $false
        payload = @{ extensionId = "mobile-def-alpha"; definition = $alphaDefinition }
        updatedByDeviceId = "definition-server-edit"
        updatedByDeviceName = "Definition Test"
    } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$alphaObjectId" -Method Put -Headers $headers -ContentType "application/json" -Body $alphaBody | Out-Null

    $betaBody = @{
        schemaVersion = 1
        expectedRevision = [long]$betaRemote.object.revision
        deleted = $true
        payload = $betaRemote.object.payload
        updatedByDeviceId = "definition-server-edit"
        updatedByDeviceName = "Definition Test"
    } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$betaObjectId" -Method Put -Headers $headers -ContentType "application/json" -Body $betaBody | Out-Null
    $indexRemote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/mobileExtensions.index.v1" -Headers $headers
    $indexBody = @{
        schemaVersion = 1
        expectedRevision = [long]$indexRemote.object.revision
        deleted = $false
        payload = @{ objectIds = @($alphaObjectId) }
        updatedByDeviceId = "definition-server-edit"
        updatedByDeviceName = "Definition Test"
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/mobileExtensions.index.v1" -Method Put -Headers $headers -ContentType "application/json" -Body $indexBody | Out-Null

    & $Adb -s $Serial shell pm clear $Package | Out-Null
    $thirdDeviceId = "android-definition-third-$([Guid]::NewGuid().ToString('N'))"
    Write-TestPrefs -BaseUrl $mobileBaseUrl -Token $token -DeviceId $thirdDeviceId -ExtensionsJson "[]"
    Start-MobileDefinitionSync

    $final = @()
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $final = @(Read-MobileExtensionsPrefs)
            if ($final.Count -eq 1 -and $final[0].name -eq "Alpha Remote") { break }
        }
        catch { $final = @() }
    } while ((Get-Date) -lt $deadline)
    if ($final.Count -ne 1) {
        throw "Fresh Android device did not honor the mobile extension tombstone/index update."
    }
    if ($final[0].id -ne "mobile-def-alpha" -or $final[0].name -ne "Alpha Remote") {
        throw "Fresh Android device did not restore the latest mobile extension definition."
    }

    Write-Host "Mobile extension definition object-sync PASSED."
    Write-Host "Migrated 2 local definitions -> restored 2 -> remote update+tombstone -> restored 1 latest definition"
    Write-Host "Index: mobileExtensions.index.v1"
}
finally {
    if ($worker -and -not $worker.HasExited) {
        cmd.exe /d /c "taskkill /PID $($worker.Id) /T /F >nul 2>nul"
    }
    Stop-YanziLocalWorkerPort -Port $Port
    if ($Serial -match '^emulator-\d+$') {
        $clearCommand = '"' + $Adb + '" -s ' + $Serial + ' shell pm clear ' + $Package + ' >nul 2>nul'
        cmd.exe /d /c $clearCommand
    }
}

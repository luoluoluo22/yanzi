param(
    [int]$Port = 8805,
    [string]$Serial = "emulator-5554"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot "cloudflare\wrangler.toml"
$TestArtifact = Join-Path $env:TEMP ("YanziDev/test-unified-extension-catalog/" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TestArtifact | Out-Null
$WorkerState = Join-Path $TestArtifact "worker-state"
$Adb = "F:\SDK\platform-tools\adb.exe"
$Package = "cc.luoluoluo.yanzi.mobile"
$ExtensionId = "unified-mobile-only"
$StorageKey = "catalog/result.txt"
$Secret = "yanzi-unified-catalog-test"
$WorkerLog = Join-Path $TestArtifact "yanzi-unified-catalog-worker.log"
$WorkerError = Join-Path $TestArtifact "yanzi-unified-catalog-worker.err"
$PrefsTemp = Join-Path $TestArtifact "yanzi-unified-catalog-prefs.xml"

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
    $payloadJson = @{ sub = $UserId; username = "unified-catalog-test"; exp = $expires } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SecretValue))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}

function Write-TestPrefs(
    [string]$BaseUrl,
    [string]$Token,
    [string]$DeviceId) {

    $escapedBase = [Security.SecurityElement]::Escape($BaseUrl)
    $escapedToken = [Security.SecurityElement]::Escape($Token)
    $escapedDevice = [Security.SecurityElement]::Escape($DeviceId)
    $nl = [Environment]::NewLine
    $xml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>" + $nl +
        "<map>" + $nl +
        "    <string name=`"baseUrl`">$escapedBase</string>" + $nl +
        "    <string name=`"token`">$escapedToken</string>" + $nl +
        "    <string name=`"username`">unified-catalog-test</string>" + $nl +
        "    <string name=`"deviceId`">$escapedDevice</string>" + $nl +
        "    <string name=`"mobileExtensions`">[]</string>" + $nl +
        "</map>" + $nl
    [IO.File]::WriteAllText($PrefsTemp, $xml, (New-Object Text.UTF8Encoding($false)))
    $pushCommand = '"' + $Adb + '" -s ' + $Serial + ' push "' + $PrefsTemp + '" /data/local/tmp/yanzi-unified-prefs.xml >nul 2>nul'
    cmd.exe /d /c $pushCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Android test preferences." }

    & $Adb -s $Serial shell chmod 644 /data/local/tmp/yanzi-unified-prefs.xml | Out-Null
    & $Adb -s $Serial shell run-as $Package mkdir -p shared_prefs | Out-Null
    & $Adb -s $Serial shell run-as $Package cp /data/local/tmp/yanzi-unified-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null
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
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $WorkerState --config $ConfigPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply local D1 migrations." }
}
finally { Pop-Location }
$userId = "unified-catalog-$([Guid]::NewGuid().ToString('N'))"
$token = New-TestToken $Secret $userId
$headers = @{ Authorization = "Bearer $token" }
$hostBaseUrl = "http://127.0.0.1:$Port"
$mobileBaseUrl = "http://10.0.2.2:$Port"
$deviceId = "android-unified-$([Guid]::NewGuid().ToString('N'))"
$definitionObjectId = "mobileExtension.v1." + (Get-Sha256Hex $ExtensionId)
$resultObjectId = "extensionData.v1." + (Get-Sha256Hex ($ExtensionId + [char]0 + $StorageKey))

$workerArgs = @(
    "wrangler", "dev", "--local", "--persist-to", $WorkerState, "--ip", "0.0.0.0", "--port", $Port,
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
    $scriptSource = @'
async function run(context) {
  const saved = await context.storage.writeText("catalog/result.txt", "ran-local", -1);
  context.mobile.done(saved.ok ? "catalog-local-ok" : "catalog-local-failed");
}
'@

    $definition = [ordered]@{
        id = $ExtensionId
        name = "Unified Local"
        version = "0.1.0"
        category = "test"
        description = "Unified catalog local runtime test"
        icon = "mdi:cellphone"
        runtime = "mobile-js"
        permissions = @()
        script = @{ source = $scriptSource }
    }

    $definitionBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{ extensionId = $ExtensionId; definition = $definition }
        updatedByDeviceId = "unified-catalog-seed"
        updatedByDeviceName = "Unified Catalog Test"
    } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$definitionObjectId" -Method Put -Headers $headers -ContentType "application/json" -Body $definitionBody | Out-Null

    $indexBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{ objectIds = @($definitionObjectId) }
        updatedByDeviceId = "unified-catalog-seed"
        updatedByDeviceName = "Unified Catalog Test"
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/mobileExtensions.index.v1" -Method Put -Headers $headers -ContentType "application/json" -Body $indexBody | Out-Null

    & $Adb -s $Serial shell pm clear $Package | Out-Null
    Write-TestPrefs -BaseUrl $mobileBaseUrl -Token $token -DeviceId $deviceId

    & $Adb -s $Serial shell am start `
        -a "cc.luoluoluo.yanzi.mobile.extensions" `
        -n "$Package/.MainActivity" | Out-Null

    Start-Sleep -Seconds 7
    $uiRemote = "/sdcard/yanzi-unified-ui.xml"
    $uiLocal = Join-Path $env:TEMP "yanzi-unified-ui.xml"
    $dumpCommand = '"' + $Adb + '" -s ' + $Serial + ' shell uiautomator dump ' + $uiRemote + ' >nul 2>nul'
    cmd.exe /d /c $dumpCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to dump unified catalog UI." }
    $pullCommand = '"' + $Adb + '" -s ' + $Serial + ' pull ' + $uiRemote + ' "' + $uiLocal + '" >nul 2>nul'
    cmd.exe /d /c $pullCommand
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $uiLocal)) { throw "Failed to pull unified catalog UI." }
    [xml]$uiDoc = [IO.File]::ReadAllText($uiLocal, [Text.Encoding]::UTF8)
    $nameNode = $uiDoc.SelectSingleNode("//node[@text='Unified Local']")
    if (-not $nameNode) {
        throw "Unified catalog did not display the mobile-only extension."
    }
    $runtimeText = ([char]0x672C).ToString() + [char]0x673A
    $runtimeNode = @(
        $uiDoc.SelectNodes("//node") |
            Where-Object { [string]$_.text -eq $runtimeText }
    ) | Select-Object -First 1
    if (-not $runtimeNode) {
        throw "Unified catalog did not mark the mobile-only extension as local runtime."
    }

    $bounds = [string]$nameNode.bounds
    $match = [regex]::Match($bounds, '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$')
    if (-not $match.Success) { throw "Could not parse unified catalog card bounds: $bounds" }
    $left = [int]$match.Groups[1].Value
    $top = [int]$match.Groups[2].Value
    $right = [int]$match.Groups[3].Value
    $bottom = [int]$match.Groups[4].Value
    $tapX = [int](($left + $right) / 2)
    $tapY = [int](($top + $bottom) / 2)
    & $Adb -s $Serial shell input tap $tapX $tapY | Out-Null

    $result = $null
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $result = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$resultObjectId" -Headers $headers -TimeoutSec 2
            if ($result.object -and -not $result.object.deleted) { break }
        }
        catch { $result = $null }
    } while ((Get-Date) -lt $deadline)
    if (-not $result -or -not $result.object) {
        throw "Tapping the unified catalog entry did not run the mobile runtime."
    }
    if ($result.object.payload.content -ne "ran-local") {
        throw "Unified catalog local runtime wrote an unexpected result."
    }

    Write-Host "Unified extension catalog PASSED."
    Write-Host "Account mobile definition -> unified catalog -> 本机 badge -> tap -> local mobile-js execution"
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

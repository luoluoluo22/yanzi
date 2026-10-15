param(
    [int]$Port = 8802,
    [string]$Serial = "emulator-5554",
    [string]$Package = "cc.luoluoluo.yanzi.mobile",
    [string]$ActivityClass = "cc.luoluoluo.yanzi.mobile.MainActivity",
    [string]$MobileBaseUrlOverride = "",
    [switch]$AllowPhysicalDev
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dev-test-worker.ps1")
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot "cloudflare\wrangler.toml"
$TestArtifact = Join-Path $env:TEMP ("YanziDev/test-mobile-extension-storage/" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TestArtifact | Out-Null
$WorkerState = Join-Path $TestArtifact "worker-state"
$Adb = "F:\SDK\platform-tools\adb.exe"
$ExtensionId = "mobile-storage-integration"
$StorageKey = "integration/state.json"
$ResultKey = "integration/result.json"
$Secret = "yanzi-mobile-storage-test"
$WorkerLog = Join-Path $TestArtifact "yanzi-mobile-storage-worker.log"
$WorkerError = Join-Path $TestArtifact "yanzi-mobile-storage-worker.err"
$PrefsTemp = Join-Path $TestArtifact "yanzi-mobile-storage-prefs.xml"

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
    $payloadJson = @{ sub = $UserId; username = "mobile-storage-test"; exp = $expires } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SecretValue))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}

if (-not (Test-Path $Adb)) { throw "adb not found: $Adb" }

$isEmulator = $Serial -match '^emulator-\d+$'
$isAllowedPhysicalDev = -not $isEmulator -and
    $AllowPhysicalDev -and
    $Package -eq "cc.luoluoluo.yanzi.mobile.dev"

if (-not $isEmulator -and -not $isAllowedPhysicalDev) {
    throw "Physical-device destructive testing is allowed only for the isolated .dev package with -AllowPhysicalDev. Serial: $Serial Package: $Package"
}

$connected = @(& $Adb devices | Where-Object { $_ -match "^$([regex]::Escape($Serial))\s+device\s*$" })
if ($connected.Count -eq 0) { throw "Android test device is not connected: $Serial" }

$packagePath = & $Adb -s $Serial shell pm path $Package
if (-not ($packagePath -match "^package:")) {
    throw "Yanzi debug app is not installed on $Serial."
}

$clearCommand = '"' + $Adb + '" -s ' + $Serial + ' shell pm clear ' + $Package + ' >nul 2>nul'
cmd.exe /d /c $clearCommand
if ($LASTEXITCODE -ne 0) { throw "Failed to reset Yanzi test app data." }

if ($isEmulator) {
    & $Adb -s $Serial logcat -b crash -c | Out-Null
}

Stop-YanziLocalWorkerPort -Port $Port

Remove-Item -LiteralPath $WorkerLog, $WorkerError -Force -ErrorAction SilentlyContinue
Push-Location $RepoRoot
try {
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $WorkerState --config $ConfigPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply local D1 migrations." }
}
finally {
    Pop-Location
}

$userId = "mobile-storage-$([Guid]::NewGuid().ToString('N'))"
$token = New-TestToken $Secret $userId
$deviceId = "android-storage-$([Guid]::NewGuid().ToString('N'))"
$hostBaseUrl = "http://127.0.0.1:$Port"
$mobileBaseUrl = if (-not [string]::IsNullOrWhiteSpace($MobileBaseUrlOverride)) {
    $MobileBaseUrlOverride.TrimEnd('/')
}
elseif ($isEmulator) {
    "http://10.0.2.2:$Port"
}
else {
    throw "Physical Dev testing requires -MobileBaseUrlOverride."
}
$objectId = "extensionData.v1." + (Get-Sha256Hex ($ExtensionId + [char]0 + $StorageKey))
$resultObjectId = "extensionData.v1." + (Get-Sha256Hex ($ExtensionId + [char]0 + $ResultKey))

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
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { throw "Local Worker did not start." }

    $scriptSource = @'
async function run(context) {
  const first = await context.storage.writeText("integration/state.json", "mobile-v1", -1);
  const read1 = await context.storage.readText("integration/state.json");
  const second = await context.storage.writeText("integration/state.json", "mobile-v2", first.revision);
  const conflict = await context.storage.writeText("integration/state.json", "stale-mobile", first.revision);
  const read2 = await context.storage.readText("integration/state.json");
  const remove = await context.storage.deleteText("integration/state.json", second.revision);
  const read3 = await context.storage.readText("integration/state.json");
  const result = { first, read1, second, conflict, read2, remove, read3 };
  await context.storage.writeText("integration/result.json", JSON.stringify(result), -1);
  context.mobile.done("storage-object-sync-complete");
}
'@

    $extension = [ordered]@{
        id = $ExtensionId
        name = "StorageIntegration"
        version = "0.1.0"
        category = "test"
        description = "Automated account object storage test"
        icon = "mdi:database"
        runtime = "mobile-js"
        permissions = @()
        script = @{ source = $scriptSource }
    }

    $mobileExtensions = ConvertTo-Json -InputObject @($extension) -Depth 8 -Compress
    $escapedBase = [Security.SecurityElement]::Escape($mobileBaseUrl)
    $escapedToken = [Security.SecurityElement]::Escape($token)
    $escapedDevice = [Security.SecurityElement]::Escape($deviceId)
    $escapedExtensions = [Security.SecurityElement]::Escape($mobileExtensions)
    $nl = [Environment]::NewLine
    $prefsXml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>" + $nl +
        "<map>" + $nl +
        "    <string name=`"baseUrl`">$escapedBase</string>" + $nl +
        "    <string name=`"token`">$escapedToken</string>" + $nl +
        "    <string name=`"username`">mobile-storage-test</string>" + $nl +
        "    <string name=`"deviceId`">$escapedDevice</string>" + $nl +
        "    <string name=`"mobileExtensions`">$escapedExtensions</string>" + $nl +
        "</map>" + $nl
    [IO.File]::WriteAllText($PrefsTemp, $prefsXml, (New-Object Text.UTF8Encoding($false)))

    & $Adb -s $Serial shell am force-stop $Package | Out-Null
    $pushCommand = '"' + $Adb + '" -s ' + $Serial + ' push "' + $PrefsTemp + '" /data/local/tmp/yanzi-storage-prefs.xml >nul 2>nul'
    cmd.exe /d /c $pushCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Android test preferences." }
    & $Adb -s $Serial shell chmod 644 /data/local/tmp/yanzi-storage-prefs.xml | Out-Null
    & $Adb -s $Serial shell run-as $Package mkdir -p shared_prefs | Out-Null
    & $Adb -s $Serial shell run-as $Package cp /data/local/tmp/yanzi-storage-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null

    & $Adb -s $Serial shell am start -n "$Package/$ActivityClass" --es run_mobile_extension_id $ExtensionId --es run_mobile_extension_name StorageIntegration | Out-Null

    $headers = @{ Authorization = "Bearer $token" }
    $deadline = (Get-Date).AddSeconds(25)
    $resultEnvelope = $null
    do {
        Start-Sleep -Milliseconds 500
        try {
            $resultEnvelope = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$resultObjectId" -Headers $headers -TimeoutSec 2
            if ($resultEnvelope.object -and -not $resultEnvelope.object.deleted) { break }
        }
        catch {
            $resultEnvelope = $null
        }
    } while ((Get-Date) -lt $deadline)

    if (-not $resultEnvelope -or -not $resultEnvelope.object) {
        throw "Mobile extension did not publish its result object."
    }

    $raw = [string]$resultEnvelope.object.payload.content
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "Mobile extension result object was empty." }
    $result = $raw | ConvertFrom-Json

    if (-not $result.first.ok -or $result.first.conflict -or $result.first.revision -ne 1) {
        throw "Initial Android storage write failed: $raw"
    }
    if ($result.read1.content -ne "mobile-v1" -or $result.read1.revision -ne 1) {
        throw "Initial Android storage read failed: $raw"
    }
    if (-not $result.second.ok -or $result.second.conflict -or $result.second.revision -ne 2) {
        throw "Second Android storage write failed: $raw"
    }
    if (-not $result.conflict.conflict -or $result.conflict.currentRevision -ne 2) {
        throw "Stale Android write was not rejected: $raw"
    }
    if ($result.read2.content -ne "mobile-v2" -or $result.read2.revision -ne 2) {
        throw "Android did not preserve revision 2 after conflict: $raw"
    }
    if (-not $result.remove.ok -or $result.remove.conflict -or -not $result.remove.deleted -or $result.remove.revision -ne 3) {
        throw "Android tombstone write failed: $raw"
    }
    if ($result.read3.exists -or -not $result.read3.deleted -or $result.read3.revision -ne 3) {
        throw "Android tombstone read failed: $raw"
    }

    $headers = @{ Authorization = "Bearer $token" }
    $remote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$objectId" -Headers $headers
    if (-not $remote.object.deleted -or $remote.object.revision -ne 3) {
        throw "Worker did not persist the final Android tombstone."
    }

    if ($isEmulator) {
        $crashText = (& $Adb -s $Serial logcat -b crash -d -t 200 2>$null) -join [Environment]::NewLine
        if ($crashText -match [regex]::Escape("Process: $Package") -and
            $crashText -match "FATAL EXCEPTION") {
            throw "Android app crashed during the mobile extension storage test."
        }
    }

    Write-Host "Mobile extension storage object-sync PASSED."
    Write-Host "User: $userId"
    Write-Host "Object: $objectId"
    Write-Host "Revisions: 1 -> 2 -> conflict(2) -> tombstone(3)"
}
finally {
    if ($worker -and -not $worker.HasExited) {
        cmd.exe /d /c "taskkill /PID $($worker.Id) /T /F >nul 2>nul"
    }
    Stop-YanziLocalWorkerPort -Port $Port
}

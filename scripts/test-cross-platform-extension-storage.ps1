param(
    [int]$Port = 8803,
    [string]$Serial = "emulator-5554"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dev-test-worker.ps1")
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ConfigPath = Join-Path $RepoRoot "cloudflare\wrangler.toml"
$VerifyProject = Join-Path $RepoRoot "src\Yanzi.SyncVerification\Yanzi.SyncVerification.csproj"
$Adb = "F:\SDK\platform-tools\adb.exe"
$Package = "cc.luoluoluo.yanzi.mobile"
$ExtensionId = "cross-platform-storage"
$StorageKey = "shared/state.json"
$ResultKey = "shared/result.json"
$Secret = "yanzi-cross-platform-storage-test"
$WorkerLog = Join-Path $env:TEMP "yanzi-cross-platform-worker.log"
$WorkerError = Join-Path $env:TEMP "yanzi-cross-platform-worker.err"
$PrefsTemp = Join-Path $env:TEMP "yanzi-cross-platform-prefs.xml"

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
    $payloadJson = @{ sub = $UserId; username = "cross-platform-test"; exp = $expires } | ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($SecretValue))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}

function Invoke-WindowsBridge(
    [string]$Operation,
    [string]$Root,
    [string]$BaseUrl,
    [string]$Token,
    [string]$UserId,
    [string]$Content = "",
    [Nullable[long]]$ExpectedRevision = $null) {

    $arguments = @(
        "run", "--project", $VerifyProject, "--no-build", "--",
        "--account-extension-bridge",
        "--operation", $Operation,
        "--root", $Root,
        "--base-url", $BaseUrl,
        "--token", $Token,
        "--user-id", $UserId,
        "--extension-id", $ExtensionId,
        "--key", $StorageKey
    )

    if ($Operation -eq "write") {
        $arguments += @("--content", $Content)
        if ($null -ne $ExpectedRevision) {
            $arguments += @("--expected-revision", [string]$ExpectedRevision)
        }
    }

    $output = @(& dotnet @arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Windows account-extension bridge failed: $($output -join [Environment]::NewLine)"
    }

    $resultLine = @($output | Where-Object { $_ -like "RESULT_JSON:*" }) | Select-Object -Last 1
    if (-not $resultLine) {
        throw "Windows bridge returned no RESULT_JSON line."
    }
    return ($resultLine.Substring("RESULT_JSON:".Length) | ConvertFrom-Json)
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

$clearCommand = '"' + $Adb + '" -s ' + $Serial + ' shell pm clear ' + $Package + ' >nul 2>nul'
cmd.exe /d /c $clearCommand
if ($LASTEXITCODE -ne 0) { throw "Failed to reset Yanzi emulator app data." }

Stop-YanziLocalWorkerPort -Port $Port

Remove-Item -LiteralPath $WorkerLog, $WorkerError -Force -ErrorAction SilentlyContinue
Push-Location $RepoRoot
try {
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $ConfigPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply local D1 migrations." }
}
finally {
    Pop-Location
}

$userId = "cross-platform-$([Guid]::NewGuid().ToString('N'))"
$token = New-TestToken $Secret $userId
$hostBaseUrl = "http://127.0.0.1:$Port"
$mobileBaseUrl = "http://10.0.2.2:$Port"
$objectId = "extensionData.v1." + (Get-Sha256Hex ($ExtensionId + [char]0 + $StorageKey))
$resultObjectId = "extensionData.v1." + (Get-Sha256Hex ($ExtensionId + [char]0 + $ResultKey))

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

    $desktopRoot1 = Join-Path $env:TEMP ("yanzi-cross-desktop-write-" + [Guid]::NewGuid().ToString("N"))
    $desktopWrite = Invoke-WindowsBridge -Operation "write" -Root $desktopRoot1 -BaseUrl $hostBaseUrl -Token $token -UserId $userId -Content "desktop-v1" -ExpectedRevision 0
    if (-not $desktopWrite.available -or $desktopWrite.revision -ne 1) {
        throw "Windows initial write failed: $($desktopWrite | ConvertTo-Json -Compress)"
    }

    $scriptSource = @'
async function run(context) {
  const first = await context.storage.readText("shared/state.json");
  const write = await context.storage.writeText("shared/state.json", "mobile-v2", first.revision);
  const verify = await context.storage.readText("shared/state.json");
  const result = { first, write, verify };
  await context.storage.writeText("shared/result.json", JSON.stringify(result), -1);
  context.mobile.done("cross-platform-storage-complete");
}
'@
    $extension = [ordered]@{
        id = $ExtensionId
        name = "CrossPlatformStorage"
        version = "0.1.0"
        category = "test"
        description = "Windows and Android account storage integration"
        icon = "mdi:database-sync"
        runtime = "mobile-js"
        permissions = @()
        script = @{ source = $scriptSource }
    }

    $mobileExtensions = ConvertTo-Json -InputObject @($extension) -Depth 8 -Compress
    $deviceId = "android-cross-$([Guid]::NewGuid().ToString('N'))"
    $escapedBase = [Security.SecurityElement]::Escape($mobileBaseUrl)
    $escapedToken = [Security.SecurityElement]::Escape($token)
    $escapedDevice = [Security.SecurityElement]::Escape($deviceId)
    $escapedExtensions = [Security.SecurityElement]::Escape($mobileExtensions)
    $nl = [Environment]::NewLine
    $prefsXml = "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>" + $nl +
        "<map>" + $nl +
        "    <string name=`"baseUrl`">$escapedBase</string>" + $nl +
        "    <string name=`"token`">$escapedToken</string>" + $nl +
        "    <string name=`"username`">cross-platform-test</string>" + $nl +
        "    <string name=`"deviceId`">$escapedDevice</string>" + $nl +
        "    <string name=`"mobileExtensions`">$escapedExtensions</string>" + $nl +
        "</map>" + $nl
    [IO.File]::WriteAllText($PrefsTemp, $prefsXml, (New-Object Text.UTF8Encoding($false)))
    & $Adb -s $Serial shell am force-stop $Package | Out-Null
    $pushCommand = '"' + $Adb + '" -s ' + $Serial + ' push "' + $PrefsTemp + '" /data/local/tmp/yanzi-cross-prefs.xml >nul 2>nul'
    cmd.exe /d /c $pushCommand
    if ($LASTEXITCODE -ne 0) { throw "Failed to push Android test preferences." }
    & $Adb -s $Serial shell chmod 644 /data/local/tmp/yanzi-cross-prefs.xml | Out-Null
    & $Adb -s $Serial shell run-as $Package mkdir -p shared_prefs | Out-Null
    & $Adb -s $Serial shell run-as $Package cp /data/local/tmp/yanzi-cross-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null

    & $Adb -s $Serial shell am start -n "$Package/.MainActivity" --es run_mobile_extension_id $ExtensionId --es run_mobile_extension_name CrossPlatformStorage | Out-Null

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
        throw "Android did not publish the cross-platform result object."
    }
    $raw = [string]$resultEnvelope.object.payload.content
    if ([string]::IsNullOrWhiteSpace($raw)) { throw "Android result object was empty." }
    $mobileResult = $raw | ConvertFrom-Json

    if (-not $mobileResult.first.exists -or $mobileResult.first.content -ne "desktop-v1" -or $mobileResult.first.revision -ne 1) {
        throw "Android did not read the Windows value: $raw"
    }
    if (-not $mobileResult.write.ok -or $mobileResult.write.conflict -or $mobileResult.write.revision -ne 2) {
        throw "Android did not update the Windows value: $raw"
    }
    if ($mobileResult.verify.content -ne "mobile-v2" -or $mobileResult.verify.revision -ne 2) {
        throw "Android verification read did not see revision 2: $raw"
    }

    $desktopRoot2 = Join-Path $env:TEMP ("yanzi-cross-desktop-read-" + [Guid]::NewGuid().ToString("N"))
    $desktopRead = Invoke-WindowsBridge -Operation "read" -Root $desktopRoot2 -BaseUrl $hostBaseUrl -Token $token -UserId $userId
    if (-not $desktopRead.available -or -not $desktopRead.exists -or $desktopRead.revision -ne 2 -or $desktopRead.content -ne "mobile-v2") {
        throw "Windows did not read the Android update: $($desktopRead | ConvertTo-Json -Compress)"
    }

    $remote = Invoke-RestMethod -Uri "$hostBaseUrl/v1/sync/objects/$objectId" -Headers $headers
    if ($remote.object.revision -ne 2 -or $remote.object.payload.content -ne "mobile-v2") {
        throw "Worker final object does not match the shared cross-platform value."
    }

    Write-Host "Cross-platform extension storage PASSED."
    Write-Host "Windows rev1 -> Android rev2 -> Windows read rev2"
    Write-Host "Object: $objectId"
}
finally {
    if ($worker -and -not $worker.HasExited) {
        cmd.exe /d /c "taskkill /PID $($worker.Id) /T /F >nul 2>nul"
    }
    Stop-YanziLocalWorkerPort -Port $Port
}

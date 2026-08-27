param(
    [int]$Port = 8799,
    [ValidateSet("true", "false")]
    [string]$AuthorityMode = "true"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $repoRoot "cloudflare\wrangler.toml"
$testSecret = "yanzi-local-object-sync-test"
$logPath = Join-Path $env:TEMP "yanzi-cloud-object-sync-test.log"
$errorPath = Join-Path $env:TEMP "yanzi-cloud-object-sync-test.err"
Remove-Item -LiteralPath $logPath, $errorPath -Force -ErrorAction SilentlyContinue

& npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $configPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to apply local D1 migrations before protocol test."
}

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-TestToken([string]$Secret) {
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $expires = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 600
    $payloadJson = @{ sub = "object-sync-test-user"; username = "object-sync-test"; exp = $expires } |
        ConvertTo-Json -Compress
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $data = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data)))
    return "$data.$signature"
}

$objectsAuthoritative = $AuthorityMode -eq "true"
$authoritativeValue = $AuthorityMode
$worker = Start-Process `
    -FilePath "npx.cmd" `
    -ArgumentList @("wrangler", "dev", "--local", "--port", $Port, "--config", $configPath, "--var", "AUTH_TOKEN_SECRET:$testSecret", "--var", "SYNC_OBJECTS_AUTHORITATIVE:$authoritativeValue") `
    -WorkingDirectory $repoRoot `
    -WindowStyle Hidden `
    -RedirectStandardOutput $logPath `
    -RedirectStandardError $errorPath `
    -PassThru

try {
    $baseUrl = "http://127.0.0.1:$Port"
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            if ((Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 1).ok) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) {
        throw "Worker did not start.`n$(Get-Content $logPath -Raw)`n$(Get-Content $errorPath -Raw)"
    }

    $headers = @{ Authorization = "Bearer $(New-TestToken $testSecret)" }
    $capabilities = Invoke-RestMethod -Uri "$baseUrl/v1/sync/capabilities" -Headers $headers
    if (-not $capabilities.objectSyncAvailable -or
        -not $capabilities.objectHistoryAvailable -or
        $capabilities.protocolVersion -lt 2 -or
        [bool]$capabilities.objectsAuthoritative -ne $objectsAuthoritative -or
        [bool]$capabilities.legacySnapshotWriteRequired -eq $objectsAuthoritative) {
        throw "Capability negotiation returned an unexpected authority mode."
    }
    $objectId = "test.$([Guid]::NewGuid().ToString('N'))"
    $createBody = @{
        schemaVersion = 1
        expectedRevision = 0
        deleted = $false
        payload = @{ value = "first" }
        updatedByDeviceId = "test-device"
        updatedByDeviceName = "Protocol Test"
    } | ConvertTo-Json -Compress

    $created = Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/$objectId" -Method Put -Headers $headers -ContentType "application/json" -Body $createBody
    if ($created.object.revision -le 0) { throw "Create did not return a positive revision." }

    $deviceBBody = @{
        schemaVersion = 1
        expectedRevision = $created.object.revision
        deleted = $false
        payload = @{ value = "device-b" }
        updatedByDeviceId = "device-b"
        updatedByDeviceName = "Device B"
    } | ConvertTo-Json -Compress
    $deviceB = Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/$objectId" -Method Put -Headers $headers -ContentType "application/json" -Body $deviceBBody

    $deviceABody = @{
        schemaVersion = 1
        expectedRevision = $created.object.revision
        deleted = $false
        payload = @{ value = "device-a" }
        updatedByDeviceId = "device-a"
        updatedByDeviceName = "Device A"
    } | ConvertTo-Json -Compress
    $conflictStatus = 0
    $conflictDetails = $null
    try {
        Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/$objectId" -Method Put -Headers $headers -ContentType "application/json" -Body $deviceABody | Out-Null
    }
    catch {
        $conflictStatus = [int]$_.Exception.Response.StatusCode
        if (-not [string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $conflictDetails = $_.ErrorDetails.Message | ConvertFrom-Json
        }
    }
    if ($conflictStatus -ne 409) { throw "Expected revision conflict 409, got $conflictStatus." }
    if ($conflictDetails.details.currentRevision -ne $deviceB.object.revision) {
        throw "Conflict did not return the current remote revision."
    }

    $retryBody = @{
        schemaVersion = 1
        expectedRevision = $deviceB.object.revision
        deleted = $false
        payload = @{ value = "device-a-retry" }
        updatedByDeviceId = "device-a"
        updatedByDeviceName = "Device A"
    } | ConvertTo-Json -Compress
    $updated = Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/$objectId" -Method Put -Headers $headers -ContentType "application/json" -Body $retryBody
    if ($updated.object.revision -le $deviceB.object.revision) { throw "Conflict retry revision did not advance." }
    if ($updated.object.revision -ne $deviceB.object.revision + 1) { throw "Rejected conflict unexpectedly consumed a global revision." }

    $history = Invoke-RestMethod -Uri "$baseUrl/v1/sync/history?objectId=$objectId&limit=20" -Headers $headers
    if ($history.versions.Count -ne 3) { throw "Expected 3 historical versions before restore, got $($history.versions.Count)." }
    if ($history.versions[0].revision -ne $updated.object.revision -or $history.versions[2].operation -ne "create") {
        throw "History order or operation metadata is incorrect."
    }

    $restoreBody = @{
        expectedRevision = $updated.object.revision
        restoreRevision = $created.object.revision
        updatedByDeviceId = "restore-device"
        updatedByDeviceName = "Restore Device"
    } | ConvertTo-Json -Compress
    $restored = Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/$objectId/restore" -Method Post -Headers $headers -ContentType "application/json" -Body $restoreBody
    if ($restored.object.revision -ne $updated.object.revision + 1 -or $restored.object.payload.value -ne "first") {
        throw "Restore did not create a new revision with the selected historical payload."
    }
    $historyAfterRestore = Invoke-RestMethod -Uri "$baseUrl/v1/sync/history?objectId=$objectId&limit=1" -Headers $headers
    if ($historyAfterRestore.versions[0].operation -ne "restore" -or
        $historyAfterRestore.versions[0].restoredFromRevision -ne $created.object.revision) {
        throw "Restore provenance was not recorded in history."
    }

    $yanmBody = @{
        updatedAtUtc = [DateTime]::UtcNow.ToString("O")
        yanm = @{
            components = @()
            componentState = @{ note = "first"; timer = "keep-remote" }
        }
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod -Uri "$baseUrl/v1/me/yanm-state" -Method Put -Headers $headers -ContentType "application/json" -Body $yanmBody | Out-Null
    $yanmPatchBody = @{
        componentState = @{ note = "patched" }
    } | ConvertTo-Json -Depth 5 -Compress
    Invoke-RestMethod -Uri "$baseUrl/v1/me/yanm-state/component-state" -Method Put -Headers $headers -ContentType "application/json" -Body $yanmPatchBody | Out-Null
    $yanmAfterPatch = Invoke-RestMethod -Uri "$baseUrl/v1/me/yanm-state" -Headers $headers
    if ($yanmAfterPatch.yanm.componentState.note -ne "patched" -or
        $yanmAfterPatch.yanm.componentState.timer -ne "keep-remote") {
        throw "Yanm component-state patch overwrote an unrelated key."
    }

    $allObjects = Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects" -Headers $headers
    $existingAiObject = $allObjects.objects | Where-Object objectId -eq "settings.ai" | Select-Object -First 1
    $aiExpectedRevision = if ($null -eq $existingAiObject) { 0 } else { $existingAiObject.revision }
    $aiBody = @{
        schemaVersion = 1
        expectedRevision = $aiExpectedRevision
        deleted = $false
        payload = @{
            aiApiKey = "must-not-persist"
            aiServiceProviders = @(@{ id = "test"; apiKey = "nested-must-not-persist" })
            aiModel = "safe-model"
        }
        updatedByDeviceId = "security-test"
        updatedByDeviceName = "Security Test"
    } | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects/settings.ai" -Method Put -Headers $headers -ContentType "application/json" -Body $aiBody | Out-Null
    $aiHistory = Invoke-RestMethod -Uri "$baseUrl/v1/sync/history?objectId=settings.ai&limit=1" -Headers $headers
    if ($aiHistory.versions[0].payload.aiApiKey -ne "" -or
        $aiHistory.versions[0].payload.aiServiceProviders[0].apiKey -ne "" -or
        $aiHistory.versions[0].payload.aiModel -ne "safe-model") {
        throw "Server-side AI secret boundary did not scrub object history payload."
    }

    $personalConfigBody = @{
        installedVersion = "1"
        enabled = $true
        settings = @{
            enabled = $true
            provider = "github"
            secrets = @{ githubToken = "must-not-persist"; webDavPassword = "must-not-persist" }
            password = "legacy-must-not-persist"
        }
    } | ConvertTo-Json -Depth 8 -Compress
    $personalManifestBody = @{
        manifest = @{
            name = "yanzi-personal-sync-settings"
            displayName = "Protocol Personal Sync Settings"
            version = "1"
            category = "system"
        }
    } | ConvertTo-Json -Depth 5 -Compress
    Invoke-RestMethod -Uri "$baseUrl/v1/extensions/yanzi-personal-sync-settings" -Method Put -Headers $headers -ContentType "application/json" -Body $personalManifestBody | Out-Null
    Invoke-RestMethod -Uri "$baseUrl/v1/me/extensions/yanzi-personal-sync-settings" -Method Put -Headers $headers -ContentType "application/json" -Body $personalConfigBody | Out-Null
    $personalItems = Invoke-RestMethod -Uri "$baseUrl/v1/me/extensions" -Headers $headers
    $personalItem = $personalItems.items | Where-Object extension_id -eq "yanzi-personal-sync-settings" | Select-Object -First 1
    if ($null -eq $personalItem) {
        $personalItem = $personalItems.items | Where-Object extensionId -eq "yanzi-personal-sync-settings" | Select-Object -First 1
    }
    $storedPersonalConfig = $personalItem.settings_json | ConvertFrom-Json
    if ($null -eq $storedPersonalConfig) {
        $storedPersonalConfig = $personalItem.settingsJson | ConvertFrom-Json
    }
    if ($storedPersonalConfig.password -ne "" -or
        ($storedPersonalConfig.secrets.PSObject.Properties | Measure-Object).Count -ne 0) {
        throw "Server-side personal sync credential boundary persisted repository secrets."
    }
    $yanmMirrorAfterCredentialScrub = Invoke-RestMethod -Uri "$baseUrl/v1/me/yanm-state" -Method Put -Headers $headers -ContentType "application/json" -Body $yanmBody
    if (-not $yanmMirrorAfterCredentialScrub.ok) {
        throw "Yanm cloud fallback failed after repository credentials became device-local."
    }

    $changes = Invoke-RestMethod -Uri "$baseUrl/v1/sync/changes?since=0" -Headers $headers
    if (-not ($changes.objects | Where-Object objectId -eq $objectId)) { throw "Incremental read did not return the updated object." }

    $invalidStatus = 0
    try {
        Invoke-RestMethod -Uri "$baseUrl/v1/sync/objects" -Headers @{ Authorization = "Bearer $($headers.Authorization.Substring(7, $headers.Authorization.Length - 8))x" } | Out-Null
    }
    catch {
        $invalidStatus = [int]$_.Exception.Response.StatusCode
    }
    if ($invalidStatus -ne 401) { throw "Expected invalid token signature 401, got $invalidStatus." }

    Write-Output "Cloud object sync protocol test passed: authoritative=$authoritativeValue, create=$($created.object.revision), deviceB=$($deviceB.object.revision), retry=$($updated.object.revision), restore=$($restored.object.revision), history=4, yanmPatch=preserved, aiSecrets=scrubbed, repoSecrets=scrubbed, conflict=409+$($conflictDetails.details.currentRevision), invalidToken=401"
}
finally {
    if (-not $worker.HasExited) {
        & taskkill /PID $worker.Id /T /F | Out-Null
    }
}

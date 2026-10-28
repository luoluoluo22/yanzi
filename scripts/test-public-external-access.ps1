param(
    [string]$Serial = "",
    [switch]$CleanStaleHeadlessDiagnostics,
    [switch]$RequireDeviceApproval
)

$ErrorActionPreference = "Stop"
$Adb = "F:\\SDK\\platform-tools\\adb.exe"
$DevPackage = "cc.luoluoluo.yanzi.mobile.dev"
$ExpectedBaseUrl = "https://sync.luoluoluo.cc.cd"

function Get-StatusCode($ErrorRecord) {
    try { return [int]$ErrorRecord.Exception.Response.StatusCode }
    catch { return 0 }
}

function Expect-Denied([scriptblock]$Action, [int]$ExpectedStatus) {
    try {
        & $Action | Out-Null
        throw "Request unexpectedly succeeded."
    }
    catch {
        $status = Get-StatusCode $_
        if ($status -ne $ExpectedStatus) { throw }
    }
}

function Get-ObjectId([string]$ExtensionId, [string]$Key) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($ExtensionId + [char]0 + $Key)
        return "extensionData.v1." + (-join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") }))
    }
    finally { $sha.Dispose() }
}

if (-not (Test-Path $Adb)) { throw "adb not found: $Adb" }

$physical = @(& $Adb devices |
    Select-Object -Skip 1 |
    Where-Object { $_ -match '^\S+\s+device\s*$' } |
    ForEach-Object { ($_ -split '\s+')[0] } |
    Where-Object { $_ -notmatch '^emulator-' })

if ([string]::IsNullOrWhiteSpace($Serial)) {
    if ($physical.Count -ne 1) {
        throw "Expected exactly one connected physical Android device; pass -Serial."
    }
    $Serial = $physical[0]
}
elseif ($Serial -match '^emulator-' -or $physical -notcontains $Serial) {
    throw "Requested physical Android device is not connected."
}

$devPath = @(& $Adb -s $Serial shell pm path $DevPackage 2>$null | Select-Object -First 1)
if ($devPath.Count -eq 0 -or $devPath[0] -notmatch '^package:') {
    throw "Yanzi Dev is not installed on the selected phone."
}

# Read the development login state in memory only. Never print credentials.
[xml]$prefs = (@(& $Adb -s $Serial shell run-as $DevPackage cat shared_prefs/yanzi-mobile.xml)) -join [Environment]::NewLine
$tokenNode = $prefs.SelectSingleNode('/map/string[@name="token"]')
$baseNode = $prefs.SelectSingleNode('/map/string[@name="baseUrl"]')
if (-not $tokenNode -or [string]::IsNullOrWhiteSpace($tokenNode.InnerText)) {
    throw "Yanzi Dev is not logged in."
}
$token = $tokenNode.InnerText
$base = if ($baseNode -and $baseNode.InnerText) { $baseNode.InnerText.TrimEnd('/') } else { $ExpectedBaseUrl }
if ($base -ne $ExpectedBaseUrl) {
    throw "Yanzi Dev is not connected to the public service."
}
$auth = @{ Authorization = "Bearer $token" }

$health = Invoke-RestMethod "$base/health" -TimeoutSec 20
if (-not $health.ok) { throw "Public service health check failed." }

$cleaned = 0
if ($CleanStaleHeadlessDiagnostics) {
    $sync = Invoke-RestMethod "$base/v1/sync/objects" -Headers $auth -TimeoutSec 20
    foreach ($item in @($sync.objects)) {
        if ($item.deleted) { continue }
        $extensionId = [string]$item.payload.extensionId
        $key = [string]$item.payload.key
        if ($extensionId -match '^real-account-storage-headless-\d+$' -and $key -eq 'roundtrip/state.json') {
            $expectedObjectId = Get-ObjectId $extensionId $key
            if ($item.objectId -ne $expectedObjectId) {
                throw "Refusing cleanup because the diagnostic object envelope is inconsistent."
            }
            $body = @{
                schemaVersion = [int]$item.schemaVersion
                expectedRevision = [long]$item.revision
                deleted = $true
                payload = @{}
                updatedByDeviceName = "Yanzi public verification cleanup"
            } | ConvertTo-Json -Depth 8 -Compress
            Invoke-RestMethod "$base/v1/sync/objects/$expectedObjectId" -Method Put -Headers $auth -ContentType "application/json; charset=utf-8" -Body $body -TimeoutSec 20 | Out-Null
            $cleaned++
        }
    }
}

$resourceResponse = Invoke-RestMethod "$base/v1/applications/access-resources" -Headers $auth -TimeoutSec 20
$resources = @($resourceResponse.resources)
$stale = @($resources | Where-Object {
    $_.extensionId -match '^real-account-storage-headless-\d+$' -and $_.key -eq 'roundtrip/state.json'
})
if ($stale.Count -ne 0) {
    throw "Stale headless diagnostic data is still visible in the public access-resource list."
}

$canonical = @($resources | Where-Object {
    ($_.extensionId -eq "quick-notes" -and $_.key -eq "notes.v1.json") -or
    ($_.extensionId -eq "taskbar-calendar" -and $_.key -eq "calendar.v1.json")
})
if ($canonical.Count -ne 2) {
    throw "Expected the canonical notes and calendar resources."
}

$invite = $null
$requestId = $null
$grantRevoked = $false
$inviteRevoked = $false
try {
    $inviteBody = @{
        resources = $canonical
        access = "read-write"
    } | ConvertTo-Json -Depth 12 -Compress
    $invite = Invoke-RestMethod "$base/v1/applications/access-invites" -Method Post -Headers $auth -ContentType "application/json; charset=utf-8" -Body $inviteBody -TimeoutSec 20

    $discovery = Invoke-RestMethod $invite.address -TimeoutSec 20
    $snapshot = @(Invoke-RestMethod ($invite.address + "/resources") -TimeoutSec 20).resources
    if (-not $discovery.authorizationRequired -or @($discovery.resources).Count -ne 2 -or $snapshot.Count -ne 2) {
        throw "Public invitation did not expose the expected two-resource metadata snapshot."
    }

    # Simulate an AI requesting the full two-resource snapshot. The account owner then narrows approval.
    $requestBody = @{
        clientName = "Yanzi public multi-resource verification"
        access = "read-write"
        scopes = "all"
    } | ConvertTo-Json -Depth 10 -Compress
    $request = Invoke-RestMethod ($invite.address + "/requests") -Method Post -ContentType "application/json; charset=utf-8" -Body $requestBody -TimeoutSec 20
    $requestId = [string]$request.requestId

    Start-Sleep -Seconds 3
    $pollHeaders = @{ Authorization = "Bearer $($request.requestSecret)" }
    $pending = Invoke-RestMethod $request.poll.url -Headers $pollHeaders -TimeoutSec 20
    if ($pending.status -ne "pending") {
        throw "External request was not pending before account approval."
    }

    $notesScope = @($canonical | Where-Object { $_.extensionId -eq "quick-notes" } | ForEach-Object {
        [pscustomobject]@{
            extensionId = $_.extensionId
            key = $_.key
            access = "read"
        }
    })
    if ($notesScope.Count -ne 1) { throw "Notes scope was not found." }

    if ($RequireDeviceApproval) {
        Write-Host ("DEVICE_APPROVAL_PENDING: userCode={0}, requestedScopes=2; on the phone leave only Notes selected and tap Allow selected." -f $request.userCode)
        $approved = $null
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            try {
                $candidate = Invoke-RestMethod $request.poll.url -Headers $pollHeaders -TimeoutSec 20
                if ($candidate.status -eq "approved") {
                    $approved = $candidate
                    break
                }
            }
            catch {
                $status = Get-StatusCode $_
                if ($status -eq 403) { throw "Device denied the verification request." }
                if ($status -ne 429) { throw }
            }
        }
        if (-not $approved) { throw "Timed out waiting for device approval." }
    }
    else {
        $decisionBody = @{
            approve = $true
            scopes = $notesScope
        } | ConvertTo-Json -Depth 10 -Compress
        Invoke-RestMethod "$base/v1/applications/access-requests/$requestId/decision" -Method Post -Headers $auth -ContentType "application/json; charset=utf-8" -Body $decisionBody -TimeoutSec 20 | Out-Null
        Start-Sleep -Seconds 3
        $approved = Invoke-RestMethod $request.poll.url -Headers $pollHeaders -TimeoutSec 20
    }
    if ($approved.status -ne "approved" -or @($approved.resources).Count -ne 1) {
        throw "Approved token did not contain exactly the narrowed resource."
    }

    $scoped = @{ Authorization = "Bearer $($approved.accessToken)" }
    $note = Invoke-RestMethod "$base/v1/extension-data/quick-notes?key=notes.v1.json" -Headers $scoped -TimeoutSec 20
    if (-not $note.ok) { throw "Approved notes resource was not readable." }

    Expect-Denied {
        Invoke-RestMethod "$base/v1/extension-data/taskbar-calendar?key=calendar.v1.json" -Headers $scoped -TimeoutSec 20
    } 403

    Invoke-RestMethod "$base/v1/applications/access-grants/$requestId" -Method Delete -Headers $auth -TimeoutSec 20 | Out-Null
    $grantRevoked = $true
    Expect-Denied {
        Invoke-RestMethod "$base/v1/extension-data/quick-notes?key=notes.v1.json" -Headers $scoped -TimeoutSec 20
    } 403

    $secret = ([Uri]$invite.address).Segments[-1].Trim('/')
    Invoke-RestMethod "$base/v1/applications/access-invites/$secret" -Method Delete -Headers $auth -TimeoutSec 20 | Out-Null
    $inviteRevoked = $true

    $pendingAfter = Invoke-RestMethod "$base/v1/applications/access-requests" -Headers $auth -TimeoutSec 20
    $leftover = @($pendingAfter.requests | Where-Object { $_.requestId -eq $requestId })
    if ($leftover.Count -ne 0) { throw "Verification request remained pending after completion." }

    Write-Host ("PUBLIC_MULTI_RESOURCE_ACCESS_PASSED: resources={0}, inviteSnapshot=2, requested=all, approved=1-read-only, unselected=denied, grant=revoked, staleDiagnosticsCleaned={1}, deviceApproval={2}" -f $resources.Count, $cleaned, [bool]$RequireDeviceApproval)
}
finally {
    if ($requestId -and -not $grantRevoked) {
        try {
            Invoke-RestMethod "$base/v1/applications/access-grants/$requestId" -Method Delete -Headers $auth -TimeoutSec 10 | Out-Null
        } catch {}
    }
    if ($invite -and -not $inviteRevoked) {
        try {
            $secret = ([Uri]$invite.address).Segments[-1].Trim('/')
            Invoke-RestMethod "$base/v1/applications/access-invites/$secret" -Method Delete -Headers $auth -TimeoutSec 10 | Out-Null
        } catch {}
    }
}

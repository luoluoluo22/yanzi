param([switch]$SkipLifecycle)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this script with powershell -STA -NoProfile -File scripts/test-capability-runtime.ps1'
}
Add-Type -AssemblyName System.Windows.Forms
$settingsPath = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$apiBase = 'http://127.0.0.1:' + $settings.AgentApiPort
$headers = @{ 'X-Yanzi-Token' = $settings.AgentApiToken }
$testId = [Guid]::NewGuid().ToString('N')
$testUrl = 'https://example.com/yanzi-capability-test/' + $testId
$testDate = '2099-12-30'
$createdIds = New-Object 'System.Collections.Generic.List[string]'
$capturedItem = $null
$clipboardChanged = $false
$clientId = 'capability-test-client-' + $testId
$extensionsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'))
$clientPath = [IO.Path]::GetFullPath((Join-Path $extensionsRoot $clientId))
$checks = New-Object 'System.Collections.Generic.List[string]'
$artifactDirectory = Join-Path $env:TEMP ('YanziDev\capabilities\run-' + $testId)
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

function Assert-Capability($condition, [string]$name) {
    if (-not $condition) { throw ('FAILED: ' + $name) }
    $checks.Add($name)
}
function Invoke-Capability([string]$name, $payload) {
    $body = @{ name = $name; payload = $payload } | ConvertTo-Json -Depth 30 -Compress
    $response = Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities/invoke') -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 20
    if (-not $response.success) { throw ($name + ': ' + $response.error) }
    return $response.data
}
function Wait-Capability([string]$name, [bool]$exists) {
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        try { $list = Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities') -Headers $headers -TimeoutSec 2 }
        catch {
            if ($null -ne $_.Exception.Response) { throw }
            Start-Sleep -Milliseconds 200
            continue
        }
        $found = @($list.capabilities | Where-Object { $_.name -eq $name }).Count -gt 0
        if ($found -eq $exists) { return }
        Start-Sleep -Milliseconds 200
    }
    throw ('Capability availability timeout: ' + $name + ', expected=' + $exists)
}
function Start-CapabilityExtension([string]$id) {
    $body = @{ input = ''; launchSource = 'app-startup' } | ConvertTo-Json -Compress
    $result = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $id + '/run') -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 60
    if (-not $result.success) { throw ('Extension start failed: ' + $id) }
}
function Assert-Rejected([string]$name, $payload, [int]$status) {
    try {
        Invoke-Capability $name $payload | Out-Null
        throw ('Expected failure for ' + $name)
    } catch {
        if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne $status) { throw }
    }
    Assert-Capability $true ($name + ' rejects invalid input with HTTP ' + $status)
}
function Invoke-TestClient([string]$name, $payload) {
    $inputJson = @{ name = $name; payload = $payload } | ConvertTo-Json -Depth 20 -Compress
    $body = @{ input = $inputJson; launchSource = 'api' } | ConvertTo-Json -Compress
    $run = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $clientId + '/run') -Method Post -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 60
    if (-not $run.success) { throw ('C# client execution failed: ' + $run.error) }
    return ($run.output | ConvertFrom-Json)
}

# Materialize all clipboard formats before changing it, preserving images/files as well as text.
$snapshot = New-Object System.Windows.Forms.DataObject
$originalClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
$hasOriginal = $null -ne $originalClipboard
if ($hasOriginal) {
    foreach ($format in $originalClipboard.GetFormats($false)) {
        $data = $originalClipboard.GetData($format, $false)
        if ($null -eq $data) { throw ('Unable to preserve clipboard format: ' + $format) }
        if ($data -is [IO.MemoryStream]) { $data = New-Object IO.MemoryStream(,$data.ToArray()) }
        elseif ($data -is [ICloneable]) { $data = $data.Clone() }
        $snapshot.SetData($format, $false, $data)
    }
}
$success = $false
$failure = $null
try {
    Wait-Capability 'clipboard.search' $true
    Wait-Capability 'calendar.create' $true
    $catalog = Invoke-RestMethod -Uri ($apiBase + '/v1/agent/catalog') -Headers $headers -TimeoutSec 8
    foreach ($providerId in @('clipboard-history', 'taskbar-calendar')) {
        $provider = @($catalog.extensions | Where-Object { $_.id -eq $providerId })[0]
        Assert-Capability ($provider.isRunning -and @($provider.capabilities | Where-Object { $_.available }).Count -gt 0) ($providerId + ' exposes callable contracts in Agent catalog')
        $owned = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $providerId + '/capabilities') -Headers $headers -TimeoutSec 8
        Assert-Capability (@($owned.capabilities | Where-Object { $_.providerExtensionId -eq $providerId -and $_.available }).Count -eq $provider.capabilities.Count) ($providerId + ' individual catalog preserves provider ownership')
    }
    $description = Invoke-Capability 'capability.describe' @{ name = 'clipboard.search' }
    Assert-Capability ($description.providerExtensionId -eq 'clipboard-history') 'real clipboard provider ownership'
    $description = Invoke-Capability 'capability.describe' @{ name = 'calendar.create' }
    Assert-Capability ($description.providerExtensionId -eq 'taskbar-calendar') 'real calendar provider ownership'

    $clipboardChanged = $true
    Invoke-Capability 'clipboard.set' @{ text = $testUrl } | Out-Null
    $found = $null
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $found = Invoke-Capability 'clipboard.search' @{ query = $testId; onlyUrls = $true; limit = 1 }
        if ($found.count -eq 1) { break }
        Start-Sleep -Milliseconds 200
    }
    Assert-Capability ($found.count -eq 1 -and $found.items[0].text -eq $testUrl) 'actual OS clipboard capture and history search'
    $capturedItem = $found.items[0]
    $bounds = Invoke-Capability 'clipboard.search' @{ query = $testId; from = $capturedItem.time }
    Assert-Capability ($bounds.count -eq 1) 'clipboard from boundary is inclusive'
    $bounds = Invoke-Capability 'clipboard.search' @{ query = $testId; to = $capturedItem.time }
    Assert-Capability ($bounds.count -eq 0) 'clipboard to boundary is exclusive'
    $bounds = Invoke-Capability 'clipboard.search' @{ query = $testId; from = '2099-01-01'; to = '2100-01-01' }
    Assert-Capability ($bounds.count -eq 0) 'clipboard date range filtering'
    Assert-Rejected 'clipboard.search' @{ limit = 0 } 400
    Assert-Rejected 'clipboard.search' @{ from = 'invalid-date' } 400
    Assert-Rejected 'calendar.create' @{ date = '2026-02-30'; title = $testUrl } 400
    Assert-Rejected 'calendar.create' @{ date = $testDate; title = 42 } 400

    # Generic runtime fixture lives exclusively under the user's extension directory.
    New-Item -ItemType Directory -Path $clientPath | Out-Null
    $clientManifest = @{ id = $clientId; name = 'Capability test'; version = '1.0.0'; runtime = 'csharp'; entryMode = 'entry'; entry = 'main.cs'; permissions = @('clipboard.read', 'calendar.read'); waitForExit = $true }
    [IO.File]::WriteAllText((Join-Path $clientPath 'manifest.json'), ($clientManifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    $clientSource = @'
using System;
using System.Text.Json;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;
public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        using var document = JsonDocument.Parse(context.InputText);
        var request = document.RootElement;
        try
        {
            var data = await context.Capabilities.InvokeAsync(request.GetProperty("name").GetString()!, request.GetProperty("payload").Clone());
            return JsonSerializer.Serialize(new { success = true, data });
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { success = false, error = ex.Message }); }
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $clientPath 'main.cs'), $clientSource, (New-Object Text.UTF8Encoding($false)))
    $clientResult = Invoke-TestClient 'clipboard.search' @{ query = $testId; onlyUrls = $true }
    Assert-Capability ($clientResult.success -and $clientResult.data.items[0].text -eq $testUrl) 'separate one-shot C# extension invokes live clipboard provider'
    $clientResult = Invoke-TestClient 'calendar.create' @{ date = $testDate; title = $testUrl }
    Assert-Capability (-not $clientResult.success) 'one-shot C# extension without calendar.write cannot create reminders'
    $calls = (Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities/calls') -Headers $headers).calls
    Assert-Capability (@($calls | Where-Object { $_.caller -eq $clientId -and $_.capability -eq 'calendar.create' -and $_.errorCode -eq 'permission_denied' }).Count -gt 0) 'permission rejection retains actual extension caller identity'

    $forward = Invoke-Capability 'clipboard.remind' @{ query = $testId; date = $testDate }
    $createdIds.Add($forward.reminder.id)
    Assert-Capability ($forward.reminder.created -and $forward.reminder.title -eq $testUrl) 'clipboard extension calls calendar extension'
    $reverse = Invoke-Capability 'calendar.fromClipboard' @{ query = $testId; date = $testDate }
    $createdIds.Add($reverse.reminder.id)
    Assert-Capability ($reverse.reminder.created -and $reverse.source.text -eq $testUrl) 'calendar extension calls clipboard extension'
    $calendar = Invoke-Capability 'calendar.list' @{ date = $testDate }
    Assert-Capability (@($calendar.items | Where-Object { $createdIds.Contains($_.id) }).Count -eq 2) 'both created reminders readable in live manager'
    $reminderPath = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\taskbar-calendar\calendar_reminders.json'
    $persisted = Get-Content -LiteralPath $reminderPath -Raw | ConvertFrom-Json
    Assert-Capability (@($persisted | Where-Object { $createdIds.Contains($_.Id) -and $_.Title -eq $testUrl }).Count -eq 2) 'both reminders persisted in real calendar storage'
    $calls = (Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities/calls') -Headers $headers).calls
    Assert-Capability (@($calls | Where-Object { $_.caller -eq 'clipboard-history' -and $_.capability -eq 'calendar.create' -and $_.provider -eq 'taskbar-calendar' -and $_.success }).Count -gt 0) 'forward cross-extension audit attribution'
    Assert-Capability (@($calls | Where-Object { $_.caller -eq 'taskbar-calendar' -and $_.capability -eq 'clipboard.search' -and $_.provider -eq 'clipboard-history' -and $_.success }).Count -gt 0) 'reverse cross-extension audit attribution'

    if (-not $SkipLifecycle) {
        foreach ($entry in @(@{ id = 'clipboard-history'; capability = 'clipboard.search' }, @{ id = 'taskbar-calendar'; capability = 'calendar.create' })) {
            Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $entry.id + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' | Out-Null
            Wait-Capability $entry.capability $false
            $dormant = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $entry.id + '/capabilities') -Headers $headers -TimeoutSec 8
            $declaration = @($dormant.capabilities | Where-Object { $_.name -eq $entry.capability })[0]
            Assert-Capability ($null -ne $declaration -and -not $declaration.available) ($entry.id + ' keeps its declaration but reports unavailable after stop')
            Assert-Capability $true ($entry.id + ' unregisters capabilities on stop')
            Start-CapabilityExtension $entry.id
            Wait-Capability $entry.capability $true
            Assert-Capability $true ($entry.id + ' rebinds capabilities on restart')
        }
        $calendar = Invoke-Capability 'calendar.list' @{ date = $testDate }
        Assert-Capability (@($calendar.items | Where-Object { $createdIds.Contains($_.id) }).Count -eq 2) 'calendar restart reloads persisted reminders'
        $found = Invoke-Capability 'clipboard.search' @{ query = $testId; onlyUrls = $true }
        Assert-Capability ($found.count -eq 1) 'clipboard restart reloads persisted history'
        foreach ($entry in @(@{ id = 'clipboard-history'; capability = 'clipboard.search' }, @{ id = 'taskbar-calendar'; capability = 'calendar.create' })) {
            $reload = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $entry.id + '/reload') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 60
            Assert-Capability ($reload.ok) ($entry.id + ' reload endpoint succeeds')
            Wait-Capability $entry.capability $true
        }
        $calendar = Invoke-Capability 'calendar.list' @{ date = $testDate }
        Assert-Capability (@($calendar.items | Where-Object { $createdIds.Contains($_.id) }).Count -eq 2) 'calendar hot reload retains reminders and binds new runtime'
        $found = Invoke-Capability 'clipboard.search' @{ query = $testId; onlyUrls = $true }
        Assert-Capability ($found.count -eq 1) 'clipboard hot reload retains history and binds new runtime'
    }
    $success = $true
}
catch { $failure = $_.Exception.Message; throw }
finally {
    $cleanupErrors = New-Object 'System.Collections.Generic.List[string]'
    if ($clipboardChanged) {
        foreach ($entry in @(@{ id = 'clipboard-history'; capability = 'clipboard.search' }, @{ id = 'taskbar-calendar'; capability = 'calendar.create' })) {
            try {
                $status = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $entry.id + '/status') -Headers $headers
                if (-not $status.isRunning) { Start-CapabilityExtension $entry.id }
                Wait-Capability $entry.capability $true
            } catch { $cleanupErrors.Add('Provider recovery failed: ' + $entry.id) }
        }
    }
    foreach ($id in $createdIds) {
        try {
            $deleted = Invoke-Capability 'calendar.delete' @{ id = $id }
            if (-not $deleted.deleted) { $cleanupErrors.Add('Reminder not removed: ' + $id) }
        } catch { $cleanupErrors.Add('Reminder cleanup failed: ' + $id) }
    }
    if ($null -ne $capturedItem) {
        try {
            $deleted = Invoke-Capability 'clipboard.delete' @{ text = $capturedItem.text; time = $capturedItem.time }
            if (-not $deleted.deleted) { $cleanupErrors.Add('Test clipboard history not removed') }
            $remaining = Invoke-Capability 'clipboard.search' @{ query = $testId }
            if ($remaining.count -ne 0) { $cleanupErrors.Add('Test clipboard history remains') }
        } catch { $cleanupErrors.Add('Clipboard history cleanup failed') }
    }
    if (Test-Path -LiteralPath $clientPath) {
        if (-not $clientPath.StartsWith($extensionsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($clientPath) -ne $clientId) {
            $cleanupErrors.Add('Fixture cleanup path validation failed')
        } else {
            try { Remove-Item -LiteralPath $clientPath -Recurse -Force } catch { $cleanupErrors.Add('Fixture cleanup failed') }
        }
    }
    try {
        if ($clipboardChanged) {
            if ($hasOriginal) { [System.Windows.Forms.Clipboard]::SetDataObject($snapshot, $true) }
            else { [System.Windows.Forms.Clipboard]::Clear() }
        }
    } catch { $cleanupErrors.Add('Clipboard restore failed') }
    $report = @{ success = ($success -and $cleanupErrors.Count -eq 0); failure = $failure; checks = @($checks.ToArray()); testId = $testId; reminderIds = @($createdIds.ToArray()); cleanupErrors = @($cleanupErrors.ToArray()); timestamp = [DateTimeOffset]::Now.ToString('O') }
    [IO.File]::WriteAllText((Join-Path $artifactDirectory 'result.json'), ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    if ($cleanupErrors.Count -gt 0) { throw ($cleanupErrors -join '; ') }
}
Write-Output ('Real capability runtime test PASSED: ' + $checks.Count + ' checks.')
Write-Output ('Report: ' + (Join-Path $artifactDirectory 'result.json'))

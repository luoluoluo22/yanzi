param([switch]$KeepArtifacts)

$ErrorActionPreference = 'Stop'
$sourceDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tools\idle-chatgpt-tasks'))
$settings = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$apiBase = 'http://127.0.0.1:' + $settings.AgentApiPort
$headers = @{ 'X-Yanzi-Token' = $settings.AgentApiToken }
$extensionsRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
$storageRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage'
$fixtureId = 'idle-task-capability-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$fixtureDirectory = Join-Path $extensionsRoot $fixtureId
$fixtureStorage = Join-Path $storageRoot $fixtureId
$checks = New-Object 'System.Collections.Generic.List[string]'
$started = $false

function Assert-Check($condition, [string]$name) {
    if (-not $condition) { throw ('FAILED: ' + $name) }
    $checks.Add($name)
}
function Get-CapabilityList {
    Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities') -Headers $headers -TimeoutSec 5
}
function Wait-Capability([bool]$exists) {
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $found = @((Get-CapabilityList).capabilities | Where-Object { $_.name -eq 'idle.tasks.list' }).Count -gt 0
        if ($found -eq $exists) { return }
        Start-Sleep -Milliseconds 150
    }
    throw ('Capability availability timeout. expected=' + $exists)
}
function Invoke-Capability($payload) {
    $body = @{ name = 'idle.tasks.list'; payload = $payload } | ConvertTo-Json -Depth 20 -Compress
    $response = Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities/invoke') -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 10
    if (-not $response.success) { throw ('idle.tasks.list failed: ' + $response.error) }
    return $response.data
}

try {
    New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $fixtureStorage -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'IdleTaskApp.cs') -Destination $fixtureDirectory

    $manifest = Get-Content -LiteralPath (Join-Path $sourceDirectory 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest.id = $fixtureId
    $manifest.name = 'Idle task capability verification fixture'
    $manifest.startup = $null
    $manifest.isPublished = $false
    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'manifest.json'), ($manifest | ConvertTo-Json -Depth 50), (New-Object Text.UTF8Encoding($false)))

    $now = [DateTimeOffset]::Now
    $tasks = @(
        [ordered]@{ Id='synthetic-error'; Title='Synthetic failed task'; Kind='chatgpt'; Prompt=''; ExtensionId=$null; ExtensionInput=$null; Tags=@('verification','failure'); RepeatEnabled=$true; Enabled=$true; Status='error'; BridgeJobId=$null; Result=$null; Error='synthetic timeout feedback'; Attempts=2; RunCount=1; LastRunStatus='error'; CreatedAt=$now.AddMinutes(-30).ToString('O'); UpdatedAt=$now.AddMinutes(-5).ToString('O'); QueuedAt=$now.AddMinutes(-29).ToString('O'); StartedAt=$now.AddMinutes(-28).ToString('O'); CompletedAt=$now.AddMinutes(-5).ToString('O'); LastCompletedAt=$now.AddMinutes(-5).ToString('O') },
        [ordered]@{ Id='synthetic-success'; Title='Synthetic successful task'; Kind='extension'; Prompt=''; ExtensionId='taskbar-calendar'; ExtensionInput='{}'; Tags=@('verification'); RepeatEnabled=$false; Enabled=$true; Status='success'; BridgeJobId=$null; Result='synthetic capability result'; Error=$null; Attempts=1; RunCount=1; LastRunStatus='success'; CreatedAt=$now.AddMinutes(-20).ToString('O'); UpdatedAt=$now.AddMinutes(-2).ToString('O'); QueuedAt=$now.AddMinutes(-19).ToString('O'); StartedAt=$now.AddMinutes(-18).ToString('O'); CompletedAt=$now.AddMinutes(-2).ToString('O'); LastCompletedAt=$now.AddMinutes(-2).ToString('O') }
    )
    [IO.File]::WriteAllText((Join-Path $fixtureStorage 'tasks.json'), ($tasks | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))

    $dormant = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/capabilities') -Headers $headers -TimeoutSec 8
    $declared = @($dormant.capabilities | Where-Object { $_.name -eq 'idle.tasks.list' })[0]
    Assert-Check ($null -ne $declared -and -not $declared.available -and $declared.onDemand) 'manifest discovery exposes dormant capability'

    $runBody = @{ input=''; launchSource='app-startup' } | ConvertTo-Json -Compress
    $run = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/run') -Method Post -Headers $headers -ContentType 'application/json' -Body $runBody -TimeoutSec 60
    Assert-Check ($run.success) 'fixture extension starts through Agent API'
    $started = $true
    Wait-Capability $true

    $active = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/capabilities') -Headers $headers -TimeoutSec 8
    $registered = @($active.capabilities | Where-Object { $_.name -eq 'idle.tasks.list' })[0]
    Assert-Check ($registered.available -and $registered.providerExtensionId -eq $fixtureId) 'runtime registration binds capability to extension'

    $errors = Invoke-Capability @{ status='error'; limit=10 }
    Assert-Check ($errors.count -eq 1 -and $errors.items[0].id -eq 'synthetic-error') 'status filter returns only matching task'
    Assert-Check ($errors.items[0].error -eq 'synthetic timeout feedback' -and $errors.items[0].runCount -eq 1) 'failure feedback and run counters are returned'

    $extensions = Invoke-Capability @{ kind='extension'; limit=10 }
    Assert-Check ($extensions.count -eq 1 -and $extensions.items[0].resultPreview -eq 'synthetic capability result') 'kind filter returns execution feedback'

    $calls = (Invoke-RestMethod -Uri ($apiBase + '/v1/capabilities/calls') -Headers $headers -TimeoutSec 8).calls
    Assert-Check (@($calls | Where-Object { $_.capability -eq 'idle.tasks.list' -and $_.provider -eq $fixtureId -and $_.success }).Count -ge 2) 'call log records successful provider feedback'

    Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 20 | Out-Null
    $started = $false
    Wait-Capability $false

    $stopped = Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/capabilities') -Headers $headers -TimeoutSec 8
    $declarationAfterStop = @($stopped.capabilities | Where-Object { $_.name -eq 'idle.tasks.list' })[0]
    Assert-Check ($null -ne $declarationAfterStop -and -not $declarationAfterStop.available -and $declarationAfterStop.onDemand) 'stop unregisters runtime while preserving discoverable declaration'

    Write-Output ('Idle task capability verification PASSED: ' + $checks.Count + ' checks.')
    $checks | ForEach-Object { Write-Output ('  - ' + $_) }
}
finally {
    if ($started) {
        try { Invoke-RestMethod -Uri ($apiBase + '/v1/extensions/' + $fixtureId + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 10 | Out-Null } catch { }
    }
    if (-not $KeepArtifacts) {
        if (Test-Path -LiteralPath $fixtureDirectory) { Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force }
        if (Test-Path -LiteralPath $fixtureStorage) { Remove-Item -LiteralPath $fixtureStorage -Recurse -Force }
    }
}

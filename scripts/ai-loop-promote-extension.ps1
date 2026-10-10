param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9][a-z0-9_-]{2,80}$')][string]$ExtensionId,
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string[]]$Files,
    [Parameter(Mandatory=$true)][string]$VerificationScript,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$installed = Join-Path (Join-Path $base 'Extensions') $ExtensionId
$source = [IO.Path]::GetFullPath($SourceDirectory)
$testFile = [IO.Path]::GetFullPath($VerificationScript)
$time = [DateTimeOffset]::Now.ToString('O')
if (-not (Test-Path -LiteralPath $installed -PathType Container)) { throw "Only existing installed extensions can be promoted: $ExtensionId" }
if (-not (Test-Path -LiteralPath $testFile -PathType Leaf) -or [IO.Path]::GetExtension($testFile) -ne '.ps1') { throw 'A repeatable PowerShell verification script is required.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'manifest.json') -PathType Leaf)) { throw 'Source manifest is required.' }
$oldManifest = Get-Content -LiteralPath (Join-Path $installed 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$newManifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($oldManifest.id -cne $ExtensionId -or $newManifest.id -cne $ExtensionId) { throw 'Manifest identity mismatch, no deployment.' }
if ([version]$newManifest.version -lt [version]$oldManifest.version) { throw 'Downgrades need separate explicit review.' }
$filesToCopy = @($Files | Select-Object -Unique)
if ($filesToCopy -cnotcontains 'manifest.json') { throw 'The explicit file list must include manifest.json.' }
foreach ($entry in $filesToCopy) {
    if ($entry -notmatch '^[a-zA-Z0-9_.-]+(?:[/\\][a-zA-Z0-9_.-]+)*$' -or $entry -match '(^|[/\\])\.\.?([/\\]|$)') { throw "Unsafe relative path: $entry" }
    if ($entry -match '(^|[/\\])(\.env|credentials|secrets|token|tasks\.json|node_modules|obj|bin)([/\\.]|$)') { throw "Protected or generated path: $entry" }
    if (-not (Test-Path -LiteralPath (Join-Path $source $entry) -PathType Leaf)) { throw "Missing source file: $entry" }
}
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
if (-not $testFile.StartsWith($repoRoot + '\',[StringComparison]::OrdinalIgnoreCase) -and
    -not $testFile.StartsWith('F:\Desktop\kaifa\OpenQuickHost-ai-loops\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'The verification script must belong to the repository or an AI-loop worktree.'
}
if (-not $Apply) {
    Write-Output "PLAN_ONLY=True"
    Write-Output "EXTENSION=$ExtensionId"
    Write-Output "INSTALLED_VERSION=$($oldManifest.version)"
    Write-Output "CANDIDATE_VERSION=$($newManifest.version)"
    Write-Output "FILE_COUNT=$($filesToCopy.Count)"
    Write-Output 'No processes stopped, no production files modified.'
    return
}

$settings = Get-Content -LiteralPath (Join-Path $base 'appsettings.local.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $settings.AgentApiToken -or -not $settings.AgentApiPort) { throw 'Agent API unavailable. Refuse unsafe file replacement.' }
$api = 'http://127.0.0.1:' + [string]$settings.AgentApiPort
$headers = @{ 'X-Yanzi-Token' = $settings.AgentApiToken }
$uri = $api + '/v1/extensions/' + [uri]::EscapeDataString($ExtensionId)
$locks = Join-Path $base 'ExtensionStorage\ai-loop-release-locks'
New-Item -ItemType Directory -Path $locks -Force | Out-Null
$lockPath = Join-Path $locks ($ExtensionId + '.lock')
$lock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$backupRoot = Join-Path (Join-Path $base 'AiLoopReleaseBackups') ($ExtensionId + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$changed = $false
$previousRunning = $false
try {
    $status = Invoke-RestMethod -Uri ($uri + '/status') -Headers $headers -TimeoutSec 12
    if (-not $status.ok -or $status.isDeploying) { throw 'Target extension cannot be safely promoted.' }
    $previousRunning = [bool]$status.isRunning

    # A failed preflight must never stop or touch production.
    & $testFile
    if (-not $?) { throw 'Pre-deployment verification failed.' }
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Pre-deployment verification exit code: $LASTEXITCODE" }
    Write-Output "PREFLIGHT_PASS=True"

    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $history = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $filesToCopy) {
        $oldFile = Join-Path $installed $entry
        $exists = Test-Path -LiteralPath $oldFile -PathType Leaf
        $history.Add([pscustomobject]@{ path=$entry; existed=$exists })
        if ($exists) {
            $destination = Join-Path $backupRoot $entry
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath $oldFile -Destination $destination -Force
        }
    }
    $history | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backupRoot 'files.json') -Encoding UTF8
    Write-Output "BACKUP=$backupRoot"

    if ($previousRunning) {
        Invoke-RestMethod -Uri ($uri + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 15 | Out-Null
        $stopped = $false
        for ($i=0; $i -lt 60; $i++) {
            $now = Invoke-RestMethod -Uri ($uri + '/status') -Headers $headers -TimeoutSec 6
            if (-not $now.isRunning) { $stopped = $true; break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $stopped) { throw 'Extension stop timed out; leaving production files unchanged.' }
    }

    $changed = $true
    foreach ($entry in $filesToCopy) {
        $destination = Join-Path $installed $entry
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $entry) -Destination $destination -Force
    }
    $run = Invoke-RestMethod -Uri ($uri + '/run') -Method Post -Headers $headers -ContentType 'application/json' -Body '{"input":"","launchSource":"app-startup"}' -TimeoutSec 60
    if (-not $run.success) { throw 'Promoted extension did not start through Agent API.' }
    $healthy = $false
    for ($i=0; $i -lt 24; $i++) {
        $now = Invoke-RestMethod -Uri ($uri + '/status') -Headers $headers -TimeoutSec 6
        if ($now.isRunning -and $now.version -eq $newManifest.version) { $healthy=$true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) { throw 'Installed version or runtime state did not pass health probe.' }
    if (-not $previousRunning) {
        Invoke-RestMethod -Uri ($uri + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 15 | Out-Null
    }
    $changed = $false
    [pscustomobject]@{ extensionId=$ExtensionId; previousVersion=$oldManifest.version; newVersion=$newManifest.version; timestamp=$time; status='production-health-passed'; backup=$backupRoot } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backupRoot 'release.json') -Encoding UTF8
    Write-Output "PRODUCTION_ACCEPTED=True"
    Write-Output "EXTENSION=$ExtensionId"
    Write-Output "NEW_VERSION=$($newManifest.version)"
}
catch {
    $reason = $_.Exception.Message
    if ($changed) {
        try {
            Invoke-RestMethod -Uri ($uri + '/stop') -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 10 | Out-Null
            Start-Sleep -Seconds 1
            foreach ($entry in $history) {
                $target = Join-Path $installed $entry.path
                if ($entry.existed) { Copy-Item -LiteralPath (Join-Path $backupRoot $entry.path) -Destination $target -Force }
                elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
            }
            if ($previousRunning) { Invoke-RestMethod -Uri ($uri + '/run') -Method Post -Headers $headers -ContentType 'application/json' -Body '{"input":"","launchSource":"app-startup"}' -TimeoutSec 60 | Out-Null }
            Write-Warning 'Promotion failed. Previous extension files were restored.'
        } catch { Write-Warning ("Rollback requires immediate investigation: " + $_.Exception.Message) }
    } elseif ($previousRunning) {
        try {
            $now=Invoke-RestMethod -Uri ($uri+'/status') -Headers $headers -TimeoutSec 6
            if (-not $now.isRunning) { Invoke-RestMethod -Uri ($uri+'/run') -Method Post -Headers $headers -ContentType 'application/json' -Body '{"input":"","launchSource":"app-startup"}' -TimeoutSec 60 | Out-Null }
        } catch {}
    }
    throw ("PRODUCTION_REJECTED: " + $reason)
}
finally {
    $lock.Dispose()
}

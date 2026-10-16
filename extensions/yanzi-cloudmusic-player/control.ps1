# This script remains for legacy shortcuts and personal maintenance commands.
# Ordinary playback controls must target the authorized personal extension,
# never the system-wide NetEase desktop player's media keys.
param(
    [ValidateSet('toggle','play','pause','next','prev','status','favorites','recommend','daemon-start','daemon-stop','sync')]
    [string]$Action = 'toggle'
)

$ErrorActionPreference = 'Stop'
$extensionId = 'yanzi-cloudmusic-player'
$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$settingsFile = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json'
$pidFile = Join-Path $baseDir 'sync_daemon.pid'

try {
    if ($Action -eq 'daemon-stop') {
        if (Test-Path $pidFile) {
            $pidText = [IO.File]::ReadAllText($pidFile).Trim()
            if ($pidText -match '^\d+$') {
                $process = Get-Process -Id ([int]$pidText) -ErrorAction SilentlyContinue
                if ($process -and ($process.ProcessName -in @('python','pythonw'))) {
                    Stop-Process -Id $process.Id -Force -ErrorAction Stop
                }
            }
            Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
        }
        Write-Output 'OK: daemon-stopped'
        exit 0
    }

    if ($Action -eq 'daemon-start') {
        $daemonScript = Join-Path $baseDir 'sync_daemon.py'
        if (-not (Test-Path $daemonScript)) {
            throw 'Missing sync_daemon.py'
        }
        $existing = $false
        if (Test-Path $pidFile) {
            $pidText = [IO.File]::ReadAllText($pidFile).Trim()
            if ($pidText -match '^\d+$') {
                $existing = [bool](Get-Process -Id ([int]$pidText) -ErrorAction SilentlyContinue)
            }
        }
        if (-not $existing) {
            Start-Process -FilePath 'python.exe' -ArgumentList @($daemonScript) -WindowStyle Hidden
        }
        Write-Output 'OK: daemon-started'
        exit 0
    }

    if ($Action -eq 'sync') {
        $syncScript = Join-Path $baseDir 'hot_sync.py'
        if (-not (Test-Path $syncScript)) { throw 'Missing hot_sync.py' }
        $process = Start-Process -FilePath 'python.exe' -ArgumentList @($syncScript) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Sync failed, exit code: $($process.ExitCode)" }
        Write-Output 'OK: sync'
        exit 0
    }

    if (-not (Test-Path $settingsFile)) { throw 'Yanzi configuration not found' }
    $settings = [IO.File]::ReadAllText($settingsFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if (-not $settings.agentApiPort -or -not $settings.agentApiToken) {
        throw 'Yanzi local API is not configured'
    }

    $url = 'http://127.0.0.1:' + $settings.agentApiPort + '/v1/capabilities/invoke'
    $headers = @{ 'X-Yanzi-Token' = [string]$settings.agentApiToken }
    $payload = @{
        name = 'extension.action'
        payload = @{ target = $extensionId; action = $Action }
    } | ConvertTo-Json -Depth 4 -Compress
    $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $payload -TimeoutSec 48
    if (-not $response -or -not $response.success -or -not $response.data -or $response.data.ok -ne $true) {
        $reason = if ($response.error) { [string]$response.error }
                  elseif ($response.data.error) { [string]$response.data.error }
                  else { 'The extension did not confirm playback' }
        throw "Music command rejected: $reason"
    }

    Write-Output ("OK: {0}, playing={1}, songId={2}, position={3}" -f $Action, $response.data.playing, $response.data.songId, $response.data.position)
    exit 0
}
catch {
    [Console]::Error.WriteLine("ERROR: " + $_.Exception.Message)
    exit 1
}

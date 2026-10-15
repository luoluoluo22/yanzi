param(
    [string]$Action = "next"
)

$extensionId = "yanzi-cloudmusic-player"
$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $baseDir "app"
$controlFile = Join-Path $appDir "control.json"
$settingsFile = "$env:LOCALAPPDATA\OpenQuickHost\appsettings.local.json"
$daemonScript = Join-Path $baseDir "sync_daemon.py"
$pidFile = Join-Path $baseDir "sync_daemon.pid"

# 1. 停止监听守护
if ($Action -eq "daemon-stop") {
    if (Test-Path $pidFile) {
        $daemonPid = (Get-Content $pidFile -Raw)
        if ($daemonPid) {
            $proc = Get-Process -Id ([int]$daemonPid.Trim()) -ErrorAction SilentlyContinue
            if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        }
        Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
    }
    Write-Output "OK: daemon-stopped"
    exit 0
}

# 2. 启动监听守护
if ($Action -eq "daemon-start") {
    $needStart = $true
    if (Test-Path $pidFile) {
        $daemonPid = (Get-Content $pidFile -Raw)
        if ($daemonPid) {
            $proc = Get-Process -Id ([int]$daemonPid.Trim()) -ErrorAction SilentlyContinue
            if ($proc) { $needStart = $false }
        }
    }
    if ($needStart -and (Test-Path $daemonScript)) {
        Start-Process -FilePath "python.exe" -ArgumentList "`"$daemonScript`"" -WindowStyle Hidden
    }
    Write-Output "OK: daemon-started"
    exit 0
}

# 3. 如果是显式 hot_sync 指令，立即执行一次全量扫描
if ($Action -eq "sync") {
    $syncScript = Join-Path $baseDir "hot_sync.py"
    if (Test-Path $syncScript) {
        Start-Process -FilePath "python.exe" -ArgumentList "`"$syncScript`"" -WindowStyle Hidden -Wait
    }
}

# 4. 写入指令文件
$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$cmdJson = "{`"action`":`"$Action`",`"time`":$timestamp}"
[System.IO.File]::WriteAllText($controlFile, $cmdJson, [System.Text.Encoding]::UTF8)

# 5. 检查小程序是否在运行，若未运行则通过燕子 API 自动唤醒
if (Test-Path $settingsFile) {
    $settingsRaw = [System.IO.File]::ReadAllText($settingsFile, [System.Text.Encoding]::UTF8)
    $settings = $settingsRaw | ConvertFrom-Json
    $port = $settings.agentApiPort
    $token = $settings.agentApiToken

    if ($port) {
        if ($token) {
            $apiUrl = "http://127.0.0.1:$port/v1/capabilities/invoke"
            $headers = @{
                "X-Yanzi-Token" = $token
                "Content-Type" = "application/json; charset=utf-8"
            }

            $statusPayload = "{`"name`":`"extension.status`",`"payload`":{`"target`":`"$extensionId`"}}"
            $statusResp = Invoke-RestMethod -Uri $apiUrl -Method Post -Headers $headers -Body $statusPayload -TimeoutSec 2 -ErrorAction SilentlyContinue

            $isRunning = $false
            if ($statusResp) {
                if ($statusResp.data) {
                    $isRunning = [bool]$statusResp.data.isRunning
                }
            }

            if (-not $isRunning) {
                $openPayload = "{`"name`":`"extension.open`",`"payload`":{`"target`":`"$extensionId`"}}"
                $null = Invoke-RestMethod -Uri $apiUrl -Method Post -Headers $headers -Body $openPayload -TimeoutSec 2 -ErrorAction SilentlyContinue
            }
        }
    }
}

Write-Output "OK: $Action"

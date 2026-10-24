param([string]$InputText = '', [string]$ContextPath = '')
$ErrorActionPreference = 'Stop'
$port = 43820
$baseUrl = 'http://127.0.0.1:' + $port
$dataDir = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\ego-browser-api'
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

$existing = $null
try { $existing = Invoke-RestMethod -Uri ($baseUrl + '/health') -TimeoutSec 2 } catch {}
if ($existing) {
    if ($existing.service -eq 'ego-browser-api') {
        Write-Output ('Ego Browser API 已运行：' + $baseUrl)
        exit 0
    }
    throw ('端口 ' + $port + ' 已被其他服务占用，拒绝覆盖。')
}

$nodePath = $null
$systemNode = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $systemNode) { $systemNode = Get-Command node -ErrorAction SilentlyContinue }
if ($systemNode) {
    $candidate = $systemNode.Source
    try {
        $versionText = (& $candidate --version | Out-String).Trim()
        $majorText = $versionText.TrimStart('v').Split('.')[0]
        if ([int]$majorText -ge 22) { $nodePath = $candidate }
    } catch {}
}

if (-not $nodePath) {
    $runtimeVersion = '24.22.0'
    $runtimeRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Runtimes'
    $runtimeFolder = 'node-v' + $runtimeVersion + '-win-x64'
    $runtimeDir = Join-Path $runtimeRoot $runtimeFolder
    $nodePath = Join-Path $runtimeDir 'node.exe'
    if (-not (Test-Path $nodePath)) {
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
        $zipName = $runtimeFolder + '.zip'
        $base = 'https://nodejs.org/dist/v' + $runtimeVersion + '/'
        $zipPath = Join-Path $dataDir $zipName
        $hashPath = Join-Path $dataDir 'SHASUMS256.txt'
        Invoke-WebRequest -Uri ($base + $zipName) -OutFile $zipPath -UseBasicParsing -TimeoutSec 120
        Invoke-WebRequest -Uri ($base + 'SHASUMS256.txt') -OutFile $hashPath -UseBasicParsing -TimeoutSec 30
        $hashLine = Select-String -Path $hashPath -Pattern ('\s' + [regex]::Escape($zipName) + '$') | Select-Object -First 1
        if (-not $hashLine) { throw '未在官方 SHASUMS256.txt 中找到 Node.js 压缩包校验值。' }
        $expectedHash = (($hashLine.Line -split '\s+')[0]).ToLowerInvariant()
        $actualHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
            throw 'Node.js 压缩包 SHA-256 校验失败，已拒绝安装。'
        }
        $extractRoot = Join-Path $runtimeRoot ('extract-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
        Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force
        $extracted = Join-Path $extractRoot $runtimeFolder
        if (-not (Test-Path (Join-Path $extracted 'node.exe'))) { throw 'Node.js 压缩包解压后缺少 node.exe。' }
        if (Test-Path $runtimeDir) { Remove-Item $runtimeDir -Recurse -Force }
        Move-Item -Path $extracted -Destination $runtimeDir
        Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item $hashPath -Force -ErrorAction SilentlyContinue
    }
}
if (-not (Test-Path $nodePath)) { throw '没有找到可用的 Node.js 22+ 运行时。' }

$serverPath = Join-Path $PSScriptRoot 'server.mjs'
if (-not (Test-Path $serverPath)) { throw ('缺少 API 入口：' + $serverPath) }
$env:EGO_BROWSER_API_DATA_DIR = $dataDir
$env:EGO_BROWSER_API_PORT = [string]$port
$stdoutLog = Join-Path $dataDir 'service.stdout.log'
$stderrLog = Join-Path $dataDir 'service.stderr.log'
Start-Process -FilePath $nodePath -ArgumentList @('"' + $serverPath + '"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog | Out-Null

$healthy = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    Start-Sleep -Milliseconds 250
    try {
        $health = Invoke-RestMethod -Uri ($baseUrl + '/health') -TimeoutSec 1
        if ($health.service -eq 'ego-browser-api') { $healthy = $true; break }
        if ($health.service) { throw ('端口被其他服务占用：' + $health.service) }
    } catch {}
}
if (-not $healthy) {
    $tail = ''
    if (Test-Path $stderrLog) { $tail = (Get-Content $stderrLog -Tail 30 -ErrorAction SilentlyContinue) -join [Environment]::NewLine }
    throw ('Ego Browser API 未能在 10 秒内启动。日志：' + $stderrLog + '。' + $tail)
}
Write-Output ('Ego Browser API 已启动：' + $baseUrl)
Write-Output ('Node.js：' + (& $nodePath --version))

# Antigravity CLI per-device bootstrap. Only the official Google HTTPS installer is used.
param(
    [ValidateSet('check', 'install', 'update')]
    [string]$Mode = 'check'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($env:OS -ne 'Windows_NT') {
    [Console]::Error.WriteLine('Antigravity CLI 自动部署目前只支持 Windows。')
    exit 1
}

function Get-AgyPath {
    $localBinary = Join-Path $env:LOCALAPPDATA 'agy\bin\agy.exe'
    if (Test-Path -LiteralPath $localBinary -PathType Leaf) { return $localBinary }
    foreach ($name in @('agy.exe', 'agy')) {
        $found = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found -and $found.Source -and (Test-Path -LiteralPath $found.Source -PathType Leaf)) { return $found.Source }
    }
    return $null
}

function Confirm-Agy([string]$binary) {
    if (-not $binary) { throw '没有找到 Antigravity CLI (agy.exe)。' }
    $v = & $binary --version 2>&1
    if ($LASTEXITCODE -ne 0) { throw '反重力 CLI 无法执行 --version。' }
    $version = ($v | Out-String).Trim()
    if (-not $version) { throw '反重力 CLI 没有返回版本号。' }
    Write-Output ("反重力 CLI 已安装：{0}" -f $version)
    Write-Output ("执行文件：{0}" -f $binary)
}

$mutex = $null
$locked = $false
$tempFile = $null
try {
    $current = Get-AgyPath
    if ($Mode -eq 'check') {
        if (-not $current) {
            Write-Output '反重力 CLI 尚未安装，点击小程序或发送 /部署 即可安装。'
            exit 2
        }
        Confirm-Agy $current
        exit 0
    }
    # Cross-process protection: do not race two installs/updates on the same computer.
    $mutex = [System.Threading.Mutex]::new($false, 'Local\YanziAntigravityCliSetup')
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw '另一项反重力部署或更新正在进行，请稍后重试。' }

    $current = Get-AgyPath
    if ($current -and $Mode -eq 'install') {
        Confirm-Agy $current
        Write-Output '现有版本可用，未重复下载安装。'
        exit 0
    }
    if ($current -and $Mode -eq 'update') {
        Write-Output '正在调用 Google 官方 agy update ...'
        & $current update
        if ($LASTEXITCODE -ne 0) { throw 'Google CLI 更新命令失败。' }
    }
    else {
        $url = 'https://antigravity.google/cli/install.ps1'
        $tempFile = Join-Path ([IO.Path]::GetTempPath()) ("yanzi-antigravity-setup-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
        Write-Output '正在从 Google 官方 HTTPS 地址下载 Windows CLI 安装器...'
        Invoke-WebRequest -Uri $url -OutFile $tempFile -MaximumRedirection 3 -ErrorAction Stop
        if (-not (Test-Path -LiteralPath $tempFile -PathType Leaf) -or (Get-Item -LiteralPath $tempFile).Length -lt 200) {
            throw '官方安装脚本未成功下载。'
        }
        Write-Output '正在安装 CLI 到当前 Windows 用户环境...'
        $powershell = Join-Path $PSHOME 'powershell.exe'
        & $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $tempFile
        if ($LASTEXITCODE -ne 0) { throw "Google 官方安装器失败，退出码 $LASTEXITCODE。" }
    }
    # Never trust installer exit code alone.
    Confirm-Agy (Get-AgyPath)
    Write-Output '部署校验通过。首次使用需要在本机 /登录 完成 Google 账号授权。'
    exit 0
}
catch {
    [Console]::Error.WriteLine("反重力环境部署失败：$($_.Exception.Message)")
    exit 1
}
finally {
    if ($tempFile -and (Test-Path -LiteralPath $tempFile)) {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }
    if ($locked -and $mutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}

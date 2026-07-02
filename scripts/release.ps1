# 燕子 (Yanzi) 一键自动编译与发布总控脚本
# 用法: 在配置好 $env:GITHUB_TOKEN 后，在根目录直接运行 .\scripts\release.ps1

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"

# 1. 自动提取版本号
if (Test-Path $projectPath) {
    [xml]$xml = Get-Content $projectPath
    $version = $xml.Project.PropertyGroup.Version.Trim()
} else {
    throw "Project file not found at: $projectPath"
}

Write-Host "==============================================" -ForegroundColor Green
Write-Host "  🚀 Starting One-Click Release for Yanzi v$version" -ForegroundColor Green
Write-Host "==============================================" -ForegroundColor Green

# 2. 智能提取 GITHUB_TOKEN
if ([string]::IsNullOrEmpty($env:GITHUB_TOKEN)) {
    $ghToken = gh auth token 2>$null
    if (-not [string]::IsNullOrEmpty($ghToken)) {
        $env:GITHUB_TOKEN = $ghToken.Trim()
        Write-Host "Automatically retrieved GITHUB_TOKEN from gh CLI."
    } else {
        throw "Environment variable GITHUB_TOKEN is not configured! Please configure it or login to gh CLI before running this script."
    }
}

# 3. 网络与代理兜底环境变量 (规避 Go http2 握手 EOF，完美复用本地代理)
$env:GODEBUG = "http2client=0"
Write-Host "Injected GODEBUG=http2client=0 to bypass connection reset issues."

# 4. 执行第一步：打包编译
Write-Host "`n[Step 1/2] Building and packaging installer..." -ForegroundColor Cyan
powershell -File (Join-Path $PSScriptRoot "publish-installer.ps1") -Version $version

# 5. 执行第二步：上传与 API 自动中文化补丁
Write-Host "`n[Step 2/2] Uploading installer assets and patching Chinese release notes..." -ForegroundColor Cyan
powershell -File (Join-Path $PSScriptRoot "upload-release-installer.ps1") -Version $version -KeepProxy

Write-Host "`n🎉 Release v$version completed successfully! All assets uploaded and release patched to Chinese." -ForegroundColor Green

﻿# 燕子 (Yanzi) 一键全自动编译与极简发布总控脚本
# 用法:
#   1. 全自动非交互: .\scripts\release.ps1 -Version "0.3.7" -Notes "更新说明..."
#   2. 交互式向导:   .\scripts\release.ps1

param(
    [string]$Version = "",
    [string]$Notes = "",
    [string]$Platform = "windows",
    [switch]$NoPush,
    [switch]$KeepProxy,
    [string]$GithubToken = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

# 1. 解析当前版本号与推荐版本号
[xml]$xml = Get-Content $projectPath
$currentVersion = $xml.Project.PropertyGroup.Version.Trim()

$suggestedVersion = $currentVersion
if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3] + 1
    $suggestedVersion = "$major.$minor.$patch"
}

# 2. 版本号交互确认 (若未传入 -Version 参数)
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "==============================================" -ForegroundColor Cyan
    Write-Host "  Yanzi 自动化发布向导" -ForegroundColor Cyan
    Write-Host "  当前项目版本: $currentVersion"
    Write-Host "=============================================="
    $inputVer = Read-Host "请输入发布版本号 [直接回车使用建议版本: $suggestedVersion]"
    $Version = if ([string]::IsNullOrWhiteSpace($inputVer)) { $suggestedVersion } else { $inputVer.Trim() }
}
$plainVersion = if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) { $Version.Substring(1) } else { $Version }

# 3. 自动同步更新 csproj 版本号
if ($currentVersion -ne $plainVersion) {
    Write-Host "正在自动更新 csproj 项目版本号 -> $plainVersion ..." -ForegroundColor Yellow
    $csprojContent = Get-Content -Path $projectPath -Raw -Encoding UTF8
    $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$plainVersion</Version>"
    $csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$plainVersion.0</FileVersion>"
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$plainVersion.0</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$plainVersion</InformationalVersion>"
    [System.IO.File]::WriteAllText($projectPath, $csprojContent, [System.Text.Encoding]::UTF8)
    Write-Host "csproj 版本号更新完成。" -ForegroundColor Green
}

# 4. 更新说明处理 (优先使用 -Notes 参数，次优读取 RELEASE_NOTES.md，最后弹出记事本)
$notesFile = Join-Path $root "RELEASE_NOTES.md"
if ([string]::IsNullOrWhiteSpace($Notes)) {
    if (Test-Path $notesFile) {
        $rawNotes = (Get-Content $notesFile -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($rawNotes) -and $rawNotes -notmatch "请在此编辑更新说明") {
            $Notes = $rawNotes
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Notes)) {
    $template = "# 燕子 Yanzi v$plainVersion 更新内容`n`n**✨ 新增与优化**`n- 优化系统交互体验与运行稳定性。`n`n**🐛 修复问题**`n- 修复已知问题。`n`n---`n一键安装包：Yanzi-win-Setup-$plainVersion.exe"
    [System.IO.File]::WriteAllText($notesFile, $template, [System.Text.Encoding]::UTF8)
    Write-Host "`n已为您自动创建更新说明文件: RELEASE_NOTES.md" -ForegroundColor Yellow
    Write-Host "正在打开记事本，请在记事本中编辑更新日志，保存并【关闭记事本】后将自动继续发布..." -ForegroundColor Yellow
    $proc = Start-Process notepad.exe -ArgumentList $notesFile -PassThru
    $proc.WaitForExit()
    if (Test-Path $notesFile) {
        $Notes = (Get-Content $notesFile -Raw).Trim()
    }
}

# 5. Token 与环境变量
$token = if ($GithubToken) { $GithubToken } elseif ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { "" }
if ([string]::IsNullOrWhiteSpace($token)) {
    $ghToken = gh auth token 2>$null
    if (-not [string]::IsNullOrWhiteSpace($ghToken)) {
        $token = $ghToken.Trim()
        $env:GITHUB_TOKEN = $token
    }
}

# 6. 第一步：本地增量打包编译 (自动利用本地已有 full.nupkg 进行秒级增量差分比对)
Write-Host "`n[1/3] 正在执行本地快速增量打包编译 (v$plainVersion)..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "publish-installer.ps1") -Version $plainVersion -GithubToken $token
if ($LASTEXITCODE -ne 0) {
    throw "打包编译失败，发布中断。"
}

# 7. 第二步：Git 提交与同步推送
if (-not $NoPush) {
    Write-Host "`n[2/3] 正在提交版本变动并推送到 GitHub (main 分支)..." -ForegroundColor Cyan
    git add .
    git commit -m "release: v$plainVersion" --allow-empty
    git push origin main
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Git push main 未能成功完成，继续尝试上传 Release 资产..."
    }
}

# 8. 第三步：上传 Release 资产并以 UTF-8 API 注入中文更新日志
Write-Host "`n[3/3] 正在上传 Release 资产并发布中文更新说明..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "upload-release-installer.ps1") -Version $plainVersion -Notes $Notes -KeepProxy -GithubToken $token
if ($LASTEXITCODE -ne 0) {
    throw "上传发布资产失败。"
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "  🎉 恭喜！Yanzi v$plainVersion 已成功发布上线！" -ForegroundColor Green
Write-Host "  🔗 发布地址: https://github.com/luoluoluo22/yanzi/releases/tag/v$plainVersion" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

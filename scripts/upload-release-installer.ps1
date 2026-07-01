param(
    [string]$Version = "0.1.0",
    [string]$Platform = "windows", # windows 或 android
    [string]$Repo = "luoluoluo22/yanzi",
    [string]$Target = "main",
    [string]$InstallerPath = "",
    [switch]$Draft,
    [switch]$KeepProxy
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$plainVersion = if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) { $Version.Substring(1) } else { $Version }

$tag = if ($Platform -eq "android") { "android-v$plainVersion" } else { "v$plainVersion" }

$installerOutDir = Join-Path $root ".artifacts\installer"

if ($Platform -eq "android") {
    $fileName = "yanzi-mobile-$plainVersion.apk"
    $installerSetupPath = if ($InstallerPath) { Resolve-Path $InstallerPath } else { Join-Path $installerOutDir $fileName }
    if (!(Test-Path -LiteralPath $installerSetupPath)) {
        # 后备方案：去 Gradle 默认输出目录寻找并复制过来
        $gradleReleaseApk = Join-Path $root "mobile\android\app\build\outputs\apk\release\app-release.apk"
        $mvpApk = Join-Path $root "mobile\android\app\build\manual-release\yanzi-mobile-release.apk"
        $mvpDebugApk = Join-Path $root "mobile\android\app\build\manual-debug\yanzi-mobile-debug.apk"
        
        if (Test-Path $gradleReleaseApk) {
            $installerSetupPath = $gradleReleaseApk
        } elseif (Test-Path $mvpApk) {
            $installerSetupPath = $mvpApk
        } elseif (Test-Path $mvpDebugApk) {
            $installerSetupPath = $mvpDebugApk
        } else {
            throw "Android APK not found. Build Android app first or pass -InstallerPath."
        }
    }

    # 确保文件拷贝到了输出目录中且重命名为标准的包名
    $targetApkPath = Join-Path $installerOutDir $fileName
    if ((Resolve-Path $installerSetupPath) -ne (Resolve-Path $targetApkPath -ErrorAction SilentlyContinue)) {
        if (!(Test-Path $installerOutDir)) { New-Item -ItemType Directory -Path $installerOutDir -Force | Out-Null }
        Copy-Item -LiteralPath $installerSetupPath -Destination $targetApkPath -Force
        $installerSetupPath = $targetApkPath
    }
} else {
    $fileName = "Yanzi-win-Setup-$plainVersion.exe"
    $installerSetupPath = if ($InstallerPath) { Resolve-Path $InstallerPath } else { Join-Path $installerOutDir $fileName }
    if (!(Test-Path -LiteralPath $installerSetupPath)) {
        throw "$fileName not found under $installerOutDir. Run scripts\publish-installer.ps1 first."
    }
}

if (-not $KeepProxy) {
    $env:HTTP_PROXY = ""
    $env:HTTPS_PROXY = ""
    $env:ALL_PROXY = ""
    $env:NO_PROXY = ""
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "GitHub CLI was not found. Install gh first: https://cli.github.com/"
}

gh api user --jq .login | Out-Host

$hash = (Get-FileHash -LiteralPath $installerSetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$notesPath = Join-Path ([IO.Path]::GetTempPath()) "yanzi-release-$plainVersion.md"

$notesContent = if ($Platform -eq "android") {
@"
# 燕子 Yanzi for Android v$plainVersion 更新内容

**✨ 移动端优化**
- 手机端自动检查更新功能上线。
- 优化了燕幕同步机制与运行日志显示。
- 修复了已知的部分闪退问题。

---
安装包：$fileName

SHA256: $hash
"@
} else {
@"
# 燕子 Yanzi v$plainVersion 更新内容

**✨ 界面与交互优化**
- 【修复】设置界面第二次打开时无响应或报错退出的问题。
- 【修复】初次呼出设置面板瞬间出现的白屏/闪屏问题，改为全局底色提前渲染。
- 【性能】彻底重构了设置面板的生命周期为“后台常驻”，首次加载后每次打开均为秒级展现，不再有由于大量数据加载带来的鼠标卡顿感。

**🎛️ 轮盘功能升级**
- 【增强】调整了燕环（轮盘）的“目标应用”智能判定逻辑。过去仅依赖“前台激活窗口”；现在已支持自动嗅探并提取**鼠标指针悬停位置**下的窗口进程。即使它处于后台非激活状态，依然能精准唤出针对该应用的专属轮盘工具。

---
一键安装包：$fileName

SHA256: $hash
"@
}
[System.IO.File]::WriteAllText($notesPath, $notesContent, [System.Text.Encoding]::UTF8)

$releaseExists = $true
$oldEAP = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
gh release view $tag --repo $Repo *> $null
if ($LASTEXITCODE -ne 0) {
    $releaseExists = $false
}
$ErrorActionPreference = $oldEAP

$releaseTitle = if ($Platform -eq "android") { "Yanzi for Android $plainVersion" } else { "Yanzi $plainVersion" }

if (-not $releaseExists) {
    gh release create $tag `
        --repo $Repo `
        --target $Target `
        --title $releaseTitle `
        --notes-file $notesPath `
        --draft | Out-Host
} else {
    gh release edit $tag `
        --repo $Repo `
        --title $releaseTitle `
        --notes-file $notesPath | Out-Host
}

$filesToUpload = if ($Platform -eq "android") {
    Get-ChildItem -Path $installerOutDir -File | Where-Object { $_.Name.Contains($plainVersion) -and $_.Name.EndsWith(".apk") }
} else {
    Get-ChildItem -Path $installerOutDir -File | Where-Object { $_.Name.Contains($plainVersion) -or $_.Name -match "releases\.win\.json|RELEASES" }
}
foreach ($file in $filesToUpload) {
    $maxRetries = 5
    $retryCount = 0
    $uploaded = $false

    while (-not $uploaded -and $retryCount -lt $maxRetries) {
        $retryCount++
        try {
            Write-Host "Uploading $($file.Name) to Release $tag... (Attempt $retryCount/$maxRetries)"
            
            $oldEAP = $ErrorActionPreference
            $ErrorActionPreference = "Stop"
            
            & gh release upload $tag $file.FullName --repo $Repo --clobber
            
            $ErrorActionPreference = $oldEAP
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Successfully uploaded $($file.Name)."
                $uploaded = $true
            } else {
                throw "gh release upload returned non-zero exit code: $LASTEXITCODE"
            }
        } catch {
            Write-Warning ("Failed to upload {0} on attempt {1}: {2}" -f $file.Name, $retryCount, $_)
            if ($retryCount -lt $maxRetries) {
                Write-Host "Waiting 5 seconds before retrying..."
                Start-Sleep -Seconds 5
            } else {
                throw "Failed to upload $($file.Name) after $maxRetries attempts."
            }
        }
    }
}

if (-not $Draft) {
    gh release edit $tag --repo $Repo --draft=false | Out-Host
}

gh release view $tag --repo $Repo --json tagName,name,isDraft,url,assets | Out-Host

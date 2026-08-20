param(
    [string]$Version = "0.1.0",
    [string]$Platform = "windows", # windows 或 android
    [string]$Repo = "luoluoluo22/yanzi",
    [string]$Target = "main",
    [string]$InstallerPath = "",
    [switch]$Draft,
    [switch]$KeepProxy,
    [string]$GithubToken = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrEmpty($Version) -or $Version -eq "0.1.0") {
    $projectPath = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"
    if (Test-Path $projectPath) {
        [xml]$xml = Get-Content $projectPath
        $Version = $xml.Project.PropertyGroup.Version.Trim()
        Write-Host "Auto-detected version from csproj: $Version"
    }
}
$plainVersion = if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) { $Version.Substring(1) } else { $Version }

$tag = if ($Platform -eq "android") { "android-v$plainVersion" } else { "v$plainVersion" }

$installerOutDir = Join-Path $root ".artifacts\installer"

if ($Platform -eq "android") {
    $fileName = "yanzi-mobile-$plainVersion.apk"
    $installerSetupPath = if ($InstallerPath) { Resolve-Path $InstallerPath } else { Join-Path $installerOutDir $fileName }
    if (!(Test-Path -LiteralPath $installerSetupPath)) {
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

    $targetApkPath = Join-Path $installerOutDir $fileName
    if ((Resolve-Path $installerSetupPath) -ne (Resolve-Path $targetApkPath -ErrorAction SilentlyContinue)) {
        if (!(Test-Path $installerOutDir)) { New-Item -ItemType Directory -Path $installerOutDir -Force | Out-Null }
        Copy-Item -LiteralPath $installerSetupPath -Destination $targetApkPath -Force
        $installerSetupPath = $targetApkPath
    }

    $sdkPath = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "F:\SDK" }
    $apkSigner = Get-ChildItem -Path (Join-Path $sdkPath "build-tools") -Recurse -Filter "apksigner.bat" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $apkSigner) {
        throw "Android apksigner not found under $sdkPath\build-tools; cannot verify APK before upload."
    }

    & $apkSigner verify --verbose $installerSetupPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Android APK signature verification failed: $installerSetupPath. Build a signed APK before upload."
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

$env:GODEBUG = "http2client=0"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "GitHub CLI was not found. Install gh first: https://cli.github.com/"
}

$hash = (Get-FileHash -LiteralPath $installerSetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$notesPath = Join-Path ([IO.Path]::GetTempPath()) "yanzi-release-$plainVersion.md"
$notesContent = "Yanzi Release v$plainVersion"
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

$token = if ($GithubToken) { $GithubToken } elseif ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { "" }
if (-not [string]::IsNullOrEmpty($token)) {
    try {
        Write-Host "Updating release notes to Chinese via GitHub API to prevent encoding issues..."
        $headers = @{
            "Authorization" = "token $token"
            "Accept"        = "application/vnd.github.v3+json"
            "User-Agent"    = "PowerShell"
        }
        
        $tagUrl = "https://api.github.com/repos/$Repo/releases/tags/$tag"
        $releaseObj = Invoke-RestMethod -Uri $tagUrl -Headers $headers -Method Get
        $releaseId = $releaseObj.id
        
        $chineseBody = if ($Platform -eq "android") {
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

**✨ 自动更新体验全面升级**
- 【极速镜像通道】自动更新重构为 SplitButton 分裂按钮设计，默认点击即可使用高速镜像源（ghfast.top）极速检测并自动启动下载，告别 GitHub 官方直连由于网络波动导致的下载失败或卡顿。
- 【灵活源切换】在更新按钮右侧提供下拉菜单，可在“镜像更新 (默认推荐)”与“GitHub更新 (官方直连)”之间自由按需切换。
- 【实时下载进度条】新增自动更新下载进度条与百分比数值反馈，后台增量包下载与组装过程一目了然。

**🖱️ 鼠标手势与快捷触发增强**
- 【新增 Ctrl+左键移动】在设置界面的鼠标触发选项中，新增“Ctrl+左键移动”键盘组合支持，下拉选项与中键移动保持完全一致（包含：禁用、背包、燕环、燕幕、窗口排列、鼠标手势）。
- 【手势底层调度修复】修复了底层手势服务在非标准触发键下的注册归一化与物理按键状态判定问题，确保 Ctrl+左键移动绘制鼠标手势百分百稳定响应与识别。

---
一键安装包：$fileName
SHA256: $hash
"@

        $payload = @{
            "name" = if ($Platform -eq "android") { "Yanzi for Android $plainVersion" } else { "燕子 Yanzi v$plainVersion" }
            "body" = $chineseBody
        } | ConvertTo-Json -Depth 10

        $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
        $patchUrl = "https://api.github.com/repos/$Repo/releases/$releaseId"
        
        $null = Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -ContentType "application/json; charset=utf-8" -Body $bytes
        Write-Host "Successfully patched release notes to Chinese."
    } catch {
        Write-Warning "Could not patch release notes to Chinese via API: $_"
    }
}

if (-not $Draft) {
    gh release edit $tag --repo $Repo --draft=false | Out-Host
}

gh release view $tag --repo $Repo --json tagName,name,isDraft,url,assets | Out-Host

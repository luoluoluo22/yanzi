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

# 1. 尝试使用 GitHub API PATCH 更新中文的 Release Notes (防止 gh 命令行临时文件乱码)
$token = $env:GITHUB_TOKEN
if (-not [string]::IsNullOrEmpty($token)) {
    try {
        Write-Host "Updating release notes to Chinese via GitHub API to prevent encoding issues..."
        $headers = @{
            "Authorization" = "token $token"
            "Accept"        = "application/vnd.github.v3+json"
            "User-Agent"    = "PowerShell"
        }
        
        # 抓取 Release 以便拿到 id
        $tagUrl = "https://api.github.com/repos/$Repo/releases/tags/$tag"
        $releaseObj = Invoke-RestMethod -Uri $tagUrl -Headers $headers -Method Get
        $releaseId = $releaseObj.id
        
        # 优先读取根目录下的 RELEASE_NOTES.md 文件作为中文更新说明
        $localNotesFile = Join-Path $root "RELEASE_NOTES.md"
        $chineseBody = ""
        if (Test-Path $localNotesFile) {
            $rawNotes = (Get-Content $localNotesFile -Raw).Trim()
            if (-not [string]::IsNullOrEmpty($rawNotes)) {
                $chineseBody = $rawNotes
                Write-Host "Loaded release notes from $localNotesFile"
            }
        }

        if ([string]::IsNullOrEmpty($chineseBody)) {
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

**✨ 新建扩展体验重构**
- 【高清晰图标】记事本程序默认不再使用矢量图标替代，而是通过新增 Windows 原生的 `IShellItemImageFactory` COM 接口，直接从系统提取 256x256 分辨率的现代高清晰 Fluent 原生记事本图标（包括其他系统自带或第三方 EXE 程序的高清图标）。
- 【输入高亮可见】修复了全选文本框内容时蓝色选中高亮几乎看不清的对比度问题，将文本选择笔刷升级为 100% 不透明的主题亮蓝色，确保在深色模式下文字极易阅读。
- 【高级选项优化】重构了“更多高级选项”中的表单布局，将之前折叠在最底部的“关键词”输入框移至最上方全宽显示，使得分类、版本和关键词的填写更加直观和方便。

---
一键安装包：$fileName
SHA256: $hash
"@
            }
        }

        $payload = @{
            "name" = if ($Platform -eq "android") { "Yanzi for Android $plainVersion" } else { "燕子 Yanzi v$plainVersion" }
            "body" = $chineseBody
        } | ConvertTo-Json -Depth 10

        # 转成 UTF-8 字节，100% 安全
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

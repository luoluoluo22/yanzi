﻿param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$SkipInstaller,
    [string]$GithubToken = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"

if ([string]::IsNullOrEmpty($Version) -or $Version -eq "0.1.0") {
    if (Test-Path $project) {
        [xml]$xml = Get-Content $project
        $Version = $xml.Project.PropertyGroup.Version.Trim()
        Write-Host "Auto-detected version from csproj: $Version"
    }
}

$publishDir = Join-Path $root ".artifacts\publish\$Runtime"
$installerOutDir = Join-Path $root ".artifacts\installer"
$issPath = Join-Path $root "installer\yanzi.iss"
$installerFileName = "YanziSetup.exe"
$installerPath = Join-Path $installerOutDir $installerFileName

function Assert-PayloadFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $publishDir $RelativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Published payload is missing required file: $RelativePath"
    }
}

function Assert-PayloadDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $publishDir $RelativePath
    if (-not (Test-Path $path -PathType Container)) {
        throw "Published payload is missing required directory: $RelativePath"
    }
}

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
# 不再清空 installerOutDir，保留之前的完整包以便 Velopack 生成增量包 (Delta)
if (-not (Test-Path $installerOutDir)) {
    New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    --source https://repo.huaweicloud.com/repository/nuget/v3/index.json `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=zh-Hans `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:CETCompat=false `
    -o $publishDir

Write-Host "Verifying installer payload..."
Assert-PayloadFile "Yanzi.exe"
Assert-PayloadFile "Yanzi.dll"
Assert-PayloadFile "Yanzi.deps.json"
Assert-PayloadFile "Yanzi.runtimeconfig.json"
Assert-PayloadFile "coreclr.dll"
Assert-PayloadFile "hostfxr.dll"
Assert-PayloadFile "hostpolicy.dll"
Assert-PayloadFile "PresentationFramework.dll"
Assert-PayloadFile "PresentationCore.dll"
Assert-PayloadFile "WindowsBase.dll"
Assert-PayloadFile "System.Xaml.dll"
Assert-PayloadFile "Microsoft.CodeAnalysis.dll"
Assert-PayloadFile "Microsoft.CodeAnalysis.CSharp.dll"
Assert-PayloadFile "Basic.Reference.Assemblies.Net90.dll"
Assert-PayloadFile "Everything64.dll"
Assert-PayloadFile "EverythingRuntime\everything.exe"
Assert-PayloadDirectory "NativeWindowRefs"
Assert-PayloadFile "NativeWindowRefs\PresentationFramework.dll"
Assert-PayloadFile "NativeWindowRefs\PresentationCore.dll"
Assert-PayloadFile "NativeWindowRefs\WindowsBase.dll"
Assert-PayloadFile "NativeWindowRefs\System.Xaml.dll"
Assert-PayloadFile "NativeWindowRefs\System.Windows.Forms.dll"

Write-Host "Published installer payload:"
Write-Host "  $publishDir"

if (-not $SkipInstaller) {
    $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    if (-not $vpk) {
        $vpkPath = "vpk"
    } else {
        $vpkPath = $vpk.Source
    }

    $cleanVer = $Version.TrimStart("vV")
    $hasLocalPreviousReleases = $false
    $releasesFile = Join-Path $installerOutDir "RELEASES"
    if (Test-Path $releasesFile) {
        $existingFullNupkg = Get-ChildItem -Path $installerOutDir -File -Filter "*-full.nupkg" | Where-Object { $_.Name -notmatch [regex]::Escape($cleanVer) }
        if ($existingFullNupkg.Count -gt 0) {
            $hasLocalPreviousReleases = $true
            Write-Host "Found local baseline release package: $($existingFullNupkg[0].Name). Skipping remote download!" -ForegroundColor Green
        }
    }

    if (-not $hasLocalPreviousReleases) {
        Write-Host "No local previous releases found. Downloading from GitHub for delta generation..."
        $downloadArgs = @("download", "github", "--repoUrl", "https://github.com/luoluoluo22/yanzi", "--outputDir", $installerOutDir)
        if ($GithubToken) {
            $downloadArgs += @("--token", $GithubToken)
        }

        try {
            & $vpkPath $downloadArgs
            Write-Host "Successfully downloaded previous releases."
        } catch {
            Write-Warning "Could not download previous releases (this is normal for the very first release or offline builds): $_"
        }
    }

    Get-ChildItem -Path $installerOutDir -File | Where-Object { $_.Name -match [regex]::Escape($cleanVer) } | Remove-Item -Force -ErrorAction SilentlyContinue
    if (Test-Path $releasesFile) {
        $lines = Get-Content $releasesFile | Where-Object { $_ -notmatch [regex]::Escape($cleanVer) }
        Set-Content -Path $releasesFile -Value $lines
    }

    Write-Host "Building Velopack installer package..."
    & $vpkPath pack `
        --packId "Yanzi" `
        --packTitle "Yanzi" `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe "Yanzi.exe" `
        --icon "$root\src\OpenQuickHost\yanzi.ico" `
        --outputDir $installerOutDir `
        --shortcuts "Desktop,StartMenuRoot"

    if ($LASTEXITCODE -ne 0) {
        throw "Velopack pack failed to build the installer."
    }

    $setupFile = Join-Path $installerOutDir "Yanzi-win-Setup.exe"
    if (Test-Path $setupFile) {
        Rename-Item -Path $setupFile -NewName "Yanzi-win-Setup-$Version.exe" -Force
    }

    Write-Host "Creating portable ZIP archive..."
    $rawZip = Join-Path $installerOutDir "Yanzi-win-Portable.zip"
    if (Test-Path $rawZip) {
        Remove-Item -Path $rawZip -Force
    }
    Compress-Archive -Path "$publishDir\*" -DestinationPath $rawZip -Force

    $zipFile = Join-Path $installerOutDir "Yanzi-win-Portable.zip"
    if (Test-Path $zipFile) {
        Rename-Item -Path $zipFile -NewName "Yanzi-win-Portable-$Version.zip" -Force
    }

    # 智能保留最近 2 个版本的 full.nupkg 作为下次本地增量基准，仅清理历史旧 exe/zip 和过期 delta 包
    Write-Host "Cleaning up historical packages in output directory..."
    $cleanVersion = $Version.TrimStart("vV")
    Get-ChildItem -Path $installerOutDir -File | Where-Object {
        ($_.Extension -in @(".exe", ".zip")) -and ($_.Name -notmatch [regex]::Escape($cleanVersion))
    } | Remove-Item -Force -ErrorAction SilentlyContinue

    Get-ChildItem -Path $installerOutDir -File -Filter "*-delta.nupkg" | Where-Object {
        $_.Name -notmatch [regex]::Escape($cleanVersion)
    } | Remove-Item -Force -ErrorAction SilentlyContinue

    $fullPackages = Get-ChildItem -Path $installerOutDir -File -Filter "*-full.nupkg" | Sort-Object LastWriteTime -Descending
    if ($fullPackages.Count -gt 2) {
        $fullPackages | Select-Object -Skip 2 | ForEach-Object {
            Write-Host "Pruning aged baseline full package: $($_.Name)"
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Velopack packaging completed successfully."
    Write-Host "Installer output directory:"
    Write-Host "  $installerOutDir"
}

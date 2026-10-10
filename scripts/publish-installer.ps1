param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$SkipInstaller,
    [string]$GithubToken = "",
    [string]$ArtifactRoot = "",
    [switch]$SkipBaselineDownload,
    [switch]$KeepHistoricalPackages
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

if ($Version -notmatch '^(\d+\.\d+\.\d+)(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be major.minor.patch with an optional prerelease suffix."
}
$assemblyVersion = $Matches[1] + '.0'
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $root '.artifacts' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$publishDir = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "publish\$Runtime"))
$installerOutDir = Join-Path $ArtifactRoot 'installer'
if (-not $publishDir.StartsWith($ArtifactRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directory must be inside the artifact root.'
}

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
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (-not (Test-Path $installerOutDir)) {
    New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "--source", "https://repo.huaweicloud.com/repository/nuget/v3/index.json",
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "-p:FileVersion=$assemblyVersion",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:PublishSingleFile=false",
    "-p:SatelliteResourceLanguages=zh-Hans",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:CETCompat=false",
    "-p:SkipStopRunningApp=true",
    "-o", $publishDir
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

# Keep two independent Windows process identities, but distribute only ONE set of
# framework, application, OCR and extension dependencies. Publishing each project
# directly into different installer folders previously duplicated ~178 MiB on disk.
$runtimeBuildDir = Join-Path $ArtifactRoot ("runtime-build\" + $Runtime)
if (Test-Path -LiteralPath $runtimeBuildDir) {
    Remove-Item -LiteralPath $runtimeBuildDir -Recurse -Force
}
$runtimePublishArgs = $publishArgs.Clone()
$runtimePublishArgs[1] = Join-Path $root 'src\Yanzi.Runtime\Yanzi.Runtime.csproj'
$runtimePublishArgs[$runtimePublishArgs.Length - 1] = $runtimeBuildDir
dotnet @runtimePublishArgs
if ($LASTEXITCODE -ne 0) { throw 'Independent Runtime publish failed.' }

# The Runtime-specific entry point and manifests live beside Yanzi.exe, not in
# another self-contained folder. Verify every common file matches byte-for-byte
# before deduplication; fail closed if the two publish graphs diverge.
$runtimeOnlyFiles = @('Yanzi.Runtime.exe', 'Yanzi.Runtime.dll',
                      'Yanzi.Runtime.deps.json', 'Yanzi.Runtime.runtimeconfig.json')
try {
    foreach ($file in @(Get-ChildItem -LiteralPath $runtimeBuildDir -File -Recurse)) {
        $relative = $file.FullName.Substring($runtimeBuildDir.TrimEnd('\').Length + 1)
        if ($relative -in $runtimeOnlyFiles) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $publishDir $relative) -Force
            continue
        }
        $shared = Join-Path $publishDir $relative
        if (-not (Test-Path -LiteralPath $shared -PathType Leaf)) {
            throw "Runtime-only dependency not present in host publish: $relative"
        }
        $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $sharedHash = (Get-FileHash -LiteralPath $shared -Algorithm SHA256).Hash
        if ($sourceHash -ne $sharedHash) {
            throw "Runtime and host dependency differ; cannot share safely: $relative"
        }
    }
}
finally {
    # Do not retain a third, untracked copy of the full framework in artifacts.
    if (Test-Path -LiteralPath $runtimeBuildDir) {
        Remove-Item -LiteralPath $runtimeBuildDir -Recurse -Force
    }
}
if (Test-Path (Join-Path $publishDir 'Runtime')) {
    throw 'Invalid release payload: duplicate Runtime directory detected.'
}
foreach ($name in $runtimeOnlyFiles) { Assert-PayloadFile $name }

Write-Host "Verifying installer payload..."
# Separate process identity, same published dependency directory.
Assert-PayloadFile 'Yanzi.Runtime.exe'
Assert-PayloadFile "Yanzi.exe"
Assert-PayloadFile "Yanzi.dll"
Assert-PayloadFile "Yanzi.Core.dll"
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

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-release-layout.ps1') -PublishDirectory $publishDir -ExpectedVersion $assemblyVersion
if ($LASTEXITCODE -ne 0) { throw 'Single-runtime release layout verification failed.' }
Write-Host "Published installer payload:"
Write-Host "  $publishDir"

if (-not $SkipInstaller) {
    # 自动解析 csproj 中锁定的 Velopack NuGet 包版本，并强行自动对齐全局 vpk 打包工具版本
    [xml]$xml = Get-Content $project
    $expectedVelopackVer = ($xml.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq "Velopack" -or $_.Include -eq "velopack" }).Version
    if (-not [string]::IsNullOrWhiteSpace($expectedVelopackVer)) {
        $vpkToolList = dotnet tool list -g 2>$null | Out-String
        if ($vpkToolList -match 'vpk\s+([0-9a-zA-Z\.\-]+)') {
            $installedVpkVer = $matches[1].Trim()
            if ($installedVpkVer -ne $expectedVelopackVer) {
                Write-Host "检测到打包工具与项目 SDK 版本不同步 ($installedVpkVer != $expectedVelopackVer)，正在自动强行对齐..." -ForegroundColor Yellow
                dotnet tool update -g vpk --version $expectedVelopackVer | Out-Null
                Write-Host "打包工具已自动对齐为 $expectedVelopackVer。" -ForegroundColor Green
            }
        } else {
            Write-Host "未检测到全局 vpk 工具，正在自动安装指定版本 $expectedVelopackVer ..." -ForegroundColor Yellow
            dotnet tool install -g vpk --version $expectedVelopackVer | Out-Null
        }
    }

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

    if (-not $hasLocalPreviousReleases -and -not $SkipBaselineDownload) {
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

    foreach ($name in @("Yanzi-$cleanVer-full.nupkg", "Yanzi-$cleanVer-delta.nupkg", "Yanzi-win-Setup-$cleanVer.exe", "Yanzi-win-Portable-$cleanVer.zip")) {
        Remove-Item -LiteralPath (Join-Path $installerOutDir $name) -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $releasesFile) {
        $lines = Get-Content $releasesFile | Where-Object { $_ -notmatch [regex]::Escape($cleanVer) }
        Set-Content -Path $releasesFile -Value $lines
    }

    Write-Host "Building Velopack installer package..."
    $packArgs = @(
        "pack",
        "--packId", "Yanzi",
        "--packTitle", "Yanzi",
        "--packVersion", $Version,
        "--packDir", $publishDir,
        "--mainExe", "Yanzi.exe",
        "--icon", "$root\src\OpenQuickHost\yanzi.ico",
        "--outputDir", $installerOutDir,
        "--shortcuts", "Desktop,StartMenuRoot"
    )
    & $vpkPath @packArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Velopack pack failed to build the installer."
    }

    $setupFile = Join-Path $installerOutDir "Yanzi-win-Setup.exe"
    if (Test-Path $setupFile) {
        Rename-Item -Path $setupFile -NewName "Yanzi-win-Setup-$Version.exe" -Force
    }

    # Keep Velopack's portable layout, updater and .portable marker.
    $zipFile = Join-Path $installerOutDir "Yanzi-win-Portable.zip"
    if (-not (Test-Path $zipFile)) { throw 'Velopack portable package is missing.' }
    Rename-Item -LiteralPath $zipFile -NewName "Yanzi-win-Portable-$Version.zip" -Force

    # 智能保留最近 2 个版本的 full.nupkg 作为下次本地增量基准，仅清理历史旧 exe/zip 和过期 delta 包
    if (-not $KeepHistoricalPackages) {
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
    }

    Write-Host "Velopack packaging completed successfully."
    Write-Host "Installer output directory:"
    Write-Host "  $installerOutDir"
}

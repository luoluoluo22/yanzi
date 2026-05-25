param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"
$publishDir = Join-Path $root ".artifacts\publish\$Runtime"
$installerOutDir = Join-Path $root ".artifacts\installer"
$issPath = Join-Path $root "installer\yanzi.iss"
$installerFileName = "YanziSetup-$Version.exe"
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
if (Test-Path $installerOutDir) {
    Remove-Item -Path $installerPath -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
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

Write-Host "Published installer payload:"
Write-Host "  $publishDir"

if (-not $SkipInstaller) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $candidatePaths = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )
        $isccPath = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    } else {
        $isccPath = $iscc.Source
    }

    if (-not $isccPath) {
        Write-Warning "Inno Setup 6 was not found. Install it to build the one-click installer, or distribute the portable Yanzi.exe above."
    } else {
        & $isccPath `
            "/DAppVersion=$Version" `
            "/DPublishDir=$publishDir" `
            "/DOutputDir=$installerOutDir" `
            $issPath

        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed to build the installer."
        }
        Write-Host "Installer output:"
        Write-Host "  $installerPath"
    }
}

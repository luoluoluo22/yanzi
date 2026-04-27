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

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
if (Test-Path $installerOutDir) {
    Remove-Item -Path (Join-Path $installerOutDir "YanziSetup-$Version.exe") -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerOutDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:CETCompat=false `
    -o $publishDir

Write-Host "Published portable executable:"
Write-Host "  $publishDir\Yanzi.exe"

if ($SkipInstaller) {
    return
}

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
    return
}

& $isccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$installerOutDir" `
    $issPath

Write-Host "Installer output:"
Write-Host "  $installerOutDir\YanziSetup-$Version.exe"

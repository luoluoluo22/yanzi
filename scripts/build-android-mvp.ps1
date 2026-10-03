param(
    [string]$SdkPath = $env:ANDROID_HOME,
    [ValidateSet("debug", "dev", "release")]
    [string]$Configuration = "debug",
    [ValidateSet("app", "calendar", "album", "notes")]
    [string]$Module = "app"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AndroidRoot = Join-Path $ProjectRoot "mobile\android"

if ([string]::IsNullOrWhiteSpace($SdkPath)) {
    $SdkPath = $env:ANDROID_SDK_ROOT
}
if ([string]::IsNullOrWhiteSpace($SdkPath)) {
    $SdkPath = "F:\SDK"
}

$env:ANDROID_HOME = $SdkPath
$env:ANDROID_SDK_ROOT = $SdkPath

$GradleWrapper = Join-Path $AndroidRoot "gradlew.bat"
if (-not (Test-Path $GradleWrapper)) {
    throw "Missing Gradle wrapper: $GradleWrapper"
}

$task = switch ($Configuration) {
    "release" { "${Module}:assembleRelease" }
    "dev" { "${Module}:assembleDev" }
    default { "${Module}:assembleDebug" }
}
$variant = $Configuration.ToLowerInvariant()

Push-Location $AndroidRoot
try {
    & $GradleWrapper $task --no-daemon
    if ($LASTEXITCODE -ne 0) {
        throw "Gradle build failed: $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$moduleRoot = if ($Module -in @('album','notes')) { Join-Path $env:LOCALAPPDATA ('OpenQuickHost\Extensions\yanzi-' + $Module + '\android') } else { Join-Path $AndroidRoot $Module }
$sourceApk = Join-Path $moduleRoot "build\outputs\apk\$variant\$Module-$variant.apk"
if (-not (Test-Path $sourceApk)) {
    throw "Gradle completed but APK was not found: $sourceApk"
}

$outputDir = Join-Path $moduleRoot "build\manual-$variant"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$artifactName = if ($Module -eq "calendar") { "yanzi-calendar" } elseif ($Module -in @('album','notes')) { 'yanzi-' + $Module } else { "yanzi-mobile" }
$outputApk = Join-Path $outputDir "$artifactName-$variant.apk"
Copy-Item -LiteralPath $sourceApk -Destination $outputApk -Force

Write-Host "Android APK built successfully."
Write-Host "Source: $sourceApk"
Write-Host "Output: $outputApk"

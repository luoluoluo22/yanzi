param(
    [string]$SdkPath = $env:ANDROID_HOME,
    [ValidateSet("debug", "dev", "release")]
    [string]$Configuration = "debug"
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
    "release" { "app:assembleRelease" }
    "dev" { "app:assembleDev" }
    default { "app:assembleDebug" }
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

$sourceApk = Join-Path $AndroidRoot "app\build\outputs\apk\$variant\app-$variant.apk"
if (-not (Test-Path $sourceApk)) {
    throw "Gradle completed but APK was not found: $sourceApk"
}

$outputDir = Join-Path $AndroidRoot "app\build\manual-$variant"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$outputApk = Join-Path $outputDir "yanzi-mobile-$variant.apk"
Copy-Item -LiteralPath $sourceApk -Destination $outputApk -Force

Write-Host "Android APK built successfully."
Write-Host "Source: $sourceApk"
Write-Host "Output: $outputApk"

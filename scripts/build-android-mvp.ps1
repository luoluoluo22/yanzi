param(
    [string]$SdkPath = $env:ANDROID_HOME,
    [ValidateSet("debug", "dev", "release")]
    [string]$Configuration = "debug",
    [ValidateSet("app", "calendar", "album", "notes")]
    [string]$Module = "app",
    [int]$VersionCode = 0,
    [string]$VersionName = "",
    [string]$ArtifactRoot = ""
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
$versionArgs = @()
if ($VersionCode -lt 0) { throw 'VersionCode must be positive or zero to use the project version.' }
if ($VersionCode -gt 0) { $versionArgs += "-PYANZI_ANDROID_VERSION_CODE=$VersionCode" }
if ($VersionName) {
    if ($VersionName -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid Android version name.' }
    $versionArgs += "-PYANZI_ANDROID_VERSION_NAME=$VersionName"
}
if ($Module -ne 'app' -and $versionArgs.Count) { throw 'Candidate version overrides only apply to the main app.' }
$candidateBuildRoot = ''
if ($ArtifactRoot) {
    if ($Module -ne 'app') { throw 'An isolated artifact root only applies to the main app.' }
    $candidateBuildRoot = Join-Path ([IO.Path]::GetFullPath($ArtifactRoot)) 'android\app'
    $versionArgs += "-PYANZI_ANDROID_BUILD_ROOT=$candidateBuildRoot"
}

Push-Location $AndroidRoot
try {
    & $GradleWrapper $task @versionArgs --no-daemon
    if ($LASTEXITCODE -ne 0) {
        throw "Gradle build failed: $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$moduleRoot = if ($Module -in @('album','notes')) { Join-Path $env:LOCALAPPDATA ('OpenQuickHost\Extensions\yanzi-' + $Module + '\android') } else { Join-Path $AndroidRoot $Module }
$moduleBuildRoot = if ($candidateBuildRoot) { $candidateBuildRoot } else { Join-Path $moduleRoot 'build' }
$sourceApk = Join-Path $moduleBuildRoot "outputs\apk\$variant\$Module-$variant.apk"
if (-not (Test-Path $sourceApk)) {
    throw "Gradle completed but APK was not found: $sourceApk"
}

$outputDir = Join-Path $moduleBuildRoot "manual-$variant"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$artifactName = if ($Module -eq "calendar") { "yanzi-calendar" } elseif ($Module -in @('album','notes')) { 'yanzi-' + $Module } else { "yanzi-mobile" }
$outputApk = Join-Path $outputDir "$artifactName-$variant.apk"
Copy-Item -LiteralPath $sourceApk -Destination $outputApk -Force

Write-Host "Android APK built successfully."
Write-Host "Source: $sourceApk"
Write-Host "Output: $outputApk"

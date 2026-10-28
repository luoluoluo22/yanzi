param(
    [switch]$SkipDesktopBuild,
    [switch]$SkipAndroidBuild,
    [switch]$SkipSyncVerification,
    [switch]$RequireDevice
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SdkPath = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "F:\SDK" }
$Adb = Join-Path $SdkPath "platform-tools\adb.exe"

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

Write-Host "Yanzi development environment check"
Write-Host "Project: $ProjectRoot"

Assert-Command "git"
Assert-Command "dotnet"
Assert-Command "java"
Assert-Command "javac"
Assert-Command "node"

if (-not (Test-Path $Adb)) {
    throw "Project Android SDK adb not found: $Adb"
}

$env:ANDROID_HOME = $SdkPath
$env:ANDROID_SDK_ROOT = $SdkPath

Push-Location $ProjectRoot
try {
    Write-Host ""
    Write-Host "Toolchain:"
    Write-Host "  git:     $(git --version)"
    Write-Host "  dotnet:  $(dotnet --version)"
    Write-Host "  java:    $(javac -version 2>&1)"
    Write-Host "  node:    $(node --version)"
    Write-Host "  adb:"
    & $Adb version

    $devices = @(& $Adb devices -l | Where-Object { $_ -match '^\S+\s+device\b' })
    Write-Host "Connected Android devices: $($devices.Count)"
    $devices | ForEach-Object { Write-Host "  $_" }
    if ($RequireDevice -and $devices.Count -eq 0) {
        throw "No authorized Android device is connected."
    }

    if (-not $SkipDesktopBuild) {
        Write-Host ""
        Write-Host "Building desktop solution..."
        dotnet build ".\OpenQuickHost.sln" -c Debug -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Desktop build failed." }
    }

    if (-not $SkipSyncVerification) {
        Write-Host ""
        Write-Host "Running sync verification..."
        dotnet run --project ".\src\Yanzi.SyncVerification\Yanzi.SyncVerification.csproj"
        if ($LASTEXITCODE -ne 0) { throw "Sync verification failed." }
    }

    Write-Host ""
    Write-Host "Checking Cloudflare Worker syntax..."
    node --check ".\cloudflare\src\index.js"
    if ($LASTEXITCODE -ne 0) { throw "Cloudflare Worker syntax check failed." }

    if (-not $SkipAndroidBuild) {
        Write-Host ""
        Write-Host "Building Android app..."
        & ".\scripts\build-android-mvp.ps1" -SdkPath $SdkPath -Configuration debug
        if ($LASTEXITCODE -ne 0) { throw "Android build failed." }
    }

    Write-Host ""
    Write-Host "Development environment check PASSED."
}
finally {
    Pop-Location
}

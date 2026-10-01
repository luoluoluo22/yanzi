param(
    [switch]$SkipBuild,
    [switch]$WipeEmulator,
    [switch]$Windowed,
    [switch]$NoScreenshot
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Push-Location $ProjectRoot
try {
    $emulatorArgs = @{}
    if ($WipeEmulator) { $emulatorArgs["WipeData"] = $true }
    if ($Windowed) { $emulatorArgs["Windowed"] = $true }

    Write-Host "Ensuring Yanzi Android emulator is running..."
    & ".\scripts\dev-emulator.ps1" @emulatorArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start the Yanzi Android emulator."
    }

    $loopArgs = @{}
    if ($SkipBuild) { $loopArgs["SkipBuild"] = $true }
    if ($NoScreenshot) { $loopArgs["NoScreenshot"] = $true }

    Write-Host ""
    Write-Host "Running Android build/install/smoke loop..."

    & ".\scripts\dev-android-loop.ps1" @loopArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Android development loop failed."
    }

    Write-Host ""
    Write-Host "Yanzi mobile development environment PASSED."
}
finally {
    Pop-Location
}

param(
    [switch]$SkipEnvironmentCheck,
    [switch]$SkipIntegrationTests
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$integrationDirty = $false
$serial = @()

Push-Location $ProjectRoot
try {
    if (-not $SkipEnvironmentCheck) {
        & ".\scripts\dev-check.ps1"
        if ($LASTEXITCODE -ne 0) {
            throw "Development environment check failed."
        }
    }

    & ".\scripts\test-communication-foundation.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Communication foundation regression failed." }

    $serialOutput = & ".\scripts\dev-emulator.ps1"
    if ($LASTEXITCODE -ne 0) {
        throw "Android emulator startup failed."
    }

    $serial = @($serialOutput | Where-Object { $_ -match '^emulator-\d+$' } | Select-Object -Last 1)
    if ($serial.Count -eq 0) {
        throw "Yanzi emulator did not return an adb serial."
    }

    Write-Host ""
    Write-Host "Installing and smoke-testing Android build on $($serial[0])..."
    & ".\scripts\dev-android-loop.ps1" -SkipBuild -Serial $serial[0]
    if ($LASTEXITCODE -ne 0) {
        throw "Android smoke test failed."
    }

    if (-not $SkipIntegrationTests) {
        $integrationDirty = $true
        Write-Host ""
        Write-Host "Running Android object-sync integration tests..."
        & ".\scripts\test-android-object-sync.ps1" -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Android Yanm object-sync test failed." }

        & ".\scripts\test-mobile-extension-storage.ps1" -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Android extension storage test failed." }

        & ".\scripts\test-cross-platform-extension-storage.ps1" -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Cross-platform extension storage test failed." }

        & ".\scripts\test-mobile-extension-definition-sync.ps1" -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Mobile extension definition object-sync test failed." }

        & ".\scripts\test-unified-extension-catalog.ps1" -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Unified extension catalog test failed." }

        Write-Host ""
        Write-Host "Resetting emulator app data after integration tests..."
        & "F:\SDK\platform-tools\adb.exe" -s $serial[0] shell pm clear cc.luoluoluo.yanzi.mobile | Out-Null
        & ".\scripts\dev-android-loop.ps1" -SkipBuild -Serial $serial[0]
        if ($LASTEXITCODE -ne 0) { throw "Post-test Android clean smoke test failed." }
        $integrationDirty = $false
    }

    Write-Host ""
    Write-Host "Restarting desktop Yanzi from the verified build..."
    & ".\scripts\dev-desktop-loop.ps1" -SkipBuild
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop smoke test failed."
    }

    Write-Host ""
    Write-Host "Yanzi full development smoke test PASSED."
}
finally {
    try {
        if ($integrationDirty -and $serial.Count -gt 0 -and $serial[0] -match '^emulator-\d+$') {
            & "F:\SDK\platform-tools\adb.exe" -s $serial[0] shell pm clear cc.luoluoluo.yanzi.mobile | Out-Null
            & ".\scripts\dev-android-loop.ps1" -SkipBuild -Serial $serial[0]
        }
    } finally {
        & ".\scripts\dev-desktop-loop.ps1" -SkipBuild
        Pop-Location
    }
}

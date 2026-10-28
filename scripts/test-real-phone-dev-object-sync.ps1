param(
    [int]$Port = 8806,
    [string]$Serial = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Adb = "F:\SDK\platform-tools\adb.exe"
$ProductionPackage = "cc.luoluoluo.yanzi.mobile"
$DevPackage = "cc.luoluoluo.yanzi.mobile.dev"
$ActivityClass = "cc.luoluoluo.yanzi.mobile.MainActivity"

function Get-PackageSnapshot([string]$Package) {
    $pathLine = @(& $Adb -s $Serial shell pm path $Package 2>$null | Select-Object -First 1)
    if ($pathLine.Count -eq 0 -or -not ($pathLine[0] -match "^package:")) {
        return $null
    }
    $versionLines = @(& $Adb -s $Serial shell dumpsys package $Package 2>$null |
        Select-String "versionCode=|versionName=" |
        Select-Object -First 2 |
        ForEach-Object { $_.Line.Trim() })
    return [pscustomobject]@{
        Path = [string]$pathLine[0]
        Version = ($versionLines -join "; ")
    }
}

if (-not (Test-Path $Adb)) {
    throw "adb not found: $Adb"
}

$serials = @(& $Adb devices |
    Select-Object -Skip 1 |
    Where-Object { $_ -match '^\S+\s+device\s*$' } |
    ForEach-Object { ($_ -split '\s+')[0] } |
    Where-Object { $_ -notmatch '^emulator-' })

if ([string]::IsNullOrWhiteSpace($Serial)) {
    if ($serials.Count -ne 1) {
        throw "Expected exactly one connected physical Android device, found $($serials.Count). Pass -Serial if needed."
    }
    $Serial = $serials[0]
}
elseif ($Serial -match '^emulator-' -or $serials -notcontains $Serial) {
    throw "Requested physical Android device is not connected: $Serial"
}

$productionBefore = Get-PackageSnapshot $ProductionPackage
$devBefore = Get-PackageSnapshot $DevPackage
if (-not $productionBefore) {
    throw "Production Yanzi is missing; refusing the coexistence test."
}
if (-not $devBefore) {
    throw "Yanzi Dev is not installed. Run scripts\dev-real-phone.ps1 first."
}

Write-Host "Production before: $($productionBefore.Version)"
Write-Host "Dev before: $($devBefore.Version)"

& $Adb -s $Serial reverse "tcp:$Port" "tcp:$Port"
if ($LASTEXITCODE -ne 0) {
    throw "adb reverse setup failed."
}

try {
    Push-Location $ProjectRoot
    try {
        $testArgs = @{
            Port = $Port
            Serial = $Serial
            Package = $DevPackage
            ActivityClass = $ActivityClass
            MobileBaseUrlOverride = "http://127.0.0.1:$Port"
            AllowPhysicalDev = $true
        }
        & ".\scripts\test-mobile-extension-storage.ps1" @testArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Real-phone Dev object-sync test failed."
        }
    }
    finally {
        Pop-Location
    }

    $productionAfter = Get-PackageSnapshot $ProductionPackage
    if (-not $productionAfter -or
        $productionAfter.Path -ne $productionBefore.Path -or
        $productionAfter.Version -ne $productionBefore.Version) {
        throw "Production Yanzi changed during the real-phone Dev integration test."
    }

    Write-Host "Production preserved: $($productionAfter.Version)"
    Write-Host "Real-phone DEV object-sync PASSED."
}
finally {
    & $Adb -s $Serial reverse --remove "tcp:$Port" 2>$null | Out-Null

    $devPath = & $Adb -s $Serial shell pm path $DevPackage 2>$null
    if ($devPath -match "^package:") {
        & $Adb -s $Serial shell pm clear $DevPackage | Out-Null
        & $Adb -s $Serial shell am start -n "$DevPackage/$ActivityClass" | Out-Null
    }
}

param(
    [string]$Serial = "",
    [switch]$SkipBuild,
    [switch]$SkipInstall,
    [switch]$NoScreenshot
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SdkPath = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "F:\SDK" }
$Adb = Join-Path $SdkPath "platform-tools\adb.exe"

$ProductionPackage = "cc.luoluoluo.yanzi.mobile"
$DevPackage = "cc.luoluoluo.yanzi.mobile.dev"
$DevActivityClass = "cc.luoluoluo.yanzi.mobile.MainActivity"
$Apk = Join-Path $ProjectRoot "mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk"
$ArtifactRoot = Join-Path $env:TEMP "YanziDev\real-phone"

if (-not (Test-Path $Adb)) {
    throw "adb not found: $Adb"
}

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
        Package = $Package
        Path = [string]$pathLine[0]
        Version = ($versionLines -join "; ")
    }
}

$deviceLines = @(& $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' })
$serials = @($deviceLines | ForEach-Object { ($_ -split '\s+')[0] })
$realSerials = @($serials | Where-Object { $_ -notmatch '^emulator-' })

if ([string]::IsNullOrWhiteSpace($Serial)) {
    if ($realSerials.Count -eq 0) {
        throw "No authorized physical Android device is connected."
    }
    if ($realSerials.Count -gt 1) {
        throw "Multiple physical Android devices are connected. Pass -Serial explicitly."
    }
    $Serial = $realSerials[0]
}
elseif ($Serial -match '^emulator-') {
    throw "dev-real-phone.ps1 only accepts a physical Android device."
}
elseif ($realSerials -notcontains $Serial) {
    throw "Requested physical Android device is not connected: $Serial"
}

$productionBefore = Get-PackageSnapshot $ProductionPackage
if (-not $productionBefore) {
    throw "Production Yanzi is not installed on the selected phone; refusing the coexistence test."
}

Write-Host "Physical device: selected (identifier omitted)"
Write-Host "Production before: $($productionBefore.Version)"
Write-Host "Production path: $($productionBefore.Path)"

if (-not $SkipBuild) {
    Push-Location $ProjectRoot
    try {
        & ".\scripts\build-android-mvp.ps1" -SdkPath $SdkPath -Configuration dev
        if ($LASTEXITCODE -ne 0) {
            throw "Dev APK build failed."
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path $Apk)) {
    throw "Dev APK not found: $Apk"
}

if (-not $SkipInstall) {
    Write-Host "Installing DEV package only: $DevPackage"
    & $Adb -s $Serial install -r $Apk
    if ($LASTEXITCODE -ne 0) {
        throw "Dev APK install failed. Production Yanzi was not modified."
    }
}

$productionAfterInstall = Get-PackageSnapshot $ProductionPackage
if (-not $productionAfterInstall -or
    $productionAfterInstall.Path -ne $productionBefore.Path -or
    $productionAfterInstall.Version -ne $productionBefore.Version) {
    throw "Production Yanzi changed during the Dev install; aborting verification."
}

$devSnapshot = Get-PackageSnapshot $DevPackage
if (-not $devSnapshot) {
    throw "Dev package was not installed."
}
Write-Host "Dev installed: $($devSnapshot.Version)"

& $Adb -s $Serial shell am force-stop $DevPackage
& $Adb -s $Serial shell am start -n "$DevPackage/$DevActivityClass" | Out-Host

$deadline = (Get-Date).AddSeconds(20)
$focusText = ""
do {
    Start-Sleep -Milliseconds 500
    $focusText = (& $Adb -s $Serial shell dumpsys window |
        Select-String "mCurrentFocus|mFocusedApp" |
        ForEach-Object { $_.Line.Trim() }) -join [Environment]::NewLine
    if ($focusText -match [regex]::Escape($DevPackage)) {
        break
    }
} while ((Get-Date) -lt $deadline)

Write-Host "Foreground:"
Write-Host $focusText
if ($focusText -notmatch [regex]::Escape($DevPackage)) {
    throw "Yanzi Dev did not become the foreground app."
}

# A focused Activity can still be on its blank launch frame. Verify rendered app content.
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null
$uiRemote = '/sdcard/yanzi-dev-smoke-ui.xml'
$uiLocal = Join-Path $ArtifactRoot 'android-ui.xml'
$uiDeadline = (Get-Date).AddSeconds(25)
$visibleAppNodes = 0
do {
    $nativePreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Adb -s $Serial shell uiautomator dump $uiRemote 2>$null | Out-Null }
    finally { $ErrorActionPreference = $nativePreference }
    if ($LASTEXITCODE -eq 0) {
        $ErrorActionPreference = 'Continue'
        try { & $Adb -s $Serial pull $uiRemote $uiLocal 2>$null | Out-Null }
        finally { $ErrorActionPreference = $nativePreference }
        if ($LASTEXITCODE -eq 0 -and (Test-Path $uiLocal)) {
            [xml]$ui = Get-Content $uiLocal -Raw -Encoding UTF8
            $visibleAppNodes = @($ui.SelectNodes('//node') | Where-Object {
                $_.package -eq $DevPackage -and -not [string]::IsNullOrWhiteSpace($_.text)
            }).Count
        }
    }
    if ($visibleAppNodes -ge 3) { break }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $uiDeadline)
if ($visibleAppNodes -lt 3) { throw 'Dev Activity focused but its content did not render.' }
Write-Host "Rendered Dev UI text nodes: $visibleAppNodes"

$appPid = (& $Adb -s $Serial shell pidof $DevPackage 2>$null).Trim()
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null

if ($appPid) {
    $logPath = Join-Path $ArtifactRoot "android-logcat.txt"
    & $Adb -s $Serial logcat --pid=$appPid -d -t 300 | Set-Content -Encoding UTF8 $logPath
    $fatal = Select-String -Path $logPath -Pattern "FATAL EXCEPTION|ANR in" -ErrorAction SilentlyContinue
    if ($fatal) {
        throw "Yanzi Dev produced a fatal runtime error. See: $logPath"
    }
    Write-Host "Logcat: $logPath"
}

if (-not $NoScreenshot) {
    $screenshot = Join-Path $ArtifactRoot "android-smoke.png"
    $command = '"' + $Adb + '" -s ' + $Serial + ' exec-out screencap -p > "' + $screenshot + '"'
    cmd.exe /d /c $command
    if ($LASTEXITCODE -eq 0 -and (Test-Path $screenshot)) {
        Write-Host "Screenshot: $screenshot"
    }
}

$productionFinal = Get-PackageSnapshot $ProductionPackage
if (-not $productionFinal -or
    $productionFinal.Path -ne $productionBefore.Path -or
    $productionFinal.Version -ne $productionBefore.Version) {
    throw "Production Yanzi changed during the Dev smoke test."
}

Write-Host "Production preserved: $($productionFinal.Version)"
Write-Host "Yanzi real-phone DEV loop PASSED."

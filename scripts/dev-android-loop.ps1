param(
    [string]$Serial = "",
    [switch]$SkipBuild,
    [switch]$NoScreenshot
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SdkPath = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "F:\SDK" }
$Adb = Join-Path $SdkPath "platform-tools\adb.exe"
$Package = "cc.luoluoluo.yanzi.mobile"
$Activity = "$Package/.MainActivity"
$Apk = Join-Path $ProjectRoot "mobile\android\app\build\manual-debug\yanzi-mobile-debug.apk"
$ArtifactRoot = Join-Path $env:TEMP "YanziDev"

if (-not (Test-Path $Adb)) {
    throw "adb not found: $Adb"
}

$env:ANDROID_HOME = $SdkPath
$env:ANDROID_SDK_ROOT = $SdkPath
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-android-mvp.ps1") -SdkPath $SdkPath -Configuration debug
    if ($LASTEXITCODE -ne 0) { throw "Android build failed." }
}

if (-not (Test-Path $Apk)) {
    throw "APK not found: $Apk"
}

$devices = @(& $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' })
$serials = @($devices | ForEach-Object { ($_ -split '\s+')[0] })

if ([string]::IsNullOrWhiteSpace($Serial)) {
    if ($serials.Count -eq 0) { throw "No authorized Android device is connected." }

    $emulators = @($serials | Where-Object { $_ -match '^emulator-' })
    if ($emulators.Count -eq 1) {
        $Serial = $emulators[0]
    }
    elseif ($serials.Count -eq 1) {
        $Serial = $serials[0]
    }
    else {
        throw "Multiple devices are connected. Start the Yanzi emulator or pass -Serial explicitly."
    }
}
elseif ($serials -notcontains $Serial) {
    throw "Requested device is not connected: $Serial"
}

Write-Host "Installing $Apk on $Serial ..."
& $Adb -s $Serial install -r $Apk
if ($LASTEXITCODE -ne 0) {
    throw "APK install failed. The existing app was left untouched."
}

& $Adb -s $Serial shell am force-stop $Package
& $Adb -s $Serial shell am start -n $Activity | Out-Host

$deadline = (Get-Date).AddSeconds(15)
$focus = $null
$focusText = ""
$activityPattern = [regex]::Escape($Package + "/.MainActivity")
do {
    Start-Sleep -Milliseconds 500
    $focus = & $Adb -s $Serial shell dumpsys window | Select-String "mCurrentFocus|mFocusedApp"
    $focusText = $focus | Out-String

    if ($focusText -match ("Application Error:\s*" + [regex]::Escape($Package))) {
        throw "Android reported an Application Error dialog for Yanzi during startup."
    }

    if ($focusText -match $activityPattern) {
        break
    }
} while ((Get-Date) -lt $deadline)

Write-Host "Foreground:"
$focus | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
if ($focusText -notmatch $activityPattern) {
    throw "Yanzi MainActivity did not become the foreground app within 15 seconds."
}

$uiRemote = "/sdcard/yanzi-dev-ui.xml"
$uiLocal = Join-Path $ArtifactRoot "android-ui.xml"
$uiDeadline = (Get-Date).AddSeconds(15)
$textNodeCount = 0
do {
    Remove-Item -LiteralPath $uiLocal -Force -ErrorAction SilentlyContinue
    $rmCommand = '"' + $Adb + '" -s ' + $Serial + ' shell rm -f ' + $uiRemote + ' >nul 2>nul'
    cmd.exe /d /c $rmCommand | Out-Null

    $dumpCommand = '"' + $Adb + '" -s ' + $Serial + ' shell uiautomator dump ' + $uiRemote + ' >nul 2>nul'
    cmd.exe /d /c $dumpCommand
    if ($LASTEXITCODE -ne 0) {
        Start-Sleep -Milliseconds 750
        continue
    }

    $pullCommand = '"' + $Adb + '" -s ' + $Serial + ' pull ' + $uiRemote + ' "' + $uiLocal + '" >nul 2>nul'
    cmd.exe /d /c $pullCommand
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $uiLocal)) {
        Start-Sleep -Milliseconds 750
        continue
    }

    $uiRaw = Get-Content $uiLocal -Raw -Encoding UTF8
    $textNodeCount = ([regex]::Matches($uiRaw, 'text="[^"]+"')).Count
    if ($textNodeCount -ge 3) {
        break
    }
    Start-Sleep -Milliseconds 750
} while ((Get-Date) -lt $uiDeadline)

Write-Host "UI text nodes: $textNodeCount"
if ($textNodeCount -lt 3) {
    throw "Yanzi foreground activity exists, but the UI did not render enough visible content within 15 seconds."
}

$appPid = (& $Adb -s $Serial shell pidof $Package).Trim()
if ($appPid) {
    $logPath = Join-Path $ArtifactRoot "android-logcat.txt"
    & $Adb -s $Serial logcat --pid=$appPid -d -t 250 | Set-Content -Encoding UTF8 $logPath
    Write-Host "Logcat: $logPath"

    $fatal = Select-String -Path $logPath -Pattern "FATAL EXCEPTION|ANR in" -ErrorAction SilentlyContinue
    if ($fatal) {
        throw "Android smoke test detected a fatal runtime error. See: $logPath"
    }
}

if (-not $NoScreenshot) {
    $screenshot = Join-Path $ArtifactRoot "android-smoke.png"
    $cmd = '"' + $Adb + '" -s ' + $Serial + ' exec-out screencap -p > "' + $screenshot + '"'
    cmd.exe /c $cmd
    if ($LASTEXITCODE -eq 0 -and (Test-Path $screenshot)) {
        Write-Host "Screenshot: $screenshot"
    }
}

& $Adb -s $Serial shell dumpsys package $Package |
    Select-String "versionCode=|versionName=" |
    Select-Object -First 4

Write-Host "Android development loop PASSED."

param(
    [string]$AvdName = "YanziApi30",
    [switch]$Stop,
    [switch]$WipeData,
    [switch]$Windowed
)

$ErrorActionPreference = "Stop"
$SdkPath = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { "F:\SDK" }
$AvdHome = Join-Path $env:USERPROFILE ".android\avd"
$Adb = Join-Path $SdkPath "platform-tools\adb.exe"
$Emulator = Join-Path $SdkPath "emulator\emulator.exe"

if (-not (Test-Path $Adb)) { throw "adb not found: $Adb" }
if (-not (Test-Path $Emulator)) { throw "emulator not found: $Emulator" }
if (-not (Test-Path $AvdHome)) { throw "AVD home not found: $AvdHome" }

$env:ANDROID_HOME = $SdkPath
$env:ANDROID_SDK_ROOT = $SdkPath
$env:ANDROID_AVD_HOME = $AvdHome
$env:ANDROID_SDK_HOME = $env:USERPROFILE

function Get-AvdSerial {
    $lines = @(& $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^emulator-\d+\s+device\s*$' })
    foreach ($line in $lines) {
        $serial = ($line -split '\s+')[0]
        $nameLine = & $Adb -s $serial emu avd name 2>$null | Select-Object -First 1
        if ($null -eq $nameLine) { continue }
        $name = ([string]$nameLine).Trim()
        if ($name -eq $AvdName) { return $serial }
    }
    return $null
}

$existing = Get-AvdSerial

if ($Stop) {
    $escapedAvd = [regex]::Escape("-avd $AvdName")
    $targetProcesses = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(emulator|qemu-system-.*)\.exe$' -and
        $_.CommandLine -match $escapedAvd
    })
    $targetPids = @($targetProcesses | ForEach-Object { $_.ProcessId })

    if ($existing) {
        & $Adb -s $existing emu kill | Out-Null
        $stopDeadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Seconds 1
            $stillOnline = Get-AvdSerial
            if (-not $stillOnline) { break }
        } while ((Get-Date) -lt $stopDeadline)
    }

    $leftovers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(emulator|qemu-system-.*)\.exe$' -and
        $_.CommandLine -match $escapedAvd
    })
    foreach ($process in $leftovers) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    if ($targetPids.Count -gt 0) {
        $killPattern = '-kill\s+(' + (($targetPids | ForEach-Object { [regex]::Escape([string]$_) }) -join '|') + ')\b'
        $killHelpers = @(Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq 'emulator.exe' -and $_.CommandLine -match $killPattern
        })
        foreach ($helper in $killHelpers) {
            Stop-Process -Id $helper.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }

    if ($existing -or $targetProcesses.Count -gt 0 -or $leftovers.Count -gt 0) {
        Write-Host "Stopped $AvdName cleanly."
    } else {
        Write-Host "$AvdName is not running."
    }
    exit 0
}

if ($existing) {
    Write-Host "$AvdName is already running: $existing"
    Write-Output $existing
    exit 0
}

$args = "-avd $AvdName -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-load -no-snapshot-save"
if (-not $Windowed) { $args += " -no-window" }
if ($WipeData) { $args += " -wipe-data" }

$artifactRoot = Join-Path $env:TEMP "YanziDev"
$avdHome = Join-Path $env:USERPROFILE ".android\avd"
$worker = Join-Path $artifactRoot "start-emulator.cmd"
$emulatorLog = Join-Path $artifactRoot "emulator.log"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$workerLines = @(
    "@echo off",
    "set `"ANDROID_AVD_HOME=$avdHome`"",
    "set `"ANDROID_HOME=$SdkPath`"",
    "set `"ANDROID_SDK_ROOT=$SdkPath`"",
    "`"$Emulator`" $args >> `"$emulatorLog`" 2>&1"
)
$workerLines | Set-Content -Encoding ASCII $worker

$commandLine = 'cmd.exe /d /c "' + $worker + '"'
$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine = $commandLine
    CurrentDirectory = Split-Path -Parent $Emulator
}
if ($result.ReturnValue -ne 0) {
    throw "Failed to launch emulator. Win32 error: $($result.ReturnValue)"
}

Write-Host "Started $AvdName, processId=$($result.ProcessId)."

$deadline = (Get-Date).AddMinutes(3)
$serial = $null
do {
    Start-Sleep -Seconds 2
    $serial = Get-AvdSerial
    if ($serial) { break }
} while ((Get-Date) -lt $deadline)

if (-not $serial) {
    throw "Emulator did not become available through adb."
}

do {
    $boot = (& $Adb -s $serial shell getprop sys.boot_completed 2>$null).Trim()
    if ($boot -eq "1") { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)

if ($boot -ne "1") {
    throw "Emulator boot timed out: $serial"
}

Write-Host "$AvdName booted successfully: $serial"
Write-Output $serial

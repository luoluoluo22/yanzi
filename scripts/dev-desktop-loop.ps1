param(
    [switch]$SkipBuild,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$ProjectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $ProjectRoot "src\OpenQuickHost\OpenQuickHost.csproj"
$legacyExe = Join-Path $ProjectRoot "src\OpenQuickHost\bin\Debug\net9.0-windows\Yanzi.exe"

# Build to the inactive slot while the old development binary continues running.
# Never modify the installed Yanzi directory or its user data during compilation.
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $pathBytes = [Text.Encoding]::UTF8.GetBytes($ProjectRoot.ToUpperInvariant())
    $projectKey = ([BitConverter]::ToString($sha.ComputeHash($pathBytes)) -replace "-", "").Substring(0, 12)
} finally {
    $sha.Dispose()
}
$devRoot = Join-Path (Join-Path $env:LOCALAPPDATA "OpenQuickHost\DevBuilds") $projectKey
$stateFile = Join-Path $devRoot "active.json"
$activeSlot = ""
$previousExe = ""
if (Test-Path -LiteralPath $stateFile) {
    try {
        $state = Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($state.projectRoot -eq $ProjectRoot -and $state.slot -in @("slot-a", "slot-b")) {
            $activeSlot = [string]$state.slot
            $previousExe = Join-Path (Join-Path $devRoot $activeSlot) "Yanzi.exe"
        }
    } catch {
        Write-Warning "Ignoring invalid build state: $($_.Exception.Message)"
    }
}

if ($SkipBuild) {
    $ExePath = if ($previousExe) { $previousExe } else { $legacyExe }
    $ExeDir = Split-Path -Parent $ExePath
    $slot = $activeSlot
} else {
    $slot = if ($activeSlot -eq "slot-a") { "slot-b" } else { "slot-a" }
    $ExeDir = Join-Path $devRoot $slot
    $ExePath = Join-Path $ExeDir "Yanzi.exe"
}

Push-Location $ProjectRoot
try {
    if (-not $SkipBuild) {
        New-Item -ItemType Directory -Path $ExeDir -Force | Out-Null
        # Override the project's pre-build process killer: the staged output is not locked.
        $buildArgs = @(
            "build", $project, "-c", "Debug", "--nologo", "-v:minimal", "-m:1",
            "-p:SkipStopRunningApp=true",
            ("-p:OutputPath=" + $ExeDir + [IO.Path]::DirectorySeparatorChar)
        )
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & dotnet @buildArgs
        $code = $LASTEXITCODE
        $timer.Stop()
        Write-Host "YANZI_DEV_BUILD_MS=$($timer.ElapsedMilliseconds)"
        if ($code -ne 0) {
            throw "Desktop staging build failed (exit $code); the running version was not stopped."
        }
    }

    foreach ($required in @("Yanzi.exe", "Yanzi.dll", "Yanzi.deps.json")) {
        if (-not (Test-Path -LiteralPath (Join-Path $ExeDir $required) -PathType Leaf)) {
            throw "Incomplete staged build ($required); the running version was not stopped."
        }
    }

    if ($BuildOnly) {
        Write-Host "YANZI_DEV_STAGING_OK=$ExePath"
        return
    }

    # Keep the old version alive through build/validation. Then stop only
    # this checkout's previously tracked development executable (if any).
    $stopScript = Join-Path $PSScriptRoot "stop-desktop-build-process.ps1"
    if ($previousExe -and (Test-Path -LiteralPath $previousExe)) {
        & $stopScript -ExecutablePath $previousExe
    }
    if (Test-Path -LiteralPath $legacyExe) {
        & $stopScript -ExecutablePath $legacyExe
    }

    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = '"' + $ExePath + '" --dev --tray'
        CurrentDirectory = $ExeDir
    }

    if ($result.ReturnValue -ne 0) {
        throw "Failed to start Yanzi. Win32 error: $($result.ReturnValue)"
    }

    Start-Sleep -Seconds 2
    $process = Get-Process -Id $result.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) {
        throw "Yanzi process did not remain running after launch."
    }

    # Persist only after the new process is confirmed alive. Use an atomic
    # replacement so an interrupted update cannot corrupt the active slot.
    if ($slot -in @("slot-a", "slot-b")) {
        $state = @{
            projectRoot = $ProjectRoot
            slot = $slot
            executable = $ExePath
            installedUtc = [DateTime]::UtcNow.ToString("o")
        }
        New-Item -ItemType Directory -Path $devRoot -Force | Out-Null
        $tempState = "$stateFile.tmp"
        $state | ConvertTo-Json | Set-Content -LiteralPath $tempState -Encoding UTF8
        Move-Item -LiteralPath $tempState -Destination $stateFile -Force
    }

    Write-Host "Desktop development loop PASSED."
    Write-Host "Yanzi PID: $($process.Id)"
    Write-Host "Executable: $ExePath"
}
finally {
    Pop-Location
}

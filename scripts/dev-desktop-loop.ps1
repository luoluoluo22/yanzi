param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ExeDir = Join-Path $ProjectRoot "src\OpenQuickHost\bin\Debug\net9.0-windows"
$ExePath = Join-Path $ExeDir "Yanzi.exe"

Push-Location $ProjectRoot
try {
    # Stop only this checkout's executable, before building files it may have locked.
    & (Join-Path $PSScriptRoot "stop-desktop-build-process.ps1") -ExecutablePath $ExePath

    if (-not $SkipBuild) {
        dotnet build ".\src\OpenQuickHost\OpenQuickHost.csproj" -c Debug -v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop build failed."
        }
    }

    if (-not (Test-Path $ExePath)) {
        throw "Desktop executable not found: $ExePath"
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

    Write-Host "Desktop development loop PASSED."
    Write-Host "Yanzi PID: $($process.Id)"
    Write-Host "Executable: $ExePath"
}
finally {
    Pop-Location
}

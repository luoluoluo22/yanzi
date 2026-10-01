param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ExeDir = Join-Path $ProjectRoot "src\OpenQuickHost\bin\Debug\net9.0-windows"
$ExePath = Join-Path $ExeDir "Yanzi.exe"

Push-Location $ProjectRoot
try {
    if (-not $SkipBuild) {
        dotnet build ".\src\OpenQuickHost\OpenQuickHost.csproj" -c Debug -v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop build failed."
        }
    }

    if (-not (Test-Path $ExePath)) {
        throw "Desktop executable not found: $ExePath"
    }

    Stop-Process -Name Yanzi -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine = '"' + $ExePath + '"'
        CurrentDirectory = $ExeDir
    }

    if ($result.ReturnValue -ne 0) {
        throw "Failed to start Yanzi. Win32 error: $($result.ReturnValue)"
    }

    Start-Sleep -Seconds 2
    $process = Get-Process -Name Yanzi -ErrorAction SilentlyContinue | Select-Object -First 1
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

param([switch]$SkipBuild, [switch]$Activate)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeBuild = Join-Path $projectRoot 'src\Yanzi.Runtime\bin\Release\net9.0-windows'
$shellBuild = Join-Path $projectRoot 'src\OpenQuickHost\bin\Release\net9.0-windows'
$installationRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'YanziRuntime'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$runtimeSnapshot = Join-Path $installationRoot "versions\$stamp"
$shellSnapshot = Join-Path $installationRoot "shells\$stamp"

if (-not $SkipBuild) {
    dotnet build (Join-Path $projectRoot 'src\Yanzi.Runtime\Yanzi.Runtime.csproj') -c Release -p:SkipStopRunningApp=true -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Runtime build failed.' }
}
if (-not (Test-Path (Join-Path $runtimeBuild 'Yanzi.Runtime.exe'))) { throw 'Runtime executable is missing.' }
if (-not (Test-Path (Join-Path $shellBuild 'Yanzi.exe'))) { throw 'Shell executable is missing.' }

New-Item -ItemType Directory -Path $runtimeSnapshot, $shellSnapshot -Force | Out-Null
Get-ChildItem -LiteralPath $runtimeBuild -Force | Copy-Item -Destination $runtimeSnapshot -Recurse -Force
Get-ChildItem -LiteralPath $shellBuild -Force | Copy-Item -Destination $shellSnapshot -Recurse -Force
$runtimeExe = Join-Path $runtimeSnapshot 'Yanzi.Runtime.exe'
$shellExe = Join-Path $shellSnapshot 'Yanzi.exe'
$locationFile = Join-Path $installationRoot 'runtime.json'
$previousLocation = if (Test-Path $locationFile) { Get-Content -LiteralPath $locationFile -Raw } else { $null }

if ($Activate) {
    # Check all target paths before ending the old desktop lifetime. No name-wide process kill.
    $installedShell = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Yanzi\current\Yanzi.exe'))
    $debugShell = [IO.Path]::GetFullPath((Join-Path $projectRoot 'src\OpenQuickHost\bin\Debug\net9.0-windows\Yanzi.exe'))
    $oldProcesses = Get-CimInstance Win32_Process -Filter "Name = 'Yanzi.exe' OR Name = 'Yanzi.Runtime.exe'" |
        Where-Object {
            $_.ExecutablePath -and (
                $_.ExecutablePath.Equals($installedShell, [StringComparison]::OrdinalIgnoreCase) -or
                $_.ExecutablePath.Equals($debugShell, [StringComparison]::OrdinalIgnoreCase) -or
                $_.ExecutablePath.StartsWith($installationRoot + '\', [StringComparison]::OrdinalIgnoreCase))
        }
    foreach ($old in $oldProcesses) { Stop-Process -Id $old.ProcessId -Force -ErrorAction SilentlyContinue }
}

@{ protocolVersion = 1; executable = $runtimeExe; stableShell = $shellExe; activatedAt = [DateTimeOffset]::Now.ToString('O') } |
    ConvertTo-Json | Set-Content -LiteralPath $locationFile -Encoding utf8
if ($previousLocation) { Set-Content -LiteralPath (Join-Path $installationRoot 'runtime.previous.json') -Value $previousLocation -Encoding utf8 }

if ($Activate) {
    $runtimeProcess = Start-Process -FilePath $runtimeExe -ArgumentList '--runtime', '--tray' -WindowStyle Hidden -PassThru
    $probe = Join-Path $projectRoot 'src\Yanzi.RuntimeVerification\bin\Release\net9.0-windows\Yanzi.RuntimeVerification.exe'
    try {
        if (-not (Test-Path -LiteralPath $probe)) { throw 'Build Yanzi.RuntimeVerification before activation.' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
        $healthy = $false
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            $runtimeProcess.Refresh()
            if ($runtimeProcess.HasExited) { throw 'Runtime exited during activation.' }
            # The pipe may not exist during startup; keep retrying within the activation deadline.
            try { $statusText = & $probe --query health 2>$null }
            catch { $statusText = $null }
            if ($LASTEXITCODE -eq 0 -and $statusText) {
                $status = $statusText | ConvertFrom-Json
                if ($status.pid -eq $runtimeProcess.Id -and $status.backgroundServices.initialized) { $healthy = $true; break }
            }
            Start-Sleep -Milliseconds 400
        }
        if (-not $healthy) { throw 'Runtime health check failed.' }
        Start-Process -FilePath $shellExe -ArgumentList '--tray' -WindowStyle Hidden
    }
    catch {
        Stop-Process -Id $runtimeProcess.Id -Force -ErrorAction SilentlyContinue
        if ($previousLocation) {
            Set-Content -LiteralPath $locationFile -Value $previousLocation -Encoding utf8
            $previous = $previousLocation | ConvertFrom-Json
            Start-Process -FilePath $previous.executable -ArgumentList '--runtime', '--tray' -WindowStyle Hidden
            if ($previous.stableShell) { Start-Process -FilePath $previous.stableShell -ArgumentList '--tray' -WindowStyle Hidden }
        }
        elseif (Test-Path -LiteralPath $installedShell) {
            Remove-Item -LiteralPath $locationFile
            Start-Process -FilePath $installedShell -ArgumentList '--tray' -WindowStyle Hidden
        }
        throw
    }
}
Write-Host "Runtime snapshot: $runtimeExe"
Write-Host "Stable Shell snapshot: $shellExe"
Write-Host "Runtime location: $locationFile"

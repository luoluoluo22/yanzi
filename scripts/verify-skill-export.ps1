$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    Write-Output 'STEP restore-host'
    & dotnet restore 'src/OpenQuickHost/OpenQuickHost.csproj' --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw 'Host restore failed.' }

    Write-Output 'STEP build-host'
    & dotnet build 'src/OpenQuickHost/OpenQuickHost.csproj' --no-restore -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }

    Write-Output 'STEP build-verification-harness'
    & dotnet build 'tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj' --no-restore -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'Verification harness build failed.' }

    Write-Output 'STEP run-verification-harness'
    $verificationOutput = @(& dotnet run --project 'tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj' --no-build --configuration Debug)
    $verificationExitCode = $LASTEXITCODE
    $verificationOutput | ForEach-Object { Write-Output $_ }
    if ($verificationExitCode -ne 0) { throw 'WPF UI verification harness failed.' }

    $summary = ($verificationOutput | Where-Object { $_ -match '^RESULT pass=[0-9]+ fail=[0-9]+$' } | Select-Object -Last 1)
    if (!$summary -or $summary -notmatch '^RESULT pass=[0-9]+ fail=0$') {
        throw 'Verification summary did not report zero failures.'
    }

    Write-Output $summary
    Write-Output 'STEP launch-isolated-development-app'
    $devExe = Join-Path $repoRoot 'src/OpenQuickHost/bin/Debug/net9.0-windows/Yanzi.exe'
    if (!(Test-Path -LiteralPath $devExe -PathType Leaf)) { throw 'Compiled Yanzi development executable was not found.' }
    $devProcess = Start-Process -FilePath $devExe -ArgumentList '--dev' -PassThru
    Start-Sleep -Seconds 2
    if (!(Get-Process -Id $devProcess.Id -ErrorAction SilentlyContinue)) {
        throw 'Compiled Yanzi development app did not remain running after launch.'
    }
    Write-Output ('DEV_APP_LAUNCHED_PID=' + $devProcess.Id)
    Write-Output 'SKILL_EXPORT_VERIFICATION_PASSED'
}
finally {
    Pop-Location
}

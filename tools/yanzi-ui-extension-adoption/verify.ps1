param([switch]$SkipRuntime)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $repo
try {
    & (Join-Path $PSScriptRoot 'install-v2.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Optimized extension staging failed' }
    $checks = @(
        @{ Name = 'Host dynamic compiler + staged extensions'; Command = @(
            'dotnet', 'run', '--project',
            'tools/yanzi-ui-extension-adoption/CompilerVerification/CompilerVerification.csproj',
            '-c', 'Release', '--no-restore') },
        @{ Name = 'Shared WPF control regressions'; Command = @(
            'dotnet', 'run', '--project',
            'src/Yanzi.UI.Verification/Yanzi.UI.Verification.csproj',
            '-c', 'Release', '--no-restore') }
    )
    if (-not $SkipRuntime) {
        $checks += @{ Name = 'Isolated Runtime lifecycle'; Command = @(
            'dotnet', 'run', '--project',
            'src/Yanzi.RuntimeVerification/Yanzi.RuntimeVerification.csproj',
            '-c', 'Release', '--no-restore', '--',
            'src/Yanzi.Runtime/bin/Release/net9.0-windows/Yanzi.Runtime.exe') }
    }
    foreach ($check in $checks) {
        Write-Host ('RUN ' + $check.Name)
        $log = Join-Path $env:TEMP ('yanzi-ui-check-' + [guid]::NewGuid().ToString('N') + '.log')
        try {
            $command = $check.Command
            & $command[0] $command[1..($command.Length - 1)] *> $log
            $exit = $LASTEXITCODE
            Get-Content $log -Tail 7
            if ($exit -ne 0) { throw ($check.Name + ' FAILED (exit ' + $exit + ')') }
        }
        finally { Remove-Item $log -ErrorAction SilentlyContinue }
    }
    Write-Host 'PASS: repeatable public UI adoption verification completed'
}
finally { Pop-Location }

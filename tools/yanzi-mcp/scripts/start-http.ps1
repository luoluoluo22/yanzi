param()
$ErrorActionPreference = 'Stop'
$toolDirectory = Split-Path $PSScriptRoot -Parent
$runtimeDirectory = Join-Path $toolDirectory '.runtime'
New-Item $runtimeDirectory -ItemType Directory -Force | Out-Null
$startupMutex = New-Object System.Threading.Mutex($false, 'Local\YanziMcpHttpStartup')
$locked = $false
try {
    $locked = $startupMutex.WaitOne(0)
    if (!$locked) { exit 0 }
    try {
        $health = Invoke-RestMethod 'http://127.0.0.1:3767/health' -TimeoutSec 3
        if ($health.ok -and $health.name -eq 'yanzi-mcp') { exit 0 }
        throw 'Port 3767 belongs to another service.'
    } catch {
        if ($_.Exception.Message -eq 'Port 3767 belongs to another service.') { throw }
    }
    if (Get-NetTCPConnection -LocalPort 3767 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 3767 is occupied but its health check failed; existing process preserved.' }
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    foreach ($logName in @('http.stdout.log','http.stderr.log')) {
        $logPath = Join-Path $runtimeDirectory $logName
        if (Test-Path $logPath) { Move-Item -LiteralPath $logPath -Destination ($logPath + '.' + (Get-Date -Format yyyyMMddHHmmssfff)) }
    }
    $serviceProcess = Start-Process -FilePath $nodePath -ArgumentList 'src/http.js' -WorkingDirectory $toolDirectory -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtimeDirectory 'http.stdout.log') -RedirectStandardError (Join-Path $runtimeDirectory 'http.stderr.log') -PassThru
    [IO.File]::WriteAllText((Join-Path $runtimeDirectory 'http.pid'), [string]$serviceProcess.Id)
    Start-Sleep -Seconds 1
    if ($serviceProcess.HasExited) { throw 'HTTP process exited during startup.' }
    $health = Invoke-RestMethod 'http://127.0.0.1:3767/health' -TimeoutSec 5
    if (!$health.ok -or $health.name -ne 'yanzi-mcp') { throw 'HTTP startup check failed.' }
    [IO.File]::AppendAllText((Join-Path $runtimeDirectory 'startup.log'), (Get-Date -Format o) + ' started pid=' + $serviceProcess.Id + [Environment]::NewLine)
} catch {
    [IO.File]::AppendAllText((Join-Path $runtimeDirectory 'startup.log'), (Get-Date -Format o) + ' startup failed: ' + $_.Exception.Message + [Environment]::NewLine)
    exit 1
} finally {
    if ($locked) { $startupMutex.ReleaseMutex() }
    $startupMutex.Dispose()
}

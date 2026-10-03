param([int]$Port = 8823)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$artifact = Join-Path $env:TEMP ('YanziDev\device-foundation\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $artifact | Out-Null
$config = Join-Path $repo 'cloudflare\wrangler.toml'
$state = Join-Path $artifact 'worker-state'
$base = "http://127.0.0.1:$Port"
$worker = $null
try {
    Stop-YanziLocalWorkerPort -Port $Port
    npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $state --config $config | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local D1 migrations failed.' }
    $workerArgs = @('wrangler','dev','--local','--persist-to',$state,'--ip','127.0.0.1','--port',$Port,'--config',$config,'--var','AUTH_TOKEN_SECRET:local-phone-message-verification')
    $worker = Start-Process npx.cmd -ArgumentList $workerArgs -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifact 'worker.log') -RedirectStandardError (Join-Path $artifact 'worker.err') -PassThru
    $ready = $false
    $deadline = (Get-Date).AddSeconds(60)
    do {
        try { $ready = (Invoke-RestMethod "$base/health" -TimeoutSec 1).ok } catch { $ready = $false }
        if (-not $ready) { Start-Sleep -Milliseconds 250 }
    } while (-not $ready -and (Get-Date) -lt $deadline)
    if (-not $ready) { throw 'Isolated Worker did not start.' }
    $fixture = Join-Path $artifact 'fixture.json'
    [IO.File]::WriteAllText($fixture,(@{baseUrl=$base} | ConvertTo-Json),[Text.UTF8Encoding]::new($true))
    & (Join-Path $PSScriptRoot 'test-device-message-protocol.ps1') -FixturePath $fixture
    # Reusing the same Worker must not collide with the preceding test's device identities.
    & (Join-Path $PSScriptRoot 'test-device-message-protocol.ps1') -FixturePath $fixture
    node (Join-Path $repo 'protocol\sdk\verify-worker.mjs') $base
    if ($LASTEXITCODE -ne 0) { throw 'Generic device SDK end-to-end verification failed.' }
    [IO.File]::WriteAllText((Join-Path $artifact 'result.txt'),'DEVICE_NETWORK_FOUNDATION=PASSED',[Text.UTF8Encoding]::new($true))
    Write-Output "DEVICE_NETWORK_FOUNDATION=PASSED; Artifacts: $artifact"
} finally {
    Stop-YanziLocalWorkerPort -Port $Port
    if ($worker -and -not $worker.HasExited) { cmd.exe /d /c "taskkill /PID $($worker.Id) /T /F >nul 2>nul" }
}

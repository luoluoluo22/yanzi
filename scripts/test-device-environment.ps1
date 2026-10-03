param([int]$Port=8854,[switch]$Android)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$repo=Split-Path -Parent $PSScriptRoot
$artifact=Join-Path $env:TEMP ('YanziDev\environment-integration\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $artifact | Out-Null
$config=Join-Path $repo 'cloudflare\wrangler.toml'
$state=Join-Path $artifact 'worker-state'
$base="http://127.0.0.1:$Port"
$worker=$null
try {
    Stop-YanziLocalWorkerPort -Port $Port
    npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $state --config $config | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Local migrations failed'}
    $worker=Start-Process npx.cmd -ArgumentList @('wrangler','dev','--local','--persist-to',$state,'--ip','127.0.0.1','--port',$Port,'--config',$config,'--var','AUTH_TOKEN_SECRET:local-phone-message-verification') -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifact 'worker.log') -RedirectStandardError (Join-Path $artifact 'worker.err') -PassThru
    $deadline=(Get-Date).AddSeconds(60);$ready=$false
    do {try{$ready=(Invoke-RestMethod "$base/health" -TimeoutSec 1).ok}catch{$ready=$false};if(-not $ready){Start-Sleep -Milliseconds 300}}while(-not $ready -and (Get-Date)-lt $deadline)
    if(-not $ready){throw 'Worker startup timeout'}
    $fixture=Join-Path $artifact 'fixture.json'
    node (Join-Path $PSScriptRoot 'verify-device-environment.mjs') $base $fixture
    if($LASTEXITCODE -ne 0){throw 'Real environment API failed'}
    if($Android){python (Join-Path $PSScriptRoot 'test-android-environment.py') --serial emulator-5554 --fixture $fixture --output $artifact; if($LASTEXITCODE -ne 0){throw 'Android environment integration failed'}}
    Write-Output "ENVIRONMENT_INTEGRATION=PASSED; artifacts=$artifact"
} finally {
    Stop-YanziLocalWorkerPort -Port $Port
    if($worker -and -not $worker.HasExited){Stop-Process -Id $worker.Id -Force -ErrorAction SilentlyContinue}
}

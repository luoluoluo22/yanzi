param([int]$Port=8812)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$repo=Split-Path -Parent $PSScriptRoot
$root=Join-Path $env:TEMP ('YanziDev\mobile-backend\'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$config=Join-Path $repo 'cloudflare\wrangler.toml'
$fixturePath=Join-Path $root 'fixture.json'
$worker=$null
try {
    Stop-YanziLocalWorkerPort -Port $Port
    npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $root --config $config | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local migrations failed' }
    $worker=Start-Process npx.cmd -ArgumentList @('wrangler','dev','--local','--persist-to',$root,'--ip','127.0.0.1','--port',$Port,'--config',$config,'--var','AUTH_TOKEN_SECRET:local-phone-message-verification') -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'worker.log') -RedirectStandardError (Join-Path $root 'worker.err') -PassThru
    $base="http://127.0.0.1:$Port"
    $ready=$false
    foreach ($attempt in 1..40) { try { $ready=(Invoke-RestMethod "$base/health" -TimeoutSec 1).ok } catch {} ; if ($ready) { break }; Start-Sleep -Milliseconds 500 }
    if (-not $ready) { throw 'Local Worker unavailable' }
    function Encode([byte[]]$bytes) { [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
    $user='backend-test-'+[Guid]::NewGuid().ToString('N')
    $device='desktop-test-'+[Guid]::NewGuid().ToString('N')
    $jwtHeader=Encode ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $jwtPayload=Encode ([Text.Encoding]::UTF8.GetBytes((@{sub=$user;username='backend-test';exp=([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+600)} | ConvertTo-Json -Compress)))
    $signer=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('local-phone-message-verification'))
    $token="$jwtHeader.$jwtPayload."+(Encode ($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes("$jwtHeader.$jwtPayload"))))
    $signer.Dispose()
    Invoke-RestMethod "$base/v1/me/devices" -Method POST -Headers @{Authorization="Bearer $token"} -ContentType 'application/json' -Body (@{deviceId=$device;platform='desktop';displayName='backend-test';capabilities=@{}} | ConvertTo-Json -Compress) | Out-Null
    [IO.File]::WriteAllText($fixturePath,(@{baseUrl=$base;token=$token;desktopDeviceId=$device} | ConvertTo-Json -Compress))
    & (Join-Path $PSScriptRoot 'test-mobile-attachments.ps1') -FixturePath $fixturePath
    [IO.File]::WriteAllText((Join-Path $root 'result.txt'),'MOBILE_BACKEND=PASSED')
    Write-Host 'MOBILE_BACKEND=PASSED'
} finally {
    if ($worker) { Stop-YanziLocalWorkerPort -Port $Port }
    if (Test-Path $fixturePath) { [IO.File]::Delete($fixturePath) }
    Write-Host "Artifacts: $root"
}

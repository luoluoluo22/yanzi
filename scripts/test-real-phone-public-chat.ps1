param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$adb = 'F:\SDK\platform-tools\adb.exe'
$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' -and $_ -notmatch '^emulator-' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($serials.Count -ne 1) { throw 'Exactly one physical phone is required.' }
$serial = $serials[0]
$package = 'cc.luoluoluo.yanzi.mobile.dev'
[xml]$prefs = (@(& $adb -s $serial exec-out run-as $package cat shared_prefs/yanzi-mobile.xml) -join "`n")
$target = [string](@($prefs.map.string | Where-Object {$_.name -eq 'deviceId'})[0].'#text')
$existing = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$artifact = Join-Path $env:TEMP ('YanziDev\public-chat\' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $artifact 'desktop'
[IO.Directory]::CreateDirectory($root) | Out-Null
$process = $null
try {
    foreach ($name in @('synccredentials.dat','syncsession.json','syncsettings.json','device-identity.json')) {
        Copy-Item -LiteralPath (Join-Path $existing $name) -Destination (Join-Path $root $name)
    }
    $last = Get-Content (Join-Path $existing 'mobile-inbox.jsonl') | Where-Object {
        try { ($_ | ConvertFrom-Json).sourceDeviceId -eq $target } catch { $false }
    } | Select-Object -Last 1
    if (-not $last) { throw 'Send a message from Dev phone to PC before running this test.' }
    [IO.File]::WriteAllText((Join-Path $root 'mobile-inbox.jsonl'),$last + [Environment]::NewLine)
    $base = (Get-Content (Join-Path $existing 'syncsettings.json') -Raw | ConvertFrom-Json).baseUrl
    $desktopId = (Get-Content (Join-Path $existing 'device-identity.json') -Raw | ConvertFrom-Json).deviceId
    $fixturePath = Join-Path $artifact 'fixture.json'
    [IO.File]::WriteAllText($fixturePath,(@{root=$root;baseUrl=$base;desktopDeviceId=$desktopId;useExistingAccount=$true} | ConvertTo-Json -Compress))
    $exe = Join-Path $repo 'src\Yanzi.SyncVerification\bin\Debug\net9.0-windows\Yanzi.SyncVerification.exe'
    $process = Start-Process -FilePath $exe -ArgumentList @('--mobile-message-bridge',('"'+$fixturePath+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifact 'desktop.stdout') -RedirectStandardError (Join-Path $artifact 'desktop.stderr')
    $end = (Get-Date).AddSeconds(40)
    while (-not (Test-Path (Join-Path $root 'ready'))) {
        if ($process.HasExited -or (Get-Date) -gt $end) { throw 'Public chat verification desktop did not become ready.' }
        Start-Sleep -Milliseconds 500
    }
    foreach ($staleLan in @($false,$true)) {
        $marker = 'public-chat-test-' + [Guid]::NewGuid().ToString('N')
        $resultPath = Join-Path $root 'chat-result.json'
        Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
        [IO.File]::WriteAllText((Join-Path $root 'chat-request.json'),(@{text=$marker;staleLan=$staleLan} | ConvertTo-Json -Compress))
        $end = (Get-Date).AddSeconds(60)
        while (-not (Test-Path $resultPath)) { if ((Get-Date) -gt $end) {throw 'Public chat send timed out.'}; Start-Sleep -Milliseconds 500 }
        $result = Get-Content $resultPath -Raw | ConvertFrom-Json
        if ($result.PSObject.Properties['error'] -or $result.input -ne '' -or -not $result.status) {throw 'Public chat window did not enqueue message.'}
        $end = (Get-Date).AddSeconds(90)
        $received = $false
        do {
            [xml]$phone = (@(& $adb -s $serial exec-out run-as $package cat shared_prefs/yanzi-mobile.xml) -join "`n")
            $history = @($phone.map.string | Where-Object {$_.name -eq 'desktop_chat_history'})
            if ($history.Count -gt 0 -and ([string]$history[0].'#text').Contains($marker)) {$received=$true;break}
            Start-Sleep -Seconds 2
        } while ((Get-Date) -lt $end)
        if (-not $received) {throw 'Public message was not saved in phone chat history.'}
        if ($staleLan) {Write-Host 'PUBLIC_CHAT_STALE_LAN_FALLBACK=PASSED'} else {Write-Host 'PUBLIC_CHAT_WITHOUT_LAN=PASSED'}
    }
    [IO.File]::WriteAllText((Join-Path $artifact 'result.txt'),'PUBLIC_CHAT_WINDOW_TO_PHONE=PASSED')
} finally {
    if ($process -and -not $process.HasExited) {
        New-Item -ItemType File -Force (Join-Path $root 'stop') | Out-Null
        if (-not $process.WaitForExit(5000)) {$process.Kill()}
    }
    foreach ($name in @('synccredentials.dat','syncsession.json','mobile-inbox.jsonl','device-identity.json')) {
        Remove-Item -LiteralPath (Join-Path $root $name) -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Artifacts: $artifact"
}

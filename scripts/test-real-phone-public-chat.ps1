param([switch]$TextOnly)
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
$testAttachments = [Collections.Generic.List[string]]::new()
function WaitPublic([scriptblock]$Check,[string]$Label,[int]$Seconds=120) {
    $deadline=(Get-Date).AddSeconds($Seconds)
    do { if (& $Check) { Write-Host "$Label=PASSED"; return }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline)
    throw "Timed out: $Label"
}
function PublicRequest([string]$Path,[string]$Method='GET',$Body=$null) {
    $session=Get-Content (Join-Path $root 'syncsession.json') -Raw | ConvertFrom-Json
    $options=@{Uri=($base+$Path);Method=$Method;TimeoutSec=30;Headers=@{Authorization=('Bearer '+$session.AccessToken);'X-Yanzi-Client'='desktop';'User-Agent'='YanziClient-Desktop/verification'}}
    if ($null -ne $Body) { $options.ContentType='application/json';$options.Body=[Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 8 -Compress)) }
    Invoke-RestMethod @options
}
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
    if (-not $TextOnly) {
        $latencies=@()
        foreach ($i in 1..10) {
            $sent=PublicRequest '/v1/me/mobile/messages' 'POST' @{sourceDeviceId=$desktopId;targetDeviceId=$target;targetPlatform='android';kind='notify';title='Public latency verification';text=('public-latency-'+$i);payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')}
            $id=$sent.messageId
            WaitPublic { (PublicRequest "/v1/me/mobile/messages/$id").status -eq 'acked' } 'PUBLIC_PHONE_ACK'
            $detail=PublicRequest "/v1/me/mobile/messages/$id"
            $latencies+=([DateTimeOffset]$detail.ackedAt-[DateTimeOffset]$detail.createdAt).TotalMilliseconds
        }
        $ordered=@($latencies | Sort-Object)
        [IO.File]::WriteAllText((Join-Path $artifact 'latency.json'),(@{sampleCount=10;medianMs=$ordered[5];p95Ms=$ordered[9];samplesMs=$latencies} | ConvertTo-Json))
        foreach ($photo in @($false,$true)) {
            $name=if ($photo) {'public-verification.png'} else {'public-verification.bin'}
            $filePath=Join-Path $artifact $name
            $bytes=if ($photo) {[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD1sAAAAASUVORK5CYII=')} else {[Text.Encoding]::UTF8.GetBytes(('public-attachment-'+[Guid]::NewGuid().ToString('N'))*12000)}
            [IO.File]::WriteAllBytes($filePath,$bytes)
            $hash=(Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $resultPath=Join-Path $root 'chat-result.json'
            Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
            [IO.File]::WriteAllText((Join-Path $root 'chat-request.json'),(@{text='';staleLan=$false;filePath=$filePath;isPhoto=$photo} | ConvertTo-Json -Compress))
            WaitPublic { Test-Path $resultPath } 'PUBLIC_WINDOW_ATTACHMENT_SEND'
            $result=Get-Content $resultPath -Raw | ConvertFrom-Json
            if ($result.PSObject.Properties['error'] -or -not $result.messageId) {throw 'Public attachment window send failed; inspect artifact.'}
            $detail=PublicRequest ("/v1/me/mobile/messages/"+$result.messageId)
            $testAttachments.Add($detail.payload.attachmentId)
            $attachmentId=$detail.payload.attachmentId
            WaitPublic { (PublicRequest ("/v1/me/mobile/messages/"+$result.messageId)).status -eq 'acked' } 'PUBLIC_ATTACHMENT_PHONE_ACK'
            WaitPublic { (@(& $adb -s $serial shell run-as $package sh -c ('"sha256sum files/mobile-attachments/'+$attachmentId+'* 2>/dev/null"')) -join "`n").Contains($hash) } 'PUBLIC_PC_TO_PHONE_ATTACHMENT_HASH'
            & $adb -s $serial push $filePath /data/local/tmp/verification-attachment | Out-Null
            & $adb -s $serial shell run-as $package mkdir -p cache | Out-Null
            & $adb -s $serial shell run-as $package cp /data/local/tmp/verification-attachment cache/verification-attachment | Out-Null
            $kind=if ($photo) {'photo'} else {'file'}
            $beforeFiles=@(Get-ChildItem (Join-Path $existing 'mobile-attachments') -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
            & $adb -s $serial shell am start -f 0x10008000 -n "$package/cc.luoluoluo.yanzi.mobile.MainActivity" --es verify_message_kind $kind --es verify_message_text public-attachment-roundtrip | Out-Null
            WaitPublic {
                $files=@(Get-ChildItem (Join-Path $existing 'mobile-attachments') -File -ErrorAction SilentlyContinue | Where-Object {$_.Extension -ne '.part'})
                @($files | Where-Object { $beforeFiles -notcontains $_.FullName -and (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash }).Count -gt 0
            } 'PUBLIC_PHONE_TO_PC_ATTACHMENT_HASH'
            $incoming=Get-Content (Join-Path $existing 'mobile-inbox.jsonl') | ForEach-Object { try { $_ | ConvertFrom-Json } catch {} } | Where-Object {$_.text -eq 'public-attachment-roundtrip' -and $_.payload.sha256 -eq $hash} | Select-Object -Last 1
            if ($incoming -and $incoming.payload.attachmentId) { $testAttachments.Add($incoming.payload.attachmentId) }
        }
        & $adb -s $serial shell am start -n "$package/cc.luoluoluo.yanzi.mobile.MainActivity" | Out-Null
        Write-Host 'PUBLIC_BIDIRECTIONAL_ATTACHMENTS=PASSED'
        $desktopLog=Join-Path $existing 'logs\host.log'
        WaitPublic { ((Get-Content $desktopLog -Tail 100) -join "`n") -match 'Mobile bridge realtime connected' } 'PUBLIC_WINDOWS_WEBSOCKET'
        WaitPublic { @((PublicRequest '/v1/me/devices').items | Where-Object {$_.deviceId -eq $target -and $_.online -and $_.capabilities.realtime}).Count -eq 1 } 'PUBLIC_ANDROID_WEBSOCKET'
        & $adb -s $serial shell am force-stop $package | Out-Null
        $recovery=PublicRequest '/v1/me/mobile/messages' 'POST' @{sourceDeviceId=$desktopId;targetDeviceId=$target;targetPlatform='android';kind='notify';title='Public recovery verification';text='public-recovery-test';payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')}
        Start-Sleep -Seconds 3
        if ((PublicRequest ("/v1/me/mobile/messages/"+$recovery.messageId)).status -ne 'pending') {throw 'Stopped phone unexpectedly acknowledged message'}
        & $adb -s $serial shell am start -n "$package/cc.luoluoluo.yanzi.mobile.MainActivity" | Out-Null
        WaitPublic { (PublicRequest ("/v1/me/mobile/messages/"+$recovery.messageId)).status -eq 'acked' } 'PUBLIC_PROCESS_RESTART_PENDING_RECOVERY'
        $crashPid=(@(& $adb -s $serial shell pidof $package)[0]).Trim()
        if ((@(& $adb -s $serial logcat --pid=$crashPid -d -t 300) -join "`n") -match 'FATAL EXCEPTION|ANR in') {throw 'Physical phone crashed during public verification'}
    }
    [IO.File]::WriteAllText((Join-Path $artifact 'result.txt'),'PUBLIC_CHAT_WINDOW_TO_PHONE=PASSED')
} finally {
    foreach ($id in $testAttachments) {
        try { PublicRequest "/v1/me/mobile/attachments/$id" 'DELETE' | Out-Null } catch { Write-Warning 'Temporary public attachment cleanup deferred.' }
    }
    if ($process -and -not $process.HasExited) {
        New-Item -ItemType File -Force (Join-Path $root 'stop') | Out-Null
        if (-not $process.WaitForExit(5000)) {$process.Kill()}
    }
    foreach ($name in @('synccredentials.dat','syncsession.json','mobile-inbox.jsonl','device-identity.json')) {
        Remove-Item -LiteralPath (Join-Path $root $name) -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Artifacts: $artifact"
}

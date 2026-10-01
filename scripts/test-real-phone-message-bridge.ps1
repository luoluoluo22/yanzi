param([int]$Port = 8811, [switch]$SkipBuild, [switch]$IncludeFinalScreenOff)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
$activity = 'cc.luoluoluo.yanzi.mobile.MainActivity'
$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' -and $_ -notmatch '^emulator-' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($serials.Count -ne 1) { throw 'Exactly one authorized physical phone is required.' }
$serial = $serials[0]
$artifact = Join-Path $env:TEMP ('YanziDev\message-bridge\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $artifact | Out-Null
$desktopRoot = Join-Path $artifact 'desktop'
$backup = Join-Path $artifact 'preferences-backup.tar'
$config = Join-Path $repo 'cloudflare\wrangler.toml'
$base = "http://127.0.0.1:$Port"
$secret = 'local-phone-message-verification'
$user = 'phone-message-' + [Guid]::NewGuid().ToString('N')
$device = 'android-bridge-' + [Guid]::NewGuid().ToString('N')
$desktopDevice = 'desktop-bridge-' + [Guid]::NewGuid().ToString('N')
$fixturePath = $null
$prefsFile = $null
$restored = $false
$desktop = $null
$worker = $null

function AdbChecked([string[]]$Arguments) {
    $result = & $adb -s $serial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB operation failed: $($Arguments[0])" }
    return $result
}
function SnapshotProduction {
    $path = @(& $adb -s $serial shell pm path cc.luoluoluo.yanzi.mobile) -join ''
    $version = @(& $adb -s $serial shell dumpsys package cc.luoluoluo.yanzi.mobile | Select-String 'versionCode=|versionName=' | Select-Object -First 2) -join ''
    return "$path|$version"
}
function Request([string]$Path, [string]$Method = 'GET', $Body = $null) {
    $options = @{ Uri = "$base$Path"; Headers = $headers; Method = $Method; TimeoutSec = 10 }
    if ($null -ne $Body) { $options.ContentType = 'application/json'; $options.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 12 -Compress)) }
    return Invoke-RestMethod @options
}
function WaitUntil([scriptblock]$Check, [string]$Label, [int]$Seconds = 45) {
    $end = (Get-Date).AddSeconds($Seconds)
    do { if (& $Check) { Write-Host "$Label=PASSED"; return }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $end)
    throw "Timed out: $Label"
}
function SendNotification([string]$Label, [string]$Kind = 'notify') {
    return (Request '/v1/me/mobile/messages' 'POST' @{
        sourceDeviceId = $desktopDevice; targetDeviceId = $device; targetPlatform = 'android'
        kind = $Kind; title = $Label; text = "Chinese notification: $([char]0x624b)$([char]0x673a)$([char]0x7535)$([char]0x8111)"; payload = @{}
    }).messageId
}
function MessageStatus([string]$Id) { return (Request "/v1/me/mobile/messages/$Id").status }
function StartPhone { AdbChecked @('shell','am','start','-n',"$package/$activity") | Out-Null }
function SaveBinaryOutput([string]$Arguments, [string]$Path) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $adb; $start.Arguments = "-s $serial $Arguments"
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true; $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    $stream = [IO.File]::Create($Path)
    try { $process.StandardOutput.BaseStream.CopyTo($stream) } finally { $stream.Dispose() }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Binary ADB export failed.' }
    $process.Dispose()
}
function WritePhonePrefs([string]$File, [string]$Name) {
    AdbChecked @('push',$File,"/data/local/tmp/$Name") | Out-Null
    AdbChecked @('shell','run-as',$package,'mkdir','-p','shared_prefs') | Out-Null
    AdbChecked @('shell','run-as',$package,'cp',"/data/local/tmp/$Name","shared_prefs/$Name") | Out-Null
}

$production = SnapshotProduction
if ($production -notmatch 'package:') { throw 'Production package must exist for preservation check.' }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-android-mvp.ps1') -Configuration dev
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
    & (Join-Path $PSScriptRoot 'dev-desktop-loop.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build and restart failed.' }
    dotnet build (Join-Path $repo 'src\Yanzi.SyncVerification\Yanzi.SyncVerification.csproj') -p:BuildProjectReferences=false -p:SkipStopRunningApp=true -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Windows verification build failed.' }
}
AdbChecked @('install','-r',(Join-Path $repo 'mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk')) | Out-Null
AdbChecked @('shell','am','force-stop',$package) | Out-Null
AdbChecked @('shell','run-as',$package,'mkdir','-p','shared_prefs') | Out-Null
SaveBinaryOutput "exec-out run-as $package tar -cf - shared_prefs" $backup
$originalAppOps = @(& $adb -s $serial shell cmd appops get $package POST_NOTIFICATION) -join ''
$originalMode = if ($originalAppOps -match 'POST_NOTIFICATION:\s*(\w+)') { $Matches[1] } else { 'default' }
$permissionGranted = @(& $adb -s $serial shell dumpsys package $package | Select-String 'android.permission.POST_NOTIFICATIONS: granted=true').Count -gt 0
$sdk = [int](@(& $adb -s $serial shell getprop ro.build.version.sdk)[0])

try {
    Stop-YanziLocalWorkerPort -Port $Port
    $workerState = Join-Path $artifact 'worker-state'
    npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --persist-to $workerState --config $config | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local D1 migrations failed.' }
    $workerArgs = @('wrangler','dev','--local','--persist-to',$workerState,'--ip','127.0.0.1','--port',$Port,'--config',$config,'--var',"AUTH_TOKEN_SECRET:$secret")
    $worker = Start-Process npx.cmd -ArgumentList $workerArgs -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifact 'worker.log') -RedirectStandardError (Join-Path $artifact 'worker.err') -PassThru
    WaitUntil { try { (Invoke-RestMethod "$base/health" -TimeoutSec 1).ok } catch { $false } } 'LOCAL_WORKER_READY'
    function B64([byte[]]$Bytes) { [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
    $header = B64 ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payload = B64 ([Text.Encoding]::UTF8.GetBytes((@{sub=$user;username='bridge-test';exp=([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+1200)} | ConvertTo-Json -Compress)))
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
    $token = "$header.$payload." + (B64 ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload"))))
    $hmac.Dispose()
    $headers = @{Authorization="Bearer $token"}
    $fixturePath = Join-Path $artifact 'fixture.json'
    [IO.File]::WriteAllText($fixturePath, (@{root=$desktopRoot;baseUrl=$base;token=$token;userId=$user;desktopDeviceId=$desktopDevice} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $verify = Join-Path $repo 'src\Yanzi.SyncVerification\bin\Debug\net9.0-windows\Yanzi.SyncVerification.exe'
    $desktop = Start-Process $verify -ArgumentList @('--mobile-message-bridge',('"'+$fixturePath+'"')) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifact 'desktop.stdout') -RedirectStandardError (Join-Path $artifact 'desktop.stderr') -PassThru
    WaitUntil {
        if ($desktop.HasExited) { throw 'Isolated Windows bridge exited; see desktop.stderr.' }
        Test-Path (Join-Path $desktopRoot 'ready')
    } 'WINDOWS_BRIDGE_AND_DEDUP'
    AdbChecked @('reverse',"tcp:$Port","tcp:$Port") | Out-Null
    $prefsFile = Join-Path $artifact 'yanzi-mobile.xml'
    $xml = "<?xml version='1.0' encoding='utf-8'?><map><string name='baseUrl'>$base</string><string name='token'>$token</string><string name='deviceId'>$device</string><string name='username'>bridge-test</string><boolean name='floatingWheelEnabled' value='false'/></map>"
    [IO.File]::WriteAllText($prefsFile,$xml,[Text.UTF8Encoding]::new($false))
    WritePhonePrefs $prefsFile 'yanzi-mobile.xml'
    $lanFile = Join-Path $artifact 'YanziPrefs.xml'
    [IO.File]::WriteAllText($lanFile,"<?xml version='1.0' encoding='utf-8'?><map><string name='lanApiToken'>bridge-lan-test</string></map>",[Text.UTF8Encoding]::new($false))
    WritePhonePrefs $lanFile 'YanziPrefs.xml'
    if ($sdk -ge 33) { AdbChecked @('shell','pm','grant',$package,'android.permission.POST_NOTIFICATIONS') | Out-Null }
    AdbChecked @('shell','cmd','appops','set',$package,'POST_NOTIFICATION','allow') | Out-Null
    StartPhone
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device -and $_.online}).Count -eq 1 } 'PHONE_CLOUD_ONLINE'
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.platform -eq 'desktop' -and $_.online}).Count -eq 1 } 'DESKTOP_CLOUD_ONLINE'
    $first = SendNotification 'foreground-test'
    WaitUntil { (MessageStatus $first) -eq 'acked' } 'FOREGROUND_NOTIFICATION_ACK'
    $notificationDump = @(& $adb -s $serial shell dumpsys notification --noredact) -join "`n"
    if ($notificationDump -notmatch [regex]::Escape($first)) { throw 'ACK received without a real Android notification.' }
    Write-Host 'ANDROID_NOTIFICATION_RECORD=PASSED'
    AdbChecked @('shell','input','keyevent','3') | Out-Null
    $background = SendNotification 'background-test'
    WaitUntil { (MessageStatus $background) -eq 'acked' } 'BACKGROUND_NOTIFICATION_ACK'
    $seenBefore = @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device})[0].lastSeenAt
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device})[0].lastSeenAt -ne $seenBefore } 'BACKGROUND_HEARTBEAT_ADVANCED'
    AdbChecked @('shell','cmd','appops','set',$package,'POST_NOTIFICATION','ignore') | Out-Null
    $denied = SendNotification 'permission-denied-test'
    Start-Sleep -Seconds 7
    if ((MessageStatus $denied) -ne 'pending') { throw 'Permission denied message was silently acknowledged.' }
    Write-Host 'PERMISSION_DENIED_KEEPS_PENDING=PASSED'
    AdbChecked @('shell','cmd','appops','set',$package,'POST_NOTIFICATION','allow') | Out-Null
    WaitUntil { (MessageStatus $denied) -eq 'acked' } 'PERMISSION_REENABLE_DELIVERY'
    $unsupported = SendNotification 'unsupported-test' 'unknown-kind'
    WaitUntil { (MessageStatus $unsupported) -eq 'failed' } 'UNSUPPORTED_KIND_REJECTED'
    AdbChecked @('reverse','--remove',"tcp:$Port") | Out-Null
    $offline = SendNotification 'disconnect-test'
    Start-Sleep -Seconds 12
    if ((MessageStatus $offline) -ne 'pending') { throw 'Disconnected phone unexpectedly consumed message.' }
    AdbChecked @('reverse',"tcp:$Port","tcp:$Port") | Out-Null
    WaitUntil { (MessageStatus $offline) -eq 'acked' } 'DISCONNECT_RECONNECT_DELIVERY' 75
    AdbChecked @('shell','am','start','-f','0x10008000','-n',"$package/$activity",'--es','verify_message_text','phone-roundtrip-text') | Out-Null
    WaitUntil { Test-Path (Join-Path $desktopRoot 'mobile-inbox.jsonl') } 'PHONE_TO_WINDOWS_TEXT'
    foreach ($staleLan in @($false,$true)) {
        $chatText = if ($staleLan) { 'chat-stale-lan-cloud-test' } else { 'chat-no-lan-cloud-test' }
        $chatResultPath = Join-Path $desktopRoot 'chat-result.json'
        Remove-Item -LiteralPath $chatResultPath -Force -ErrorAction SilentlyContinue
        [IO.File]::WriteAllText((Join-Path $desktopRoot 'chat-request.json'), (@{ text=$chatText; staleLan=$staleLan } | ConvertTo-Json -Compress))
        WaitUntil { Test-Path $chatResultPath } 'CHAT_WINDOW_SEND_COMPLETED'
        $chatResult = Get-Content $chatResultPath -Raw | ConvertFrom-Json
        if ($chatResult.PSObject.Properties['error'] -or $chatResult.input -ne '' -or -not $chatResult.status) { throw 'Chat window did not enqueue cloud message successfully.' }
        WaitUntil {
            [xml]$chatPrefs = (@(& $adb -s $serial exec-out run-as $package cat shared_prefs/yanzi-mobile.xml) -join "`n")
            $historyNodes = @($chatPrefs.map.string | Where-Object {$_.name -eq 'desktop_chat_history'})
            if ($historyNodes.Count -eq 0) { return $false }
            $history = [string]$historyNodes[0].'#text'
            $history.Contains($chatText)
        } 'CHAT_WINDOW_TO_PHONE_HISTORY'
    }
    WaitUntil { (Get-Content (Join-Path $desktopRoot 'logs\host.log') -Raw) -match 'Mobile bridge realtime connected' } 'WINDOWS_WEBSOCKET_CONNECTED'
    $latencies = @()
    foreach ($i in 1..10) {
        $id = SendNotification "latency-$i"
        WaitUntil { (MessageStatus $id) -eq 'acked' } 'REALTIME_DELIVERY'
        $detail = Request "/v1/me/mobile/messages/$id"
        $latencies += ([DateTimeOffset]$detail.ackedAt - [DateTimeOffset]$detail.createdAt).TotalMilliseconds
    }
    $ordered = @($latencies | Sort-Object)
    [IO.File]::WriteAllText((Join-Path $artifact 'latency.json'), (@{sampleCount=10;medianMs=$ordered[5];p95Ms=$ordered[9];samplesMs=$latencies} | ConvertTo-Json))
    foreach ($photo in @($false,$true)) {
        $name = if ($photo) { 'pc-verification.png' } else { 'pc-verification.bin' }
        $filePath = Join-Path $artifact $name
        $bytes = if ($photo) { [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD1sAAAAASUVORK5CYII=') } else { [Text.Encoding]::UTF8.GetBytes(('attachment-content-' * 8000)) }
        [IO.File]::WriteAllBytes($filePath, $bytes)
        $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Remove-Item -LiteralPath $chatResultPath -Force -ErrorAction SilentlyContinue
        [IO.File]::WriteAllText((Join-Path $desktopRoot 'chat-request.json'), (@{text='';staleLan=$false;filePath=$filePath;isPhoto=$photo} | ConvertTo-Json -Compress))
        WaitUntil { Test-Path $chatResultPath } 'WINDOWS_ATTACHMENT_SEND' 90
        $sent = Get-Content $chatResultPath -Raw | ConvertFrom-Json
        if ($sent.PSObject.Properties['error']) { throw 'Attachment UI send failed; inspect result artifact.' }
        WaitUntil {
            $hashes = @(& $adb -s $serial shell run-as $package sh -c '"sha256sum files/mobile-attachments/* 2>/dev/null"') -join "`n"
            $hashes.Contains($hash)
        } 'PHONE_ATTACHMENT_BYTES_VERIFIED' 90
        AdbChecked @('push',$filePath,'/data/local/tmp/verification-attachment') | Out-Null
        AdbChecked @('shell','run-as',$package,'mkdir','-p','cache') | Out-Null
        AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/verification-attachment','cache/verification-attachment') | Out-Null
        $kind = if ($photo) { 'photo' } else { 'file' }
        AdbChecked @('shell','am','start','-f','0x10008000','-n',"$package/$activity",'--es','verify_message_kind',$kind,'--es','verify_message_text','attachment-roundtrip') | Out-Null
        WaitUntil {
            $paths = @(Get-ChildItem (Join-Path $desktopRoot 'mobile-attachments') -File -ErrorAction SilentlyContinue)
            @($paths | Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash }).Count -gt 0
        } 'WINDOWS_ATTACHMENT_BYTES_VERIFIED' 90
    }
    & (Join-Path $PSScriptRoot 'test-mobile-attachments.ps1') -FixturePath $fixturePath
    $command = 'Write-Output phone-roundtrip-result'
    AdbChecked @('shell','am','start','-f','0x10008000','-n',"$package/$activity",'--es','verify_message_kind','run-shell','--es','verify_message_text',('"'+$command+'"')) | Out-Null
    WaitUntil { (Get-Content (Join-Path $desktopRoot 'logs\host.log') -Raw) -match 'kind=run-shell, text=Write-Output phone-roundtrip-result' } 'PHONE_TO_WINDOWS_EXECUTION'
    WaitUntil {
        $phonePid = @(& $adb -s $serial shell pidof $package)[0].Trim()
        (@(& $adb -s $serial logcat --pid=$phonePid -d -t 500) -join "`n") -match 'Phone read desktop execution result: PASSED'
    } 'WINDOWS_RESULT_READ_BY_PHONE'
    # Inspect the local D1 queue through the real receiving phone identity, without consuming desktop messages.
    $pending = Request "/v1/me/mobile/messages?deviceId=$device"
    if (@($pending.items).Count -ne 0) { throw 'Inbox still has unprocessed messages.' }
    AdbChecked @('forward','tcp:42992','tcp:42982') | Out-Null
    [xml]$lanPrefs = (@(& $adb -s $serial exec-out run-as $package cat shared_prefs/YanziPrefs.xml) -join "`n")
    $lanToken = [string](@($lanPrefs.map.string | Where-Object { $_.name -eq 'lanApiToken' })[0].'#text')
    if ([string]::IsNullOrEmpty($lanToken)) { throw 'Mobile LAN pairing token missing.' }
    $lanBody = [Text.Encoding]::UTF8.GetBytes((@{title='lan-utf8-test';message=([string][char]0x4e2d+[char]0x6587)} | ConvertTo-Json -Compress))
    try { Invoke-WebRequest 'http://127.0.0.1:42992/' -Method POST -Body $lanBody -ContentType 'application/json' -UseBasicParsing -TimeoutSec 8 | Out-Null; throw 'Unauthenticated LAN request accepted.' }
    catch { if ($_.Exception.Response.StatusCode.value__ -ne 401) { throw } }
    Write-Host 'LAN_UNAUTHORIZED_REJECTED=PASSED'
    Invoke-WebRequest 'http://127.0.0.1:42992/' -Method POST -Headers @{Authorization="Bearer $lanToken"} -Body $lanBody -ContentType 'application/json' -UseBasicParsing -TimeoutSec 8 | Out-Null
    Write-Host 'LAN_CHINESE_MESSAGE=PASSED'
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    $restart = SendNotification 'restart-test'
    StartPhone
    WaitUntil { (MessageStatus $restart) -eq 'acked' } 'PROCESS_RESTART_PENDING_RECOVERY'
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    Write-Host 'Checking actual presence expiry (120-second server window)...'
    if ((@(& $adb -s $serial shell pidof $package) -join '').Trim()) { throw 'Phone remained running after force-stop.' }
    Write-Host 'PHONE_FORCE_STOP_CONFIRMED=PASSED'
    # Isolate the expiry phase from accidental app resumes during physical-phone interaction.
    AdbChecked @('reverse','--remove',"tcp:$Port") | Out-Null
    New-Item -ItemType File -Force (Join-Path $desktopRoot 'stop') | Out-Null
    if (-not $desktop.WaitForExit(5000)) { throw 'Isolated desktop did not stop.' }
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device -and -not $_.online}).Count -eq 1 } 'PHONE_OFFLINE_EXPIRY' 135
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $desktopDevice -and -not $_.online}).Count -eq 1 } 'DESKTOP_OFFLINE_EXPIRY' 35
    AdbChecked @('reverse',"tcp:$Port","tcp:$Port") | Out-Null
    StartPhone
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device -and $_.online}).Count -eq 1 } 'PHONE_ONLINE_RECOVERY'
    if ($IncludeFinalScreenOff) {
        try {
            AdbChecked @('shell','input','keyevent','223') | Out-Null
            $screenOff = SendNotification 'final-screen-off-test'
            WaitUntil { (MessageStatus $screenOff) -eq 'acked' } 'FINAL_SCREEN_OFF_NOTIFICATION_ACK'
        } finally { AdbChecked @('shell','input','keyevent','224') | Out-Null }
    }
    $appPid = @(& $adb -s $serial shell pidof $package)[0].Trim()
    $logs = @(& $adb -s $serial logcat --pid=$appPid -d -t 300) -join "`n"
    if ($logs -match 'FATAL EXCEPTION|ANR in') { throw 'Android crash or ANR detected.' }
    [IO.File]::WriteAllText((Join-Path $artifact 'result.txt'),'REAL_PHONE_MESSAGE_BRIDGE=PASSED')
    Write-Host 'REAL_PHONE_MESSAGE_BRIDGE=PASSED'
}
finally {
    if ($desktop -and -not $desktop.HasExited) {
        New-Item -ItemType File -Force (Join-Path $desktopRoot 'stop') | Out-Null
        if (-not $desktop.WaitForExit(5000)) { $desktop.Kill() }
    }
    if ($worker) { Stop-YanziLocalWorkerPort -Port $Port }
    & $adb -s $serial reverse --remove "tcp:$Port" 2>$null | Out-Null
    & $adb -s $serial forward --remove tcp:42992 2>$null | Out-Null
    & $adb -s $serial shell am force-stop $package | Out-Null
    if (Test-Path $backup) {
        AdbChecked @('push',$backup,'/data/local/tmp/yanzi-bridge-backup.tar') | Out-Null
        AdbChecked @('shell','run-as',$package,'tar','-xf','/data/local/tmp/yanzi-bridge-backup.tar') | Out-Null
        $restored = $true
    }
    if ($sdk -ge 33 -and -not $permissionGranted) { & $adb -s $serial shell pm revoke $package android.permission.POST_NOTIFICATIONS | Out-Null }
    & $adb -s $serial shell cmd appops set $package POST_NOTIFICATION $originalMode | Out-Null
    StartPhone
    if ((SnapshotProduction) -ne $production) { throw 'Production package changed.' }
    Write-Host 'PRODUCTION_PRESERVED=PASSED'
    Write-Host "DEV_PREFERENCES_RESTORED=$restored"
    foreach ($sensitive in @($backup, $fixturePath, $prefsFile)) {
        if ($sensitive -and [IO.Path]::GetFullPath($sensitive).StartsWith([IO.Path]::GetFullPath($artifact) + '\')) {
            Remove-Item -LiteralPath $sensitive -Force -ErrorAction SilentlyContinue
        }
    }
    & $adb -s $serial shell rm -f /data/local/tmp/yanzi-bridge-backup.tar /data/local/tmp/yanzi-mobile.xml /data/local/tmp/YanziPrefs.xml | Out-Null
    Write-Host "Artifacts: $artifact"
}

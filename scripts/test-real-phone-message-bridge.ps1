param([int]$Port = 8811, [switch]$SkipBuild, [switch]$IncludeFinalScreenOff, [switch]$VerifyAccountLan, [switch]$CapabilitiesOnly, [switch]$VerifyAlbumWorkflow, [string]$Serial = '', [switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$clipboardSnapshot = New-Object System.Windows.Forms.DataObject
$clipboardOriginal = [System.Windows.Forms.Clipboard]::GetDataObject()
if ($null -ne $clipboardOriginal) {
    foreach ($format in $clipboardOriginal.GetFormats($false)) {
        $value = $clipboardOriginal.GetData($format,$false)
        if ($null -eq $value) { throw 'Cannot preserve clipboard format before bridge test.' }
        if ($value -is [IO.MemoryStream]) { $value = New-Object IO.MemoryStream(,$value.ToArray()) }
        elseif ($value -is [ICloneable]) { $value = $value.Clone() }
        $clipboardSnapshot.SetData($format,$false,$value)
    }
}
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
. (Join-Path $PSScriptRoot 'dev-phone-chat-snapshot.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
$activity = 'cc.luoluoluo.yanzi.mobile.MainActivity'
$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' -and $_ -notmatch '^emulator-' } | ForEach-Object { ($_ -split '\s+')[0] })
if (-not $Serial) {
    if ($serials.Count -ne 1) { throw 'Multiple phones are connected; pass -Serial explicitly.' }
    $Serial = $serials[0]
}
if ($serials -notcontains $Serial) { throw 'Selected physical phone is not connected.' }
$serial = $Serial
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
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $result = & $adb -s $serial @Arguments 2>&1 }
    finally { $ErrorActionPreference = $savedPreference }
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
function WaitUntil([scriptblock]$Check, [string]$Label, [int]$Seconds = 45, [bool]$Quiet = $false) {
    $end = (Get-Date).AddSeconds($Seconds)
    do { if (& $Check) { if (-not $Quiet) { Write-Host "$Label=PASSED" }; return }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $end)
    throw "Timed out: $Label"
}
function SendNotification([string]$Label, [string]$Kind = 'notify') {
    return (Request '/v1/me/mobile/messages' 'POST' @{
        sourceDeviceId = $desktopDevice; targetDeviceId = $device; targetPlatform = 'android'
        kind = $Kind; title = $Label; text = "Chinese notification: $([char]0x624b)$([char]0x673a)$([char]0x7535)$([char]0x8111)"; payload = @{}
    }).messageId
}
function MessageStatus([string]$Id) { return (Request "/v1/me/mobile/messages/$Id").status }
function InvokePhoneCapability([string]$Name, $Arguments = @{}) {
    $id = (Request '/v1/me/mobile/messages' 'POST' @{
        sourceDeviceId=$desktopDevice;targetDeviceId=$device;targetPlatform='android';kind='capability.invoke'
        payload=@{name=$Name;arguments=$Arguments};expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(45).ToString('O')
    }).messageId
    WaitUntil { (MessageStatus $id) -in @('completed','failed') } ('MOBILE_CAPABILITY_'+$Name) 50 $true
    $message = Request "/v1/me/mobile/messages/$id"
    if ($message.status -ne 'completed') { throw ('Mobile capability failed: '+$message.payload.executionResult.output) }
    return ($message.payload.executionResult.output | ConvertFrom-Json)
}
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
    # Android prefers a leftover atomic preference backup over the injected fixture.
    AdbChecked @('shell','run-as',$package,'rm','-f',"shared_prefs/$Name.bak") | Out-Null
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
if (-not $SkipInstall) {
    AdbChecked @('install','-r',(Join-Path $repo 'mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk')) | Out-Null
} else {
    if (-not ((AdbChecked @('shell','pm','path',$package)) -match '^package:')) { throw 'Dev package is not installed.' }
}
AdbChecked @('shell','am','force-stop',$package) | Out-Null
AdbChecked @('shell','run-as',$package,'mkdir','-p','shared_prefs') | Out-Null
SaveBinaryOutput "exec-out run-as $package tar -cf - shared_prefs" $backup
$originalBaseUrl = ''
if (@(AdbChecked @('shell','run-as',$package,'ls','shared_prefs')).Contains('yanzi-mobile.xml')) {
    [xml]$originalPrefsXml = (AdbChecked @('shell','run-as',$package,'cat','shared_prefs/yanzi-mobile.xml')) -join "`n"
    $originalBaseUrl = [string](($originalPrefsXml.map.string | Where-Object { $_.name -eq 'baseUrl' } | Select-Object -First 1).InnerText)
}
$originalAppOps = @(& $adb -s $serial shell cmd appops get $package POST_NOTIFICATION) -join ''
$originalMode = if ($originalAppOps -match 'POST_NOTIFICATION:\s*(\w+)') { $Matches[1] } else { 'default' }
$permissionGranted = @(& $adb -s $serial shell dumpsys package $package | Select-String 'android.permission.POST_NOTIFICATIONS: granted=true').Count -gt 0
$sdk = [int](@(& $adb -s $serial shell getprop ro.build.version.sdk)[0])

try {
    if ($VerifyAccountLan) { Stop-Process -Name Yanzi -Force -ErrorAction SilentlyContinue }
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
    [IO.File]::WriteAllText($fixturePath, (@{root=$desktopRoot;baseUrl=$base;token=$token;userId=$user;desktopDeviceId=$desktopDevice;verifyAccountLan=[bool]$VerifyAccountLan;verifyAlbumWorkflow=[bool]$VerifyAlbumWorkflow} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $verify = Join-Path $repo 'src\Yanzi.SyncVerification\bin\Debug\net9.0-windows\Yanzi.SyncVerification.exe'
    $desktop = Start-Process $verify -ArgumentList @('--mobile-message-bridge',('"'+$fixturePath+'"')) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifact 'desktop.stdout') -RedirectStandardError (Join-Path $artifact 'desktop.stderr') -PassThru
    WaitUntil {
        if ($desktop.HasExited) { throw 'Isolated Windows bridge exited; see desktop.stderr.' }
        Test-Path (Join-Path $desktopRoot 'ready')
    } 'WINDOWS_BRIDGE_AND_DEDUP'
    & (Join-Path $PSScriptRoot 'test-device-message-protocol.ps1') -FixturePath $fixturePath
    AdbChecked @('reverse',"tcp:$Port","tcp:$Port") | Out-Null
    $prefsFile = Join-Path $artifact 'yanzi-mobile.xml'
    $xml = "<?xml version='1.0' encoding='utf-8'?><map><string name='baseUrl'>$base</string><string name='token'>$token</string><string name='deviceId'>$device</string><string name='username'>bridge-test</string><boolean name='floatingWheelEnabled' value='false'/></map>"
    [IO.File]::WriteAllText($prefsFile,$xml,[Text.UTF8Encoding]::new($false))
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    WritePhonePrefs $prefsFile 'yanzi-mobile.xml'
    $lanFile = Join-Path $artifact 'YanziPrefs.xml'
    [IO.File]::WriteAllText($lanFile,"<?xml version='1.0' encoding='utf-8'?><map><string name='lanApiToken'>bridge-lan-test</string></map>",[Text.UTF8Encoding]::new($false))
    WritePhonePrefs $lanFile 'YanziPrefs.xml'
    if ($sdk -ge 33) { AdbChecked @('shell','pm','grant',$package,'android.permission.POST_NOTIFICATIONS') | Out-Null }
    AdbChecked @('shell','cmd','appops','set',$package,'POST_NOTIFICATION','allow') | Out-Null
    StartPhone
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device -and $_.online}).Count -eq 1 } 'PHONE_CLOUD_ONLINE'
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.platform -eq 'desktop' -and $_.online}).Count -eq 1 } 'DESKTOP_CLOUD_ONLINE'
    if ($VerifyAccountLan) {
        WaitUntil {
            [xml]$pairs = (AdbChecked @('shell','run-as',$package,'cat','shared_prefs/YanziPrefs.xml')) -join "`n"
            @($pairs.map.string | Where-Object { $_.name -eq ('securePair.'+$desktopDevice) }).Count -eq 1
        } 'PHONE_SAME_ACCOUNT_AUTO_CONNECTED' 75
        WaitUntil { @(Get-ChildItem (Join-Path $desktopRoot 'device-network/pairs') -Filter '*.json' -ErrorAction SilentlyContinue).Count -gt 0 } 'DESKTOP_SAME_ACCOUNT_AUTO_CONNECTED' 75
        WaitUntil {
            [xml]$pairs = (AdbChecked @('shell','run-as',$package,'cat','shared_prefs/YanziPrefs.xml')) -join "`n"
            [string](@($pairs.map.string | Where-Object { $_.name -eq 'lanBaseUrl' })[0].InnerText) -match ':42994$'
        } 'SAME_ACCOUNT_AUTHENTICATED_LAN_DISCOVERY' 75
    }
    $first = SendNotification 'foreground-test'
    WaitUntil { (MessageStatus $first) -eq 'acked' } 'FOREGROUND_NOTIFICATION_ACK'
    $notificationDump = @(& $adb -s $serial shell dumpsys notification --noredact) -join "`n"
    if ($notificationDump -notmatch [regex]::Escape($first)) { throw 'ACK received without a real Android notification.' }
    Write-Host 'ANDROID_NOTIFICATION_RECORD=PASSED'
    AdbChecked @('shell','input','keyevent','3') | Out-Null
    $background = SendNotification 'background-test'
    WaitUntil { (MessageStatus $background) -eq 'acked' } 'BACKGROUND_NOTIFICATION_ACK'
    $seenBefore = @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device})[0].lastSeenAt
    if ($VerifyAlbumWorkflow) {
        $albumRun = Start-Job -ScriptBlock {
            Invoke-RestMethod 'http://127.0.0.1:42994/v1/extensions/yanzi-album/run' -Headers @{Authorization='Bearer account-lan-verification'} -Method POST -ContentType 'application/json' -Body '{"input":""}' -TimeoutSec 240
        }
        WaitUntil {
            $available = Invoke-RestMethod 'http://127.0.0.1:42994/v1/capabilities' -Headers @{Authorization='Bearer account-lan-verification'} -TimeoutSec 5
            @($available.capabilities | Where-Object name -eq 'album.process').Count -eq 1
        } 'ALBUM_DESKTOP_REAL_HANDLER_READY' 75
        $albumAndroid = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\yanzi-album\android'
        AdbChecked @('install','-r',(Join-Path $albumAndroid 'build\outputs\apk\dev\album-dev.apk')) | Out-Null
        AdbChecked @('install','-r',(Join-Path $albumAndroid 'build\outputs\apk\androidTest\dev\album-dev-androidTest.apk')) | Out-Null
        if (Test-Path (Join-Path $albumAndroid 'src\androidTest\java\cc\luoluoluo\yanzi\album\AlbumUiTest.java')) {
            $uiTest=AdbChecked @('shell','am','instrument','-w','-e','class','cc.luoluoluo.yanzi.album.AlbumUiTest','cc.luoluoluo.yanzi.album.dev.test/android.test.InstrumentationTestRunner')
            $uiTest | Set-Content -LiteralPath (Join-Path $artifact 'album-ui-test.log') -Encoding UTF8
            if (($uiTest -join "`n") -notmatch 'OK \(1 test\)') { throw 'Album UI interactions failed; see album-ui-test.log.' }
            foreach ($image in @('album-dark-gallery.png','album-dark-send.png','album-dark-tasks.png')) {
                AdbChecked @('pull',('/sdcard/Android/data/cc.luoluoluo.yanzi.album.dev/files/'+$image),(Join-Path $artifact $image)) | Out-Null
            }
            Write-Host 'ALBUM_DARK_UI_SELECTION_NAVIGATION_AND_SEND_DIALOG=PASSED'
        }
        $instrumentation = AdbChecked @('shell','am','instrument','-w','-e','class','cc.luoluoluo.yanzi.album.AlbumPipelineTest','cc.luoluoluo.yanzi.album.dev.test/android.test.InstrumentationTestRunner')
        $instrumentation | Set-Content -LiteralPath (Join-Path $artifact 'album-test.log') -Encoding UTF8
        if (($instrumentation -join "`n") -notmatch 'OK \(2 tests\)' -or ($instrumentation -join "`n") -match 'FAILURES|INSTRUMENTATION_FAILED') { throw 'Album real image round trip failed; see album-test.log.' }
        AdbChecked @('pull','/sdcard/Android/data/cc.luoluoluo.yanzi.album.dev/files/album-pipeline.png',(Join-Path $artifact 'album-pipeline.png')) | Out-Null
        AdbChecked @('shell','am','force-stop','cc.luoluoluo.yanzi.album.dev') | Out-Null
        $cleanup=AdbChecked @('shell','am','instrument','-w','-e','class','cc.luoluoluo.yanzi.album.AlbumCleanupTest','cc.luoluoluo.yanzi.album.dev.test/android.test.InstrumentationTestRunner')
        if(($cleanup -join "`n") -notmatch 'OK \(1 test\)'){throw 'Isolated album result cleanup failed.'}
        Write-Host 'ALBUM_REAL_IMAGE_LAN_CLOUD_WORKFLOW_AND_SYSTEM_GALLERY=PASSED'
        $largeTicket=Get-ChildItem (Join-Path $desktopRoot 'companion-files') -Recurse -Filter '*.bin' | Where-Object Length -gt (2*1048576) | Select-Object -First 1
        if (-not $largeTicket) { throw 'Album did not test a large image.' }
        $ticketId=[IO.Path]::GetFileNameWithoutExtension($largeTicket.Name)
        $tail=Join-Path $artifact 'album-tail.bin'
        Invoke-WebRequest ('http://127.0.0.1:42994/v1/companion/files/'+$ticketId+'?offset=1048576') -Headers @{Authorization='Bearer account-lan-verification'} -OutFile $tail -UseBasicParsing -TimeoutSec 15
        $originalBytes=[IO.File]::ReadAllBytes($largeTicket.FullName);$tailBytes=[IO.File]::ReadAllBytes($tail)
        $sha=[Security.Cryptography.SHA256]::Create()
        if ($tailBytes.Length -ne ($originalBytes.Length-1048576) -or [Convert]::ToBase64String($sha.ComputeHash($tailBytes)) -ne [Convert]::ToBase64String($sha.ComputeHash($originalBytes,1048576,$originalBytes.Length-1048576))) { throw 'Album resume offset differs.' }
        $sha.Dispose()
        $badTicket=[Guid]::NewGuid().ToString('N');$denied=$false
        try { Invoke-RestMethod ('http://127.0.0.1:42994/v1/companion/files/'+$badTicket) -Method PUT -Headers @{Authorization='Bearer account-lan-verification';'X-Content-Sha256'=('0'*64)} -Body ([byte[]](1,2,3)) -ContentType 'application/octet-stream' -TimeoutSec 10 | Out-Null }
        catch { $denied=$_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 400 }
        if (-not $denied) { throw 'Companion accepted invalid checksum.' }
        Write-Host 'ALBUM_LARGE_IMAGE_CHUNKS_OFFSET_AND_CHECKSUM_REJECTION=PASSED'
    }
    WaitUntil { @((Request '/v1/me/devices').items | Where-Object {$_.deviceId -eq $device})[0].lastSeenAt -ne $seenBefore } 'BACKGROUND_HEARTBEAT_ADVANCED'
    $state = InvokePhoneCapability 'mobile.status.get'
    if ($state.deviceId -ne $device -or $state.revision -lt 1 -or $state.value.battery.percent -lt 0 -or -not $state.collectedAt) { throw 'Real phone state snapshot is incomplete.' }
    $catalog = InvokePhoneCapability 'mobile.capabilities.list'
    if (@($catalog.items).Count -ne 7) { throw 'Mobile capability catalog incomplete.' }
    InvokePhoneCapability 'mobile.files.list' | Out-Null
    InvokePhoneCapability 'mobile.extensions.list' | Out-Null
    $cataloguedPhone = @((Request '/v1/me/devices').items | Where-Object deviceId -eq $device)[0]
    if ($cataloguedPhone.capabilities.stateSnapshot.deviceId -ne $device -or @($cataloguedPhone.capabilities.capabilityCatalog).Count -ne 7) { throw 'Presence did not publish state and capabilities.' }
    if ($VerifyAccountLan) {
        $agentHeaders = @{Authorization='Bearer account-lan-verification'}
        $agentBase = 'http://127.0.0.1:42994/v1/me/devices/' + $device
        $agentDevices = Invoke-RestMethod 'http://127.0.0.1:42994/v1/me/devices' -Headers $agentHeaders -TimeoutSec 15
        if (@($agentDevices.items | Where-Object deviceId -eq $device).Count -ne 1) { throw 'Agent device discovery failed.' }
        $agentState = Invoke-RestMethod ($agentBase+'/state') -Headers $agentHeaders -TimeoutSec 15
        if ($agentState.snapshot.deviceId -ne $device) { throw 'Agent API state snapshot mismatch.' }
        $agentCatalog = Invoke-RestMethod ($agentBase+'/capabilities') -Headers $agentHeaders -TimeoutSec 15
        if (@($agentCatalog.items).Count -ne 7) { throw 'Agent API capability discovery failed.' }
        $agentResult = Invoke-RestMethod ($agentBase+'/invoke') -Method POST -Headers $agentHeaders -ContentType 'application/json' -Body '{"name":"mobile.status.get","arguments":{}}' -TimeoutSec 35
        if ($agentResult.source -ne 'lan' -or -not $agentResult.data.ok -or $agentResult.data.result.deviceId -ne $device) { throw 'Agent API did not invoke the real phone over LAN.' }
        Write-Host 'AGENT_API_DISCOVERY_AND_LAN_INVOCATION=PASSED'
        # Change only the isolated harness registry, leaving the real account's LAN links intact.
        $peerFile = Get-ChildItem (Join-Path $desktopRoot 'device-network') -Filter 'peers-*.json' | Select-Object -First 1
        $peerBackup = [IO.File]::ReadAllText($peerFile.FullName)
        try {
            [IO.File]::WriteAllText($peerFile.FullName,'{}',[Text.Encoding]::UTF8)
            $cloudResult = Invoke-RestMethod ($agentBase+'/invoke') -Method POST -Headers $agentHeaders -ContentType 'application/json' -Body '{"name":"mobile.status.get","arguments":{}}' -TimeoutSec 35
            if ($cloudResult.source -ne 'cloud' -or $cloudResult.status -ne 'completed' -or ($cloudResult.result.output | ConvertFrom-Json).deviceId -ne $device) { throw 'Agent cloud fallback failed.' }
            $receipt = Invoke-RestMethod ($agentBase+'/results/'+$cloudResult.messageId) -Headers $agentHeaders -TimeoutSec 15
            if ($receipt.status -ne 'completed' -or $receipt.targetDeviceId -ne $device) { throw 'Agent cloud result recovery failed.' }
            Write-Host 'AGENT_API_CLOUD_FALLBACK_AND_RESULT_RECOVERY=PASSED'
        } finally { [IO.File]::WriteAllText($peerFile.FullName,$peerBackup,[Text.Encoding]::UTF8) }
    }
    $capabilityFilePath = '/sdcard/Android/data/' + $package + '/files/Documents/capability-' + [Guid]::NewGuid().ToString('N') + '.txt'
    $capabilityFile = Join-Path $artifact 'capability-sample.txt'
    [IO.File]::WriteAllText($capabilityFile,'real-phone-file-capability',[Text.Encoding]::ASCII)
    AdbChecked @('shell','mkdir','-p',('/sdcard/Android/data/'+$package+'/files/Documents')) | Out-Null
    AdbChecked @('push',$capabilityFile,$capabilityFilePath) | Out-Null
    $fileResult = InvokePhoneCapability 'mobile.files.read' @{name=($capabilityFilePath -split '/')[-1]}
    if ([Text.Encoding]::ASCII.GetString([Convert]::FromBase64String($fileResult.content)) -ne 'real-phone-file-capability') { throw 'Mobile file capability bytes differ.' }
    Write-Host 'MOBILE_FILE_CAPABILITY_BYTES=PASSED'
    $syncObjectId = 'extensionData.v1.' + ('a' * 64)
    $syncWrite = Request ('/v1/sync/objects/'+$syncObjectId) 'PUT' @{schemaVersion=1;expectedRevision=0;deviceId=$desktopDevice;deviceName='Fixture desktop';deleted=$false;payload=@{extensionId='sync-fixture';key='sample';content='first revision'}}
    WaitUntil {
        $result = InvokePhoneCapability 'mobile.data.read' @{objectId=$syncObjectId}
        $result.exists -and $result.object.revision -eq $syncWrite.object.revision -and $result.object.payload.content -eq 'first revision'
    } 'BACKGROUND_INCREMENTAL_OBJECT_SYNC' 90
    $beforeSync = InvokePhoneCapability 'mobile.sync.status'
    Start-Sleep -Seconds 3
    WaitUntil {
        $current = InvokePhoneCapability 'mobile.sync.status'
        if ($current.lastChangedAt -ne $beforeSync.lastChangedAt) { throw 'Unchanged polling incorrectly reported changed data.' }
        $current.lastCheckedAt -ne $beforeSync.lastCheckedAt -and $current.revision -eq $beforeSync.revision
    } 'PERIODIC_SYNC_WITHOUT_REWRITING_UNCHANGED_DATA' 90
    Request ('/v1/sync/objects/'+$syncObjectId) 'PUT' @{schemaVersion=1;expectedRevision=$syncWrite.object.revision;deviceId=$desktopDevice;deviceName='Fixture desktop';deleted=$true;payload=@{}} | Out-Null
    WaitUntil {
        $result = InvokePhoneCapability 'mobile.data.read' @{objectId=$syncObjectId}
        -not $result.exists -and $result.object.deleted
    } 'SYNC_TOMBSTONE_PROPAGATION' 90
    $bad = (Request '/v1/me/mobile/messages' 'POST' @{sourceDeviceId=$desktopDevice;targetDeviceId=$device;kind='capability.invoke';payload=@{name='mobile.files.read';arguments=@{name='../private.xml'}}}).messageId
    WaitUntil { (MessageStatus $bad) -eq 'failed' } 'MOBILE_FILE_SCOPE_TRAVERSAL_DENIED'
    if ($CapabilitiesOnly) { Write-Host 'REAL_PHONE_ENDPOINT_SYNC=PASSED'; return }
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
            Test-YanziPhoneChatMessage $adb $serial $package $chatText $artifact
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
    [IO.File]::WriteAllText((Join-Path $artifact 'latency.json'), (@{sampleCount=10;medianMs=(($ordered[4]+$ordered[5])/2);p95Ms=$ordered[9];samplesMs=$latencies} | ConvertTo-Json))
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
            $names = @(AdbChecked @('shell','run-as',$package,'ls','files/mobile-attachments'))
            $hashes = @($names | Where-Object { $_ -match '^[a-zA-Z0-9_.-]+$' -and $_.EndsWith($name) } | ForEach-Object {
                AdbChecked @('shell','run-as',$package,'sha256sum',('files/mobile-attachments/'+$_))
            }) -join "`n"
            $hashes.Contains($hash)
        } 'PHONE_ATTACHMENT_BYTES_VERIFIED' 90
        AdbChecked @('push',$filePath,'/data/local/tmp/verification-attachment') | Out-Null
        AdbChecked @('shell','run-as',$package,'mkdir','-p','cache') | Out-Null
        AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/verification-attachment','cache/verification-attachment') | Out-Null
        $kind = if ($photo) { 'photo' } else { 'file' }
        AdbChecked @('shell','am','start','-f','0x10008000','-n',"$package/$activity",'--es','verify_message_kind',$kind,'--es','verify_message_text','attachment-roundtrip') | Out-Null
        WaitUntil {
            $paths = @(Get-ChildItem (Join-Path $desktopRoot 'mobile-attachments') -File -Recurse -ErrorAction SilentlyContinue)
            @($paths | Where-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash }).Count -gt 0
        } 'WINDOWS_ATTACHMENT_BYTES_VERIFIED' 90
    }
    & (Join-Path $PSScriptRoot 'test-mobile-attachments.ps1') -FixturePath $fixturePath
    $command = 'Write-Output phone-roundtrip-result'
    AdbChecked @('shell',"am start -f 0x10008000 -n $package/$activity --es verify_message_kind run-shell --es verify_message_text '$command'") | Out-Null
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
    $lanToken = [string](@($lanPrefs.map.string | Where-Object { $_.name -eq 'lanApiToken' })[0].InnerText)
    if ([string]::IsNullOrEmpty($lanToken)) { $lanToken = [string](@($lanPrefs.map.string | Where-Object { $_.name -eq 'nativeLocalToken' })[0].InnerText) }
    if ([string]::IsNullOrEmpty($lanToken)) { throw 'Mobile native localhost token missing.' }
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
    if ($capabilityFilePath) { & $adb -s $serial shell rm -f $capabilityFilePath | Out-Null }
    if ($null -ne $clipboardOriginal) { [System.Windows.Forms.Clipboard]::SetDataObject($clipboardSnapshot,$true) }
    else { [System.Windows.Forms.Clipboard]::Clear() }
    if ($desktop -and -not $desktop.HasExited) {
        New-Item -ItemType File -Force (Join-Path $desktopRoot 'stop') | Out-Null
        if (-not $desktop.WaitForExit(5000)) { $desktop.Kill() }
    }
    if ($worker) { Stop-YanziLocalWorkerPort -Port $Port }
    $cleanupPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $adb -s $serial reverse --remove "tcp:$Port" 2>$null | Out-Null
        & $adb -s $serial forward --remove tcp:42992 2>$null | Out-Null
    } finally { $ErrorActionPreference = $cleanupPreference }
    & $adb -s $serial shell am force-stop $package | Out-Null
    if (Test-Path $backup) {
        # Android prefers .bak over .xml; test-era backups must never replace the restored settings.
        AdbChecked @('shell','run-as',$package,'find','shared_prefs','-name','*.bak','-delete') | Out-Null
        AdbChecked @('push',$backup,'/data/local/tmp/yanzi-bridge-backup.tar') | Out-Null
        AdbChecked @('shell','run-as',$package,'tar','-xf','/data/local/tmp/yanzi-bridge-backup.tar') | Out-Null
        AdbChecked @('shell','run-as',$package,'find','shared_prefs','-name','*.bak','-delete') | Out-Null
        $restored = $true
    }
    if ($sdk -ge 33 -and -not $permissionGranted) { & $adb -s $serial shell pm revoke $package android.permission.POST_NOTIFICATIONS | Out-Null }
    & $adb -s $serial shell cmd appops set $package POST_NOTIFICATION $originalMode | Out-Null
    StartPhone
    if ($restored -and $originalBaseUrl) {
        [xml]$restoredPrefsXml = (AdbChecked @('shell','run-as',$package,'cat','shared_prefs/yanzi-mobile.xml')) -join "`n"
        $restoredBaseUrl = [string](($restoredPrefsXml.map.string | Where-Object { $_.name -eq 'baseUrl' } | Select-Object -First 1).InnerText)
        if ($restoredBaseUrl -ne $originalBaseUrl) { throw 'DEV server address changed after preference restoration.' }
        Write-Host 'DEV_SERVER_ADDRESS_RESTORED=PASSED'
    }
    if ((SnapshotProduction) -ne $production) { throw 'Production package changed.' }
    Write-Host 'PRODUCTION_PRESERVED=PASSED'
    Write-Host "DEV_PREFERENCES_RESTORED=$restored"
    foreach ($sensitive in @($backup, $fixturePath, $prefsFile)) {
        if ($sensitive -and [IO.Path]::GetFullPath($sensitive).StartsWith([IO.Path]::GetFullPath($artifact) + '\')) {
            Remove-Item -LiteralPath $sensitive -Force -ErrorAction SilentlyContinue
        }
    }
    & $adb -s $serial shell rm -f /data/local/tmp/yanzi-bridge-backup.tar /data/local/tmp/yanzi-mobile.xml /data/local/tmp/YanziPrefs.xml | Out-Null
    if ($VerifyAccountLan) { & (Join-Path $PSScriptRoot 'dev-desktop-loop.ps1') -SkipBuild }
    Write-Host "Artifacts: $artifact"
}

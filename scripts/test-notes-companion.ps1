param([int]$Port = 8812, [string]$Serial = "emulator-5554")
$ErrorActionPreference = "Stop"
if ($Serial -notmatch '^emulator-\d+$') { throw "Notes integration tests require an emulator." }
. (Join-Path $PSScriptRoot "dev-test-worker.ps1")
$repo = Split-Path -Parent $PSScriptRoot
$adb = "F:\SDK\platform-tools\adb.exe"
$hostPackage = "cc.luoluoluo.yanzi.mobile.dev"
$notesPackage = "cc.luoluoluo.yanzi.notes.dev"
$config = Join-Path $repo 'cloudflare\wrangler.toml'
$artifact = Join-Path $env:TEMP ('YanziNotesTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($artifact) | Out-Null
function Base64Url([byte[]]$bytes) { return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
$secret = 'notes-test-' + [Guid]::NewGuid().ToString('N')
$user = 'notes-test-' + [Guid]::NewGuid().ToString('N')
$phoneDevice = 'notes-phone-' + [Guid]::NewGuid().ToString('N')
$desktopDevice = 'notes-desktop-' + [Guid]::NewGuid().ToString('N')
$header = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$claims = @{sub=$user;username='notes-test';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+1800}|ConvertTo-Json -Compress
$payload = Base64Url ([Text.Encoding]::UTF8.GetBytes($claims))
$unsigned = "$header.$payload"
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$token = $unsigned + '.' + (Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))))
$base = "http://127.0.0.1:$Port"
function Bridge([string]$operation, [string]$content = '', [long]$revision = 0) {
    $root = Join-Path $artifact ([Guid]::NewGuid().ToString('N'))
    $argv = @('run','--project',(Join-Path $repo 'src\Yanzi.SyncVerification'),'--no-build','--',
        '--account-extension-bridge','--operation',$operation,'--root',$root,'--base-url',$base,
        '--token',$token,'--user-id',$user,'--extension-id','yanzi-notes','--key','notes/sync.v1.json')
    if ($operation -eq 'write') {
        $contentFile = Join-Path $artifact ([Guid]::NewGuid().ToString('N') + '.json')
        [IO.File]::WriteAllText($contentFile,$content,[Text.UTF8Encoding]::new($false))
        $argv += @('--content-file',$contentFile,'--expected-revision',"$revision")
    }
    $output = @(& dotnet @argv)
    if ($LASTEXITCODE -ne 0) { throw "Windows notes bridge failed." }
    $line = $output | Where-Object {$_ -like 'RESULT_JSON:*'} | Select-Object -Last 1
    if (!$line) { throw 'No Windows bridge result.' }
    return $line.Substring(12) | ConvertFrom-Json
}
foreach ($pair in @(
    @('app\build\outputs\apk\dev\app-dev.apk',$hostPackage),
    @('notes\build\outputs\apk\dev\notes-dev.apk',$notesPackage),
    @('notes\build\outputs\apk\androidTest\dev\notes-dev-androidTest.apk',"$notesPackage.test"))) {
    $apk = if ($pair[0].StartsWith('notes\')) { Join-Path $env:LOCALAPPDATA ('OpenQuickHost\Extensions\yanzi-notes\android\'+$pair[0].Substring(6)) } else { Join-Path $repo ('mobile\android\'+$pair[0]) }
    if (!(Test-Path $apk)) { throw "Missing APK: $apk" }
    & $adb -s $Serial install -r $apk | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Emulator install failed.' }
}
& $adb -s $Serial shell pm clear $hostPackage | Out-Null
& $adb -s $Serial shell pm clear $notesPackage | Out-Null
Stop-YanziLocalWorkerPort -Port $Port
Push-Location $repo
try {
    & npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $config | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Local migrations failed.' }
} finally { Pop-Location }
$worker = Start-Process npx.cmd -ArgumentList @('wrangler','dev','--local','--ip','0.0.0.0','--port',$Port,
    '--config',$config,'--var',"AUTH_TOKEN_SECRET:$secret",'--var','SYNC_OBJECTS_AUTHORITATIVE:true') `
    -WorkingDirectory $repo -WindowStyle Hidden -PassThru
try {
    $ready=$false
    for ($i=0;$i -lt 40;$i++) {
        try { if ((Invoke-RestMethod "$base/health" -TimeoutSec 1).ok) {$ready=$true;break} }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (!$ready) { throw 'Local Worker unavailable.' }
    $doc = @{schemaVersion=1;records=@{'article/desktop-seed.md'=@{version='desktop-initial';deleted=$false;item=@{content='desktop seed';updatedAt='2026-10-03T00:00:00Z'}}}}|ConvertTo-Json -Depth 8 -Compress
    $written=Bridge 'write' $doc
    if ($written.revision -ne 1) { throw 'Desktop fixture was not created.' }
    $xml = '<map><string name="baseUrl">http://10.0.2.2:'+ $Port +'</string><string name="token">'+
        [Security.SecurityElement]::Escape($token)+'</string><string name="deviceId">'+$phoneDevice+'</string></map>'
    $prefs = Join-Path $artifact 'prefs.xml'
    [IO.File]::WriteAllText($prefs,$xml,[Text.UTF8Encoding]::new($false))
    & $adb -s $Serial push $prefs /data/local/tmp/notes-test-prefs.xml | Out-Null
    & $adb -s $Serial shell chmod 644 /data/local/tmp/notes-test-prefs.xml | Out-Null
    & $adb -s $Serial shell run-as $hostPackage mkdir -p shared_prefs | Out-Null
    & $adb -s $Serial shell run-as $hostPackage cp /data/local/tmp/notes-test-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null
    # A shell UID without the signature permission must never enter the bridge.
    $denialPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $denied = @(& $adb -s $Serial shell content call --uri "content://$hostPackage.extension-storage" --method read --arg yanzi-notes 2>&1) -join "`n"
    } finally { $ErrorActionPreference = $denialPreference }
    if ($denied -notmatch 'Permission Denial|SecurityException|requires') { throw 'Unsigned caller was not denied.' }
    $cloudHeaders=@{Authorization="Bearer $token"}
    Invoke-RestMethod "$base/v1/me/devices" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body (@{deviceId=$desktopDevice;platform='desktop';displayName='Notes fixture';capabilities=@{}}|ConvertTo-Json -Compress) | Out-Null
    Invoke-RestMethod "$base/v1/me/devices" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body (@{deviceId=$phoneDevice;platform='android';displayName='Notes phone fixture';capabilities=@{}}|ConvertTo-Json -Compress) | Out-Null
    $result = @(& $adb -s $Serial shell am instrument -w -r -e accountId $user -e targetDeviceId $desktopDevice -e class 'cc.luoluoluo.yanzi.notes.HostRoundtripTest,cc.luoluoluo.yanzi.notes.NotesSyncTest' `
        "$notesPackage.test/android.test.InstrumentationTestRunner") -join "`n"
    [IO.File]::WriteAllText((Join-Path $artifact 'instrumentation.txt'),$result,[Text.UTF8Encoding]::new($false))
    if ($result -notmatch 'OK \(5 tests\)') { throw "Notes instrumentation failed: $result" }
    $capture=[Diagnostics.ProcessStartInfo]::new($adb,"-s $Serial exec-out run-as $notesPackage cat files/notes-editor-qa.png")
    $capture.UseShellExecute=$false;$capture.CreateNoWindow=$true;$capture.RedirectStandardOutput=$true
    $captureProcess=[Diagnostics.Process]::Start($capture)
    $captureFile=[IO.File]::Create((Join-Path $artifact 'notes-editor-qa.png'))
    try {$captureProcess.StandardOutput.BaseStream.CopyTo($captureFile)} finally {$captureFile.Dispose()}
    $captureProcess.WaitForExit()
    if($captureProcess.ExitCode -ne 0 -or (Get-Item (Join-Path $artifact 'notes-editor-qa.png')).Length -lt 1000){throw 'Notes editor screenshot extraction failed'}
    $homeCapture=[Diagnostics.ProcessStartInfo]::new($adb,"-s $Serial exec-out run-as $notesPackage cat files/notes-dark-home-qa.png")
    $homeCapture.UseShellExecute=$false;$homeCapture.CreateNoWindow=$true;$homeCapture.RedirectStandardOutput=$true
    $homeProcess=[Diagnostics.Process]::Start($homeCapture)
    $homeFile=[IO.File]::Create((Join-Path $artifact 'notes-dark-home-qa.png'))
    try {$homeProcess.StandardOutput.BaseStream.CopyTo($homeFile)} finally {$homeFile.Dispose()}
    $homeProcess.WaitForExit()
    if($homeProcess.ExitCode -ne 0 -or (Get-Item (Join-Path $artifact 'notes-dark-home-qa.png')).Length -lt 1000){throw 'Dark home screenshot extraction failed'}
    $detailCapture=[Diagnostics.ProcessStartInfo]::new($adb,"-s $Serial exec-out run-as $notesPackage cat files/notes-detail-long-qa.png")
    $detailCapture.UseShellExecute=$false
    $detailCapture.RedirectStandardOutput=$true
    $detailProcess=[Diagnostics.Process]::Start($detailCapture)
    $detailFile=[IO.File]::Create((Join-Path $artifact 'notes-detail-long-qa.png'))
    try {$detailProcess.StandardOutput.BaseStream.CopyTo($detailFile)} finally {$detailFile.Dispose()}
    $detailProcess.WaitForExit()
    if($detailProcess.ExitCode -ne 0 -or (Get-Item (Join-Path $artifact 'notes-detail-long-qa.png')).Length -lt 1000){throw 'Full screen detail screenshot extraction failed'}
    $returned=Bridge 'read'
    $returnedDoc=$returned.content|ConvertFrom-Json
    if ($returnedDoc.records.'article/mobile-return.md'.item.content -ne 'mobile return 中文' -or
        !$returnedDoc.records.'article/desktop-seed.md'.deleted) { throw 'Windows did not receive mobile changes and tombstone.' }
    # With no Activity and a killed process, the persisted system job must pull new desktop data.
    $returnedDoc.records | Add-Member -NotePropertyName 'article/background-seed.md' -NotePropertyValue @{
        version='background-desktop';deleted=$false;item=@{content='background download';updatedAt='2026-10-03T01:00:00Z'}
    }
    $backgroundWrite=Bridge 'write' ($returnedDoc|ConvertTo-Json -Depth 12 -Compress) $returned.revision
    & $adb -s $Serial shell am kill $notesPackage | Out-Null
    # The Yanzi host receives a durable metadata hint and explicitly wakes the signed companion.
    & $adb -s $Serial shell am start -n "$hostPackage/cc.luoluoluo.yanzi.mobile.MainActivity" | Out-Null
    $cloudHeaders=@{Authorization="Bearer $token"}
    Invoke-RestMethod "$base/v1/me/devices" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body (@{deviceId=$desktopDevice;platform='desktop';displayName='Notes fixture';capabilities=@{}}|ConvertTo-Json -Compress) | Out-Null
    Invoke-RestMethod "$base/v1/me/devices" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body (@{deviceId=$phoneDevice;platform='android';displayName='Notes phone fixture';capabilities=@{}}|ConvertTo-Json -Compress) | Out-Null
    $hint=@{sourceDeviceId=$desktopDevice;targetDeviceId=$phoneDevice;kind='extension-storage.changed';clientMessageId=[Guid]::NewGuid().ToString('N');payload=@{extensionId='yanzi-notes';key='notes/sync.v1.json';revision=$backgroundWrite.revision}}|ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod "$base/v1/me/mobile/messages" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body $hint | Out-Null
    $downloaded=$false
    for($i=0;$i -lt 30;$i++) {
        $cache=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/notes.xml) -join "`n"
        if($cache.Contains('background download')) {$downloaded=$true;break}
        Start-Sleep -Milliseconds 500
    }
    if(!$downloaded) {throw 'Job did not download desktop note without Activity'}
    Start-Sleep -Seconds 3
    $before=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/sync-status.xml) -join "`n"
    Start-Sleep -Seconds 12
    $after=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/sync-status.xml) -join "`n"
    if($before -ne $after) {throw 'Idle notes still performed periodic sync'}
    $second=Bridge 'read';$secondDoc=$second.content|ConvertFrom-Json
    $secondDoc.records | Add-Member -NotePropertyName 'article/event-second.md' -NotePropertyValue @{version='event-second';deleted=$false;item=@{content='second event received';updatedAt='2026-10-03T02:00:00Z'}}
    $secondWrite=Bridge 'write' ($secondDoc|ConvertTo-Json -Depth 12 -Compress) $second.revision
    $nextHint=@{sourceDeviceId=$desktopDevice;targetDeviceId=$phoneDevice;kind='extension-storage.changed';clientMessageId=[Guid]::NewGuid().ToString('N');payload=@{extensionId='yanzi-notes';key='notes/sync.v1.json';revision=$secondWrite.revision}}|ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod "$base/v1/me/mobile/messages" -Method Post -Headers $cloudHeaders -ContentType 'application/json' -Body $nextHint | Out-Null
    $secondArrived=$false
    for($i=0;$i -lt 60;$i++) {
        $cache=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/notes.xml) -join "`n"
        if($cache.Contains('second event received')) {$secondArrived=$true;break}
        Start-Sleep -Milliseconds 500
    }
    if(!$secondArrived) {throw 'A new change hint did not wake idle companion'}
    Start-Sleep -Seconds 1
    $beforeSpoof=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/sync-status.xml) -join "`n"
    & $adb -s $Serial shell am broadcast -a "$hostPackage.EXTENSION_STORAGE_CHANGED" -n "$notesPackage/cc.luoluoluo.yanzi.notes.NotesStorageChangedReceiver" --es accountId $user --es extensionId yanzi-notes --es key notes/sync.v1.json --el revision 999999 | Out-Null
    Start-Sleep -Seconds 3
    $afterSpoof=@(& $adb -s $Serial shell run-as $notesPackage cat shared_prefs/sync-status.xml) -join "`n"
    # Android may silently skip a permission-protected receiver rather than return a shell error.
    if($beforeSpoof -ne $afterSpoof) {throw 'Unsigned storage hint woke companion'}
    # Leave a local outbox item, kill again, then verify JobService uploads it.
    $prepare=@(& $adb -s $Serial shell am instrument -w -r -e class 'cc.luoluoluo.yanzi.notes.BackgroundOutboxTest' "$notesPackage.test/android.test.InstrumentationTestRunner") -join "`n"
    if($prepare -notmatch 'OK \(1 test\)') {throw "Background outbox preparation failed: $prepare"}
    & $adb -s $Serial shell am kill $notesPackage | Out-Null
    $uploaded=$false
    for($i=0;$i -lt 30;$i++) {
        $received=Bridge 'read';$receivedDoc=$received.content|ConvertFrom-Json
        if($receivedDoc.records.'article/background-upload.md'.item.content -eq 'background upload') {$uploaded=$true;break}
        Start-Sleep -Milliseconds 500
    }
    if(!$uploaded) {throw 'Job did not upload durable outbox without Activity'}
    $outgoing=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$desktopDevice&limit=100" -Headers $cloudHeaders
    $signals=@($outgoing.items|Where-Object {$_.kind -eq 'extension-storage.changed' -and $_.payload.extensionId -eq 'yanzi-notes' -and $_.payload.key -eq 'notes/sync.v1.json'})
    if($signals.Count -lt 1 -or ($signals|Where-Object {$_.payload.content})) {throw 'Phone write did not send metadata-only change hint'}
    Write-Host 'Notes companion PASSED: dark UI/search/category/create/drafts, CAS/conflicts, metadata hint wakes killed companion, idle without periodic sync, durable outbox upload.'
    Write-Host "Artifacts: $artifact"
} finally {
    Stop-YanziLocalWorkerPort -Port $Port
    & $adb -s $Serial shell am force-stop $hostPackage | Out-Null
    & $adb -s $Serial shell am force-stop $notesPackage | Out-Null
    & $adb -s $Serial shell pm clear $hostPackage | Out-Null
    & $adb -s $Serial shell pm clear $notesPackage | Out-Null
    & $adb -s $Serial shell rm -f /data/local/tmp/notes-test-prefs.xml | Out-Null
    Remove-Item -LiteralPath (Join-Path $artifact 'prefs.xml') -Force -ErrorAction SilentlyContinue
}

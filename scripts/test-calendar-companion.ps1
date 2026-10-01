param([int]$Port = 8812, [string]$Serial = "emulator-5554")
$ErrorActionPreference = "Stop"
if ($Serial -notmatch '^emulator-\d+$') { throw "Calendar integration tests require an emulator." }
. (Join-Path $PSScriptRoot "dev-test-worker.ps1")
$repo = Split-Path -Parent $PSScriptRoot
$adb = "F:\SDK\platform-tools\adb.exe"
$hostPackage = "cc.luoluoluo.yanzi.mobile.dev"
$calendarPackage = "cc.luoluoluo.yanzi.calendar.dev"
$config = Join-Path $repo 'cloudflare\wrangler.toml'
$artifact = Join-Path $env:TEMP ('YanziCalendarTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($artifact) | Out-Null
function Base64Url([byte[]]$bytes) { return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_') }
$secret = 'calendar-test-' + [Guid]::NewGuid().ToString('N')
$user = 'calendar-test-' + [Guid]::NewGuid().ToString('N')
$header = Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$claims = @{sub=$user;username='calendar-test';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+1800}|ConvertTo-Json -Compress
$payload = Base64Url ([Text.Encoding]::UTF8.GetBytes($claims))
$unsigned = "$header.$payload"
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$token = $unsigned + '.' + (Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))))
$base = "http://127.0.0.1:$Port"
function Bridge([string]$operation, [string]$content = '') {
    $root = Join-Path $artifact ([Guid]::NewGuid().ToString('N'))
    $argv = @('run','--project',(Join-Path $repo 'src\Yanzi.SyncVerification'),'--no-build','--',
        '--account-extension-bridge','--operation',$operation,'--root',$root,'--base-url',$base,
        '--token',$token,'--user-id',$user,'--extension-id','taskbar-calendar','--key','calendar.v1.json')
    if ($operation -eq 'write') { $argv += @('--content',$content,'--expected-revision','0') }
    $output = @(& dotnet @argv)
    if ($LASTEXITCODE -ne 0) { throw "Windows calendar bridge failed." }
    $line = $output | Where-Object {$_ -like 'RESULT_JSON:*'} | Select-Object -Last 1
    if (!$line) { throw 'No Windows bridge result.' }
    return $line.Substring(12) | ConvertFrom-Json
}
foreach ($pair in @(
    @('app\build\outputs\apk\dev\app-dev.apk',$hostPackage),
    @('calendar\build\outputs\apk\dev\calendar-dev.apk',$calendarPackage),
    @('calendar\build\outputs\apk\androidTest\dev\calendar-dev-androidTest.apk',"$calendarPackage.test"))) {
    $apk = Join-Path $repo ('mobile\android\'+$pair[0])
    if (!(Test-Path $apk)) { throw "Missing APK: $apk" }
    & $adb -s $Serial install -r $apk | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Emulator install failed.' }
}
& $adb -s $Serial shell pm clear $hostPackage | Out-Null
& $adb -s $Serial shell pm clear $calendarPackage | Out-Null
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
    $doc = @{schemaVersion=1;records=@{'desktop-seed'=@{version='desktop-initial';deleted=$false;
        item=@{Id='desktop-seed';Title='desktop-seed';TargetDate='2080-01-02T00:00:00';AlarmTime=$null;
            IsAlarm=$false;IsTriggered=$false;CreatedTime='2026-10-01T12:00:00'}}}}|ConvertTo-Json -Depth 8 -Compress
    $written=Bridge 'write' $doc
    if ($written.revision -ne 1) { throw 'Desktop fixture was not created.' }
    $xml = '<map><string name="baseUrl">http://10.0.2.2:'+ $Port +'</string><string name="token">'+
        [Security.SecurityElement]::Escape($token)+'</string><string name="deviceId">calendar-test-device</string></map>'
    $prefs = Join-Path $artifact 'prefs.xml'
    [IO.File]::WriteAllText($prefs,$xml,[Text.UTF8Encoding]::new($false))
    & $adb -s $Serial push $prefs /data/local/tmp/calendar-test-prefs.xml | Out-Null
    & $adb -s $Serial shell chmod 644 /data/local/tmp/calendar-test-prefs.xml | Out-Null
    & $adb -s $Serial shell run-as $hostPackage mkdir -p shared_prefs | Out-Null
    & $adb -s $Serial shell run-as $hostPackage cp /data/local/tmp/calendar-test-prefs.xml shared_prefs/yanzi-mobile.xml | Out-Null
    # A shell UID without the signature permission must never enter the bridge.
    $denied = @(& $adb -s $Serial shell content call --uri "content://$hostPackage.extension-storage" --method read --arg taskbar-calendar 2>&1) -join "`n"
    if ($denied -notmatch 'Permission Denial|SecurityException|requires') { throw 'Unsigned caller was not denied.' }
    $result = @(& $adb -s $Serial shell am instrument -w -r -e accountId $user `
        "$calendarPackage.test/android.test.InstrumentationTestRunner") -join "`n"
    [IO.File]::WriteAllText((Join-Path $artifact 'instrumentation.txt'),$result,[Text.UTF8Encoding]::new($false))
    if ($result -notmatch 'OK \(1 test\)') { throw "Calendar instrumentation failed: $result" }
    $returned=Bridge 'read'
    $returnedDoc=$returned.content|ConvertFrom-Json
    if ($returnedDoc.records.'mobile-return'.item.Title -ne 'mobile-return' -or
        !$returnedDoc.records.'desktop-seed'.deleted) { throw 'Windows did not receive mobile changes and tombstone.' }
    Write-Host 'Calendar companion PASSED: Windows -> Android -> Windows, record conflict, CAS, deletion, scope and signature denial.'
    Write-Host "Artifacts: $artifact"
} finally {
    Stop-YanziLocalWorkerPort -Port $Port
    & $adb -s $Serial shell am force-stop $hostPackage | Out-Null
    & $adb -s $Serial shell am force-stop $calendarPackage | Out-Null
    & $adb -s $Serial shell pm clear $hostPackage | Out-Null
    & $adb -s $Serial shell pm clear $calendarPackage | Out-Null
    & $adb -s $Serial shell rm -f /data/local/tmp/calendar-test-prefs.xml | Out-Null
    Remove-Item -LiteralPath (Join-Path $artifact 'prefs.xml') -Force -ErrorAction SilentlyContinue
}

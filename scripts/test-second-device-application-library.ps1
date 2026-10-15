param([ValidateSet('Prepare','Verify','Cleanup')][string]$Mode='Prepare',[string]$Serial='emulator-5554')
$ErrorActionPreference='Stop'
if($Serial -notmatch '^emulator-\d+$'){throw 'This test only modifies an emulator'}
$adb='F:\SDK\platform-tools\adb.exe';$package='cc.luoluoluo.yanzi.mobile'
if($Mode -eq 'Cleanup'){& $adb -s $Serial shell am force-stop $package|Out-Null;& $adb -s $Serial shell pm clear $package|Out-Null;Write-Host 'SECOND_DEVICE_TEST_CLEANED';return}
if($Mode -eq 'Verify'){
    [xml]$state=(@(& $adb -s $Serial shell run-as $package cat shared_prefs/yanzi-mobile.xml)) -join "`n"
    $definitions=$state.SelectSingleNode('/map/string[@name="mobileExtensions"]').InnerText|ConvertFrom-Json
    $note=@($definitions|Where-Object {$_.id -eq 'quick-notes' -and -not $_.bundled})
    if($note.Count -ne 1 -or -not $note[0].script.source){throw 'Account note definition was not downloaded by the second device'}
    $base=$state.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText.TrimEnd('/')
    $token=$state.SelectSingleNode('/map/string[@name="token"]').InnerText
    $library=Invoke-RestMethod "$base/v1/applications/library" -Headers @{Authorization='Bearer '+$token} -TimeoutSec 20
    if(-not @($library.applications|Where-Object {$_.applicationId -eq 'quick-notes' -and $_.enabled})){throw 'Second device cannot see the account selection'}
    Write-Host 'SECOND_DEVICE_ACCOUNT_DEFINITION_AND_LIBRARY_PASSED';return
}
$root=Split-Path -Parent $PSScriptRoot
$apk=Join-Path $root 'mobile\android\app\build\manual-debug\yanzi-mobile-debug.apk'
& $adb -s $Serial install -r $apk|Out-Null;if($LASTEXITCODE -ne 0){throw 'Emulator host installation failed'}
& $adb -s $Serial shell pm clear $package|Out-Null
$physical=(@(& $adb devices|Select-Object -Skip 1|Where-Object {$_ -match '^\S+\s+device$' -and $_ -notmatch '^emulator-'}|ForEach-Object {($_ -split '\s+')[0]}))[0]
if(-not $physical){throw 'Development phone required for same-account verification'}
[xml]$source=(@(& $adb -s $physical shell run-as cc.luoluoluo.yanzi.mobile.dev cat shared_prefs/yanzi-mobile.xml)) -join "`n"
$token=$source.SelectSingleNode('/map/string[@name="token"]').InnerText
$base=$source.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText
if($base -ne 'https://sync.luoluoluo.cc.cd'){throw 'Public service required'}
$temp=Join-Path $env:TEMP ('application-emulator-'+[Guid]::NewGuid().ToString('N')+'.xml')
try{
    $xml='<map><string name="baseUrl">'+[Security.SecurityElement]::Escape($base)+'</string><string name="token">'+[Security.SecurityElement]::Escape($token)+'</string><string name="deviceId">application-library-emulator-verification</string></map>'
    [IO.File]::WriteAllText($temp,$xml,[Text.UTF8Encoding]::new($false))
    & $adb -s $Serial push $temp /data/local/tmp/application-emulator-prefs.xml|Out-Null
    & $adb -s $Serial shell chmod 644 /data/local/tmp/application-emulator-prefs.xml|Out-Null
    & $adb -s $Serial shell run-as $package mkdir -p shared_prefs|Out-Null
    & $adb -s $Serial shell run-as $package cp /data/local/tmp/application-emulator-prefs.xml shared_prefs/yanzi-mobile.xml|Out-Null
}finally{if(Test-Path $temp){Remove-Item -LiteralPath $temp -Force};& $adb -s $Serial shell rm -f /data/local/tmp/application-emulator-prefs.xml|Out-Null}
& $adb -s $Serial shell am start -n "$package/cc.luoluoluo.yanzi.mobile.MainActivity"|Out-Null
Write-Host 'SECOND_DEVICE_PREPARED_WITHOUT_APP_CACHE'

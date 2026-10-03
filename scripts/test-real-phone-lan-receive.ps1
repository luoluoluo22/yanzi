param([switch]$AllowPhysicalDev, [string]$Serial = '')
$ErrorActionPreference = 'Stop'
if (-not $AllowPhysicalDev) { throw 'Use -AllowPhysicalDev for the isolated DEV phone receiver.' }
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
. (Join-Path $PSScriptRoot 'dev-phone-chat-snapshot.ps1')
$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
if (-not $Serial) {
    if ($serials.Count -ne 1) { throw 'Multiple phones are connected; pass -Serial explicitly.' }
    $Serial = $serials[0]
}
if ($serials -notcontains $Serial) { throw 'Selected physical phone is not connected.' }
$serial = $Serial
function AdbChecked([string[]]$Arguments) {
    $saved = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $value = & $adb -s $serial @Arguments 2>&1 } finally { $ErrorActionPreference = $saved }
    if ($LASTEXITCODE -ne 0) { throw 'ADB test operation failed.' }
    return $value
}
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path $env:TEMP ('YanziDev\lan-receive\' + $id)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw | ConvertFrom-Json
$phoneNetwork = AdbChecked @('shell','ip','-4','addr','show','wlan0')
if (($phoneNetwork -join '') -notmatch 'inet ([0-9.]+)/') { throw 'Phone has no Wi-Fi IPv4 address.' }
$ip = $Matches[1]
$productionBefore = (AdbChecked @('shell','pm','path','cc.luoluoluo.yanzi.mobile')) -join ''
$preferences = AdbChecked @('shell','run-as',$package,'cat','shared_prefs/yanzi-mobile.xml')
$backup = Join-Path $root 'preferences-backup.xml'
[IO.File]::WriteAllText($backup,($preferences -join "`n"),[Text.UTF8Encoding]::new($false))
$fixture = Join-Path $root 'send-config.json'
$binary = Join-Path $root ('pc-file-'+$id+'.bin')
$bytes = New-Object byte[] 3145729
([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($bytes)
[IO.File]::WriteAllBytes($binary,$bytes)
$photo = Join-Path $root ('pc-photo-'+$id+'.png')
[IO.File]::WriteAllBytes($photo,[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD1sAAAAASUVORK5CYII='))
$ids = @([Guid]::NewGuid().ToString('N'),[Guid]::NewGuid().ToString('N'))
$verify = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Yanzi.CapabilityVerification\bin\Debug\net9.0-windows\Yanzi.CapabilityVerification.dll'
$securePair = $null
$desktopFixtureRoot = Join-Path $root 'desktop'
New-Item -ItemType Directory -Force -Path $desktopFixtureRoot | Out-Null
$pairBackup = Join-Path $root 'pair-preferences.xml'
[IO.File]::WriteAllText($pairBackup, ((AdbChecked @('shell','run-as',$package,'cat','shared_prefs/YanziPrefs.xml')) -join "`n"), [Text.UTF8Encoding]::new($false))
try {
    [xml]$phonePrefs = $preferences -join "`n"
    $phoneId = [string](@($phonePrefs.map.string | Where-Object { $_.name -eq 'deviceId' })[0].'#text')
    $pairBody = @{deviceId=$phoneId;displayName='Isolated Dev receive';scopes=@('chat','attachments')} | ConvertTo-Json -Compress
    $securePair = Invoke-RestMethod -Uri ('http://127.0.0.1:'+$settings.agentApiPort+'/v1/me/devices/pairs') -Method POST -Headers @{Authorization=('Bearer '+$settings.agentApiToken)} -ContentType 'application/json' -Body $pairBody
    foreach ($file in @('syncsession.json','device-identity.json')) {
        Copy-Item -LiteralPath (Join-Path $env:LOCALAPPDATA ('OpenQuickHost/'+$file)) -Destination (Join-Path $desktopFixtureRoot $file)
    }
    $pairRoot = Join-Path $desktopFixtureRoot 'device-network/pairs'
    New-Item -ItemType Directory -Force -Path $pairRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:LOCALAPPDATA ('OpenQuickHost/device-network/pairs/'+$securePair.pairId+'.json')) -Destination $pairRoot
    $pairConfig = Join-Path $root 'lan-verification.json'
    [IO.File]::WriteAllText($pairConfig, (@{lanBaseUrl='http://127.0.0.1:9';lanToken='';securePair=$securePair;steps=@()} | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    # Pause public account-link refresh while testing a separately granted fixture pair.
    $phonePrefs.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText = 'http://127.0.0.1:9'
    $isolatedPrefs = Join-Path $root 'isolated-preferences.xml'
    $phonePrefs.Save($isolatedPrefs)
    AdbChecked @('push',$isolatedPrefs,'/data/local/tmp/lan-receive-isolated.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/lan-receive-isolated.xml','shared_prefs/yanzi-mobile.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'find','shared_prefs','-name','*.bak','-delete') | Out-Null
    AdbChecked @('push',$pairConfig,'/data/local/tmp/lan-verification.json') | Out-Null
    AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/lan-verification.json','files/lan-verification.json') | Out-Null
    AdbChecked @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity'),'--ez','verify_desktop_lan','true') | Out-Null
    Start-Sleep -Seconds 2
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    AdbChecked @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity')) | Out-Null
    Start-Sleep -Seconds 4
    for ($i=0; $i -lt 2; $i++) {
        $file = @($binary,$photo)[$i]
        $kind = @('file','photo')[$i]
        [IO.File]::WriteAllText($fixture,(@{dataRoot=$desktopFixtureRoot;peerDeviceId=$phoneId;ip=$ip;port=42982;token=$settings.agentApiToken;file=$file;kind=$kind;id=$ids[$i];dropBlockAck=($i -eq 0)} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        dotnet $verify --send-mobile-lan $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Desktop binary LAN send failed.' }
        $phonePath = 'files/mobile-attachments/lan-'+$ids[$i]+'-'+[IO.Path]::GetFileName($file)
        $hash = (AdbChecked @('shell','run-as',$package,'sha256sum',$phonePath)) -join ''
        if (-not $hash.ToLowerInvariant().Contains((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant())) { throw 'Phone receiver checksum mismatch.' }
        dotnet $verify --send-mobile-lan $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Desktop binary LAN retry failed.' }
        if (-not (Test-YanziPhoneChatMessage $adb $serial $package $ids[$i] $root -Contains -ExpectedCount 1)) {
            throw 'Phone LAN retry did not preserve exactly one history entry.'
        }
    }
    Write-Output 'DESKTOP_TO_PHONE_BINARY_PHOTO_FILE_SHA256_AND_RETRY=PASSED'
} finally {
    if ($securePair) { Invoke-RestMethod -Uri ('http://127.0.0.1:'+$settings.agentApiPort+'/v1/me/devices/pairs/'+$securePair.pairId) -Method DELETE -Headers @{Authorization=('Bearer '+$settings.agentApiToken)} | Out-Null }
    foreach ($file in @('syncsession.json','device-identity.json')) { Remove-Item -LiteralPath (Join-Path $desktopFixtureRoot $file) -Force -ErrorAction SilentlyContinue }
    if ($securePair) { Remove-Item -LiteralPath (Join-Path $desktopFixtureRoot ('device-network/pairs/'+$securePair.pairId+'.json')) -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath (Join-Path $root 'lan-verification.json') -Force -ErrorAction SilentlyContinue
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    for ($i=0; $i -lt 2; $i++) {
        $file = @($binary,$photo)[$i]
        AdbChecked @('shell','run-as',$package,'rm','-f',('files/mobile-attachments/lan-'+$ids[$i]+'-'+[IO.Path]::GetFileName($file))) | Out-Null
    }
    AdbChecked @('push',$backup,'/data/local/tmp/lan-receive-preferences.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/lan-receive-preferences.xml','shared_prefs/yanzi-mobile.xml') | Out-Null
    AdbChecked @('shell','rm','-f','/data/local/tmp/lan-receive-preferences.xml') | Out-Null
    AdbChecked @('push',$pairBackup,'/data/local/tmp/lan-pair-preferences.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/lan-pair-preferences.xml','shared_prefs/YanziPrefs.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'rm','-f','files/lan-verification.json','files/lan-verification-result.json') | Out-Null
    AdbChecked @('shell','rm','-f','/data/local/tmp/lan-pair-preferences.xml','/data/local/tmp/lan-verification.json') | Out-Null
    Remove-Item -LiteralPath $pairBackup -Force
    Remove-Item -LiteralPath $backup -Force
    Remove-Item -LiteralPath (Join-Path $root 'isolated-preferences.xml') -Force -ErrorAction SilentlyContinue
    AdbChecked @('shell','rm','-f','/data/local/tmp/lan-receive-isolated.xml') | Out-Null
    AdbChecked @('shell','run-as',$package,'find','shared_prefs','-name','*.bak','-delete') | Out-Null
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
    AdbChecked @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity')) | Out-Null
    $productionAfter = (AdbChecked @('shell','pm','path','cc.luoluoluo.yanzi.mobile')) -join ''
    if ($productionBefore -ne $productionAfter) { throw 'Production app changed.' }
}

param([switch]$AllowPhysicalDev, [switch]$ForceCloudFallback, [string]$Serial = '')
$ErrorActionPreference = 'Stop'
if (-not $AllowPhysicalDev) { throw 'Use -AllowPhysicalDev to test only the isolated DEV package.' }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
$serials = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
if (-not $Serial) {
    if ($serials.Count -ne 1) { throw 'Multiple phones are connected; pass -Serial explicitly.' }
    $Serial = $serials[0]
}
if ($serials -notcontains $Serial) { throw 'Selected physical phone is not connected.' }
$serial = $Serial
$productionBefore = (& $adb -s $serial shell pm path cc.luoluoluo.yanzi.mobile) -join ''
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw | ConvertFrom-Json
if (-not $settings.enableLanSync) { throw 'Enable desktop LAN sync first.' }
$ip = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.InterfaceAlias -ne 'Meta' } | Select-Object -First 1).IPv4Address.IPAddress
$runId = [Guid]::NewGuid().ToString('N')
$root = Join-Path $env:TEMP ('YanziDev\lan-transfer\' + $runId)
$lanRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\mobile-attachments\lan'
New-Item -ItemType Directory -Force -Path $root | Out-Null
$fileBytes = New-Object byte[] 3145729
([Security.Cryptography.RandomNumberGenerator]::Create()).GetBytes($fileBytes)
$binary = Join-Path $root 'fixture.bin'
[IO.File]::WriteAllBytes($binary, $fileBytes)
$photo = Join-Path $root 'fixture.jpg'
$bitmap = New-Object Drawing.Bitmap(540,1200)
$bitmap.SetPixel(0,0,[Drawing.Color]::Blue)
$bitmap.Save($photo,[Drawing.Imaging.ImageFormat]::Jpeg)
$bitmap.Dispose()
# Match the recent user's transfer size without using their actual photo.
$photoBytes = [IO.File]::ReadAllBytes($photo)
if ($photoBytes.Length -lt 75623) {
    $paddedPhoto = New-Object byte[] 75623
    [Array]::Copy($photoBytes,$paddedPhoto,$photoBytes.Length)
    [IO.File]::WriteAllBytes($photo,$paddedPhoto)
}
$remoteFile = Join-Path $root 'remote-binary.bin'
$counter = Join-Path $root 'terminal-count.txt'
$operationIds = @(1..6 | ForEach-Object { [Guid]::NewGuid().ToString() })
$steps = @(
    @{kind='photo';phoneFile='lan-fixture.jpg';name=('lan-photo-'+$runId+'.jpg')},
    @{kind='file';phoneFile='lan-fixture.bin';name=('lan-file-'+$runId+'.bin')},
    @{kind='remote';path='/v1/fs/write';payload=@{path=$remoteFile;content=[Convert]::ToBase64String([byte[]](0,1,2,128,255));base64=$true;clientOperationId=$operationIds[0]}},
    @{kind='remote';path='/v1/fs/list';payload=@{path=$root;clientOperationId=$operationIds[1]}},
    @{kind='remote';path='/v1/fs/read';payload=@{path=$counter;clientOperationId=$operationIds[2]}},
    @{kind='remote';path='/v1/shell/run';payload=@{command=("Start-Sleep -Milliseconds 1500; Add-Content -LiteralPath '"+$counter+"' -Value 'once'; Write-Output 'lan-terminal-ok'");clientOperationId=$operationIds[3]}},
    @{kind='remote';path='/v1/shell/run';payload=@{command=("Start-Sleep -Milliseconds 1500; Add-Content -LiteralPath '"+$counter+"' -Value 'once'; Write-Output 'lan-terminal-ok'");clientOperationId=$operationIds[3]}},
    @{kind='remote';path='/v1/fs/read';payload=@{path=$counter;clientOperationId=$operationIds[4]}}
)
$config = @{lanBaseUrl=('http://'+$ip+':'+$settings.agentApiPort);lanToken=$settings.agentApiToken;cloudBaseUrl='http://127.0.0.1:9';cloudToken='unused-lan-verification-token';steps=$steps}
if ($ForceCloudFallback) {
    $steps = @($steps[0],$steps[1])
    $config.steps = $steps
    $config.lanBaseUrl = 'http://127.0.0.1:9'
    $config.cloudBaseUrl = 'http://127.0.0.1:53929'
    $config.cloudToken = 'disposable-cloud-transfer-test'
    $config.forceLanFailure = $true
}
$configPath = Join-Path $root 'lan-verification.json'
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
function AdbChecked([string[]]$Arguments) {
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $value = & $adb -s $serial @Arguments 2>&1 }
    finally { $ErrorActionPreference = $savedPreference }
    if ($LASTEXITCODE -ne 0) { throw 'ADB test operation failed.' }
    return $value
}
$clipboardSnapshot = New-Object Windows.Forms.DataObject
$originalClipboard = [Windows.Forms.Clipboard]::GetDataObject()
if ($null -ne $originalClipboard) {
    foreach ($format in $originalClipboard.GetFormats($false)) {
        $value = $originalClipboard.GetData($format, $false)
        if ($null -eq $value) { throw 'Clipboard format could not be preserved.' }
        if ($value -is [IO.MemoryStream]) { $value = New-Object IO.MemoryStream(,$value.ToArray()) }
        elseif ($value -is [ICloneable]) { $value = $value.Clone() }
        $clipboardSnapshot.SetData($format,$false,$value)
    }
}
$results = $null
$pairingBackup = Join-Path $root 'pairing-backup.xml'
$savedPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$oldPairing = & $adb -s $serial shell run-as $package cat shared_prefs/YanziPrefs.xml 2>$null
$hadPairing = $LASTEXITCODE -eq 0
$ErrorActionPreference = $savedPreference
if ($hadPairing) { [IO.File]::WriteAllText($pairingBackup,($oldPairing -join "`n"),[Text.UTF8Encoding]::new($false)) }
$securePair = $null
try {
    if (-not $ForceCloudFallback) {
        [xml]$phonePrefs = (AdbChecked @('shell','run-as',$package,'cat','shared_prefs/yanzi-mobile.xml')) -join "`n"
        $phoneId = [string](@($phonePrefs.map.string | Where-Object { $_.name -eq 'deviceId' })[0].'#text')
        $pairBody = @{deviceId=$phoneId;displayName='Isolated Dev LAN test';scopes=@('chat','attachments','files.remote','terminal')} | ConvertTo-Json -Compress
        $securePair = Invoke-RestMethod -Uri ('http://127.0.0.1:'+$settings.agentApiPort+'/v1/me/devices/pairs') -Method POST -Headers @{Authorization=('Bearer '+$settings.agentApiToken)} -ContentType 'application/json' -Body $pairBody
        $config.securePair = $securePair
        [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    }
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    AdbChecked @('shell','run-as',$package,'mkdir','-p','files') | Out-Null
    foreach ($pair in @(@($configPath,'lan-verification.json'),@($binary,'lan-fixture.bin'),@($photo,'lan-fixture.jpg'))) {
        AdbChecked @('push',$pair[0],('/data/local/tmp/'+$pair[1])) | Out-Null
        AdbChecked @('shell','run-as',$package,'cp',('/data/local/tmp/'+$pair[1]),('files/'+$pair[1])) | Out-Null
        AdbChecked @('shell','rm',('/data/local/tmp/'+$pair[1])) | Out-Null
    }
    AdbChecked @('shell','run-as',$package,'rm','-f','files/lan-verification-result.json') | Out-Null
    AdbChecked @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity'),'--ez','verify_desktop_lan','true') | Out-Null
    $deadline = (Get-Date).AddSeconds(100)
    do {
        Start-Sleep -Milliseconds 400
        $raw = AdbChecked @('shell','run-as',$package,'sh','-c',"'if test -f files/lan-verification-result.json; then cat files/lan-verification-result.json; fi'")
        if ($raw) { $results = ($raw -join '') | ConvertFrom-Json; break }
    } while ((Get-Date) -lt $deadline)
    if ($null -eq $results -or $results.error -or $results.results.Count -ne $steps.Count) { throw 'Phone verification did not complete.' }
    $rows = @($results.results)
    [IO.File]::WriteAllText((Join-Path $root 'result.json'), ($results | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    if (-not $rows[0].success -or -not $rows[1].success) { throw 'Photo/file LAN send failed.' }
    if ($ForceCloudFallback) {
        if ($rows[0].messageId -notlike 'msg_fixture_*' -or $rows[1].messageId -notlike 'msg_fixture_*') { throw 'Cloud fallback route was not used.' }
        Write-Output 'PHONE_PHOTO_AND_FILE_CLOUD_FALLBACK=PASSED'
        $rows | Select-Object kind,success,durationMs | Format-Table | Out-String | Write-Output
        [IO.File]::WriteAllText((Join-Path $root 'result.json'), ($results | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
        return
    }
    $lanRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\mobile-attachments\lan'
    $savedFile = Join-Path $lanRoot ($rows[1].messageId+'-'+$steps[1].name)
    if ((Get-FileHash -LiteralPath $savedFile).Hash -ne (Get-FileHash -LiteralPath $binary).Hash) { throw 'Binary LAN checksum mismatch.' }
    if (-not (Test-Path (Join-Path $lanRoot ($rows[0].messageId+'-'+$steps[0].name)))) { throw 'LAN photo not saved.' }
    if (-not $rows[2].success -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($remoteFile)) -ne 'AAECgP8=') { throw 'Remote binary file upload mismatch.' }
    if (-not $rows[3].success -or -not $rows[3].response.ok) { throw 'LAN remote file listing failed.' }
    if ($rows[4].success -or $rows[4].error -ne 'DesktopOperationRejected') { throw 'A missing file must fail without cloud fallback.' }
    if (-not $rows[5].success -or $rows[5].response.output -notmatch 'lan-terminal-ok' -or $rows[5].durationMs -lt 1500) { throw 'Slow LAN terminal command failed.' }
    if (-not $rows[6].success -or (Get-Content $counter).Count -gt 1) { throw 'Terminal retry executed twice.' }
    if (-not $rows[7].success -or $rows[7].response.content -notmatch 'once') { throw 'LAN remote file read failed.' }
    $rows | Select-Object kind,success,durationMs | Format-Table | Out-String | Write-Output
    [IO.File]::WriteAllText((Join-Path $root 'result.json'), ($results | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    Write-Output ('PHONE_LAN_PHOTO_FILE_REMOTE_FILES_TERMINAL_AND_RETRY=PASSED; Report: '+(Join-Path $root 'result.json'))
} finally {
    if ($securePair) { Invoke-RestMethod -Uri ('http://127.0.0.1:'+$settings.agentApiPort+'/v1/me/devices/pairs/'+$securePair.pairId) -Method DELETE -Headers @{Authorization=('Bearer '+$settings.agentApiToken)} | Out-Null }
    if ($null -ne $originalClipboard) { [Windows.Forms.Clipboard]::SetDataObject($clipboardSnapshot,$true) } else { [Windows.Forms.Clipboard]::Clear() }
    AdbChecked @('shell','am','force-stop',$package) | Out-Null
    AdbChecked @('shell','run-as',$package,'rm','-f','files/lan-verification.json','files/lan-fixture.bin','files/lan-fixture.jpg','files/lan-verification-result.json') | Out-Null
    AdbChecked @('shell','rm','-f','/data/local/tmp/lan-verification.json','/data/local/tmp/lan-fixture.bin','/data/local/tmp/lan-fixture.jpg') | Out-Null
    Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue
    if ($hadPairing) {
        AdbChecked @('push',$pairingBackup,'/data/local/tmp/lan-pairing-backup.xml') | Out-Null
        AdbChecked @('shell','run-as',$package,'cp','/data/local/tmp/lan-pairing-backup.xml','shared_prefs/YanziPrefs.xml') | Out-Null
        AdbChecked @('shell','rm','-f','/data/local/tmp/lan-pairing-backup.xml') | Out-Null
        Remove-Item -LiteralPath $pairingBackup -Force
    }
    if ($null -ne $results) {
        $ids = @($results.results | Where-Object { $_.messageId } | ForEach-Object { $_.messageId })
        $inbox = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\mobile-inbox.jsonl'
        $remaining = @(Get-Content $inbox | Where-Object { $ids -notcontains ($_ | ConvertFrom-Json).messageId })
        [IO.File]::WriteAllLines($inbox,$remaining,[Text.UTF8Encoding]::new($false))
        for ($i=0; $i -lt 2; $i++) {
            $id = $results.results[$i].messageId
            if ($id -match '^[a-f0-9]{32}$') {
                Remove-Item -LiteralPath (Join-Path $lanRoot ($id+'-'+$steps[$i].name)) -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath (Join-Path $lanRoot ($id+'.json')) -Force -ErrorAction SilentlyContinue
            }
        }
    }
    $productionAfter = (& $adb -s $serial shell pm path cc.luoluoluo.yanzi.mobile) -join ''
    if ($productionBefore -ne $productionAfter) { throw 'Production app changed during isolated DEV test.' }
    AdbChecked @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity')) | Out-Null
}

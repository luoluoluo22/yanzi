param([string]$Serial='')
$ErrorActionPreference='Stop'
$adb='F:\SDK\platform-tools\adb.exe'
if(-not $Serial){$Serial=(@(& $adb devices | Select-Object -Skip 1 | Where-Object {$_ -match '^\S+\s+device$' -and $_ -notmatch '^emulator-'} | ForEach-Object {($_ -split '\s+')[0]}))[0]}
if(-not $Serial){throw 'Physical development phone required'}
# Read development credentials in memory only; never write or print them.
[xml]$prefs=(@(& $adb -s $Serial shell run-as cc.luoluoluo.yanzi.mobile.dev cat shared_prefs/yanzi-mobile.xml)) -join "`n"
$token=$prefs.SelectSingleNode('/map/string[@name="token"]').InnerText
$base=$prefs.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText.TrimEnd('/')
if($base -ne 'https://sync.luoluoluo.cc.cd'){throw 'Development phone is not connected to the public service'}
$auth=@{Authorization='Bearer '+$token}
$catalog=Invoke-RestMethod "$base/v1/applications/catalog" -TimeoutSec 20
$artifacts=Join-Path $env:TEMP ('YanziApplicationPublic-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($artifacts)|Out-Null
foreach($app in $catalog.applications){
    $asset=Join-Path $artifacts ([IO.Path]::GetFileName($app.downloadPath))
    Invoke-WebRequest ($base+$app.downloadPath) -OutFile $asset -TimeoutSec 30
    if((Get-Item $asset).Length -ne $app.size -or (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant() -ne $app.sha256){throw 'Public artifact checksum failed'}
}
$grant=$null
try{
    $before=Invoke-RestMethod "$base/v1/extension-data/quick-notes?key=notes.v1.json" -Headers $auth -TimeoutSec 20
    $grant=Invoke-RestMethod "$base/v1/applications/quick-notes/grants" -Method Post -Headers $auth -ContentType 'application/json' -Body '{"userConsent":true,"access":"read","clientName":"Public integration verification"}' -TimeoutSec 20
    $scoped=@{Authorization='Bearer '+$grant.accessToken}
    $read=Invoke-RestMethod "$base/v1/extension-data/quick-notes?key=notes.v1.json" -Headers $scoped -TimeoutSec 20
    if($read.content -ne $before.content -or $read.accountId -ne $before.accountId){throw 'Public scoped read differs'}
    foreach($pair in @(@('/v1/sync/objects',401),@('/v1/extension-data/taskbar-calendar?key=calendar.v1.json',403))){
        try{Invoke-RestMethod ($base+$pair[0]) -Headers $scoped -TimeoutSec 20|Out-Null;throw 'Scoped request unexpectedly succeeded'}catch{if([int]$_.Exception.Response.StatusCode -ne $pair[1]){throw}}
    }
    Invoke-RestMethod ($base+'/v1/applications/quick-notes/grants/'+$grant.grantId) -Method Delete -Headers $auth -TimeoutSec 20|Out-Null
    try{Invoke-RestMethod "$base/v1/extension-data/quick-notes?key=notes.v1.json" -Headers $scoped -TimeoutSec 20|Out-Null;throw 'Revoked request succeeded'}catch{if([int]$_.Exception.Response.StatusCode -ne 403){throw}}
    $grant=$null
    $library=Invoke-RestMethod "$base/v1/applications/library" -Headers $auth -TimeoutSec 20
    Write-Host "PUBLIC_APPLICATION_PLATFORM_PASSED: artifacts=$($catalog.applications.Count), accountSelections=$($library.applications.Count), scoped read, account API denial, cross-app denial, revocation"
    Write-Host "Verified downloads: $artifacts"
}finally{if($grant){Invoke-RestMethod ($base+'/v1/applications/quick-notes/grants/'+$grant.grantId) -Method Delete -Headers $auth -TimeoutSec 20|Out-Null}}

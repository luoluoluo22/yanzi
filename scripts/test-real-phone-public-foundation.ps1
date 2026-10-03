param([Parameter(Mandatory=$true)][string]$Serial)
$ErrorActionPreference = 'Stop'
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
[xml]$prefs = (@(& $adb -s $Serial exec-out run-as $package cat shared_prefs/yanzi-mobile.xml) -join "`n")
$base = $prefs.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText.TrimEnd('/')
$token = $prefs.SelectSingleNode('/map/string[@name="token"]').InnerText
$device = $prefs.SelectSingleNode('/map/string[@name="deviceId"]').InnerText
if ($base -ne 'https://sync.luoluoluo.cc.cd' -or -not $token) { throw 'Dev phone must be signed in to the public service.' }
$headers = @{Authorization=('Bearer '+$token);'X-Yanzi-Client'='mobile';'User-Agent'='YanziClient-Mobile/foundation-verification'}
function PublicCall([string]$Path, [string]$Method = 'GET', $Body = $null) {
    $options = @{Uri=($base+$Path);Headers=$headers;Method=$Method;TimeoutSec=30}
    if ($null -ne $Body) { $options.ContentType='application/json'; $options.Body=[Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress)) }
    Invoke-RestMethod @options
}
function Check([bool]$Value, [string]$Label) { if (-not $Value) { throw ('Public foundation check failed: '+$Label) }; Write-Output ($Label+'=PASSED') }
$health = Invoke-RestMethod ($base+'/health') -TimeoutSec 20
Check ($health.ok -and $health.foundationRevision -eq '2026-10-03-domains-v1') 'PUBLIC_DEPLOYED_FOUNDATION_REVISION'
$me = PublicCall '/v1/auth/me'
Check ([bool]$me.userId) 'PUBLIC_EXISTING_PHONE_AUTHENTICATION'
$caps = PublicCall '/v1/sync/capabilities'
Check ($caps.objectSyncAvailable -and $caps.objectHistoryAvailable) 'PUBLIC_OBJECT_AND_HISTORY_CAPABILITIES'
$id = 'verification.foundation.' + [Guid]::NewGuid().ToString('N')
$path = '/v1/sync/objects/' + $id
$created = $false
$cleaned = $false
try {
    $text = ([string][char]0x624b)+([string][char]0x673a)+"`nUTF-8 round trip"
    $write = PublicCall $path 'PUT' @{expectedRevision=0;schemaVersion=1;payload=@{text=$text};updatedByDeviceId=$device}
    $created = $true
    $first = $write.object
    $read = PublicCall $path
    Check ($first.revision -gt 0 -and $read.object.payload.text -eq $text) 'PUBLIC_PHONE_ACCOUNT_OBJECT_UTF8_READ_WRITE'
    $write = PublicCall $path 'PUT' @{expectedRevision=$first.revision;payload=@{text='updated'};updatedByDeviceId=$device}
    $latest = $write.object
    $conflict = $false
    try { PublicCall $path 'PUT' @{expectedRevision=$first.revision;payload=@{text='stale'}} | Out-Null }
    catch { $conflict = [int]$_.Exception.Response.StatusCode -eq 409 }
    Check $conflict 'PUBLIC_STALE_REVISION_CONFLICT'
    Check ((PublicCall $path).object.payload.text -eq 'updated') 'PUBLIC_CONFLICT_PRESERVES_CURRENT_DATA'
    $history = PublicCall ('/v1/sync/history?objectId='+$id)
    Check ($history.versions.Count -eq 2 -and $history.versions[0].revision -eq $latest.revision) 'PUBLIC_IMMUTABLE_OBJECT_HISTORY'
    $deleted = PublicCall $path 'PUT' @{expectedRevision=$latest.revision;deleted=$true;payload=@{};updatedByDeviceId=$device}
    $cleaned = $true
    Check ((PublicCall $path).object.deleted -and $deleted.object.revision -gt $latest.revision) 'PUBLIC_TEST_OBJECT_TOMBSTONE_CLEANUP'
    Write-Output 'REAL_PHONE_PUBLIC_ACCOUNT_FOUNDATION=PASSED'
} finally {
    if ($created -and -not $cleaned) {
        $current = (PublicCall $path).object
        if (-not $current.deleted) { PublicCall $path 'PUT' @{expectedRevision=$current.revision;deleted=$true;payload=@{};updatedByDeviceId=$device} | Out-Null }
    }
    $token = $null
    $headers = $null
}

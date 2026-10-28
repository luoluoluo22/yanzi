param([string]$Serial = '81f7e66d')
$ErrorActionPreference = 'Stop'
$adb = 'F:\SDK\platform-tools\adb.exe'
$package = 'cc.luoluoluo.yanzi.mobile.dev'
[xml]$prefs = (@(& $adb -s $Serial exec-out run-as $package cat shared_prefs/yanzi-mobile.xml) -join "`n")
if ($LASTEXITCODE -ne 0) { throw 'Dev preferences unavailable' }
$token = $prefs.SelectSingleNode('/map/string[@name="token"]').InnerText
$base = $prefs.SelectSingleNode('/map/string[@name="baseUrl"]').InnerText.TrimEnd('/')
$device = $prefs.SelectSingleNode('/map/string[@name="deviceId"]').InnerText
if ($base -ne 'https://sync.luoluoluo.cc.cd' -or -not $token) { throw 'Signed-in public Dev account required' }
$headers = @{ Authorization = "Bearer $token" }
# Synthetic unknown state in a unique test namespace; never collects GPS or changes the real position program.
$extension = 'environment-check-' + [Guid]::NewGuid().ToString('N')
$path = "$base/v1/me/devices/$device/environment/$extension"
$sequence = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$sample = @{ sequence=$sequence; enabled=$true; observedAt=[DateTimeOffset]::UtcNow.ToString('O'); place='unknown'; confidence='unknown'; availability='verification'; network=@{type='none'}; location=@{latitude=0;longitude=0;accuracy=5} }
function PutSnapshot($value) {
    return Invoke-RestMethod $path -Method Put -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 6 -Compress))) -TimeoutSec 20
}
try {
    PutSnapshot $sample | Out-Null
    $value = Invoke-RestMethod $path -Headers $headers -TimeoutSec 20
    if (-not $value.exists -or $value.value.PSObject.Properties['location']) { throw 'Default privacy failed' }
    PutSnapshot @{sequence=($sequence+1);enabled=$false} | Out-Null
    $stopped = Invoke-RestMethod $path -Headers $headers -TimeoutSec 20
    if ($stopped.exists -or $null -ne $stopped.value) { throw 'Stop failed' }
    try { PutSnapshot $sample | Out-Null; throw 'Old request accepted' }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 409) { throw } }
    Write-Output 'PUBLIC_DEVICE_ENVIRONMENT=PASSED; latest-state, coordinate-default, stop and stale-ordering verified'
} finally {
    PutSnapshot @{sequence=($sequence+2);enabled=$false} | Out-Null
}

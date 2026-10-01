param(
    [Parameter(Mandatory=$true)][string]$CalendarApkPath,
    [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=Join-Path $root '.artifacts\application-catalog'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$apk=(Resolve-Path -LiteralPath $CalendarApkPath).Path
$sdk=if($env:ANDROID_HOME){$env:ANDROID_HOME}else{'F:\SDK'}
$tools=Get-ChildItem (Join-Path $sdk 'build-tools') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$badging=@(& (Join-Path $tools.FullName 'aapt.exe') dump badging $apk)
if($LASTEXITCODE -ne 0 -or $badging[0] -notmatch "name='cc.luoluoluo.yanzi.calendar' versionCode='(\d+)' versionName='([^']+)'"){throw 'Calendar production package identity mismatch'}
$versionCode=[int]$Matches[1];$version=$Matches[2]
$signature=@(& (Join-Path $tools.FullName 'apksigner.bat') verify --print-certs $apk)
if($LASTEXITCODE -ne 0){throw 'APK signature invalid'}
$certificateLine=$signature | Where-Object {$_ -match '^Signer #1 certificate SHA-256 digest: ([a-f0-9]+)$'} | Select-Object -First 1
if(-not $certificateLine -or $certificateLine -notmatch ': ([a-f0-9]+)$'){throw 'Certificate digest unavailable'}
$certificate=$Matches[1]
$definition=Get-Content -Raw -LiteralPath (Join-Path $root 'extensions\quick-notes\mobile.json') | ConvertFrom-Json
$source=Get-Content -Raw -LiteralPath (Join-Path $root 'extensions\quick-notes\mobile.js')
$definition | Add-Member -NotePropertyName script -NotePropertyValue @{source=$source} -Force
$definitionPath=Join-Path $output "quick-notes-$($definition.version).json"
[IO.File]::WriteAllText($definitionPath,($definition | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
$calendarPath=Join-Path $output "yanzi-calendar-$version.apk"
Copy-Item -LiteralPath $apk -Destination $calendarPath -Force
$assets=@($definitionPath,$calendarPath)
$apps=@(
    @{applicationId='quick-notes';name='便签';kind='mobile-js';version=$definition.version;description='内嵌便签，支持账号数据同步';minHostVersionCode=27;downloadPath="/downloads/applications/quick-notes-$($definition.version).json";size=(Get-Item $definitionPath).Length;sha256=(Get-FileHash $definitionPath -Algorithm SHA256).Hash.ToLowerInvariant()},
    @{applicationId='taskbar-calendar';name='日历';kind='android-apk';version=$version;versionCode=$versionCode;description='独立日历，与电脑小程序双向同步';packageName='cc.luoluoluo.yanzi.calendar';minHostVersionCode=27;certificateSha256=$certificate;downloadPath="/downloads/applications/yanzi-calendar-$version.apk";size=(Get-Item $calendarPath).Length;sha256=(Get-FileHash $calendarPath -Algorithm SHA256).Hash.ToLowerInvariant()}
)
# Preserve future independently published catalog entries.
try {
    $existing=Invoke-RestMethod 'https://sync.luoluoluo.cc.cd/v1/applications/catalog' -TimeoutSec 20
    $apps+=@($existing.applications | Where-Object {$_.applicationId -notin @('quick-notes','taskbar-calendar')})
} catch { if(-not $PrepareOnly -and (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404)){throw} }
$catalogPath=Join-Path $output 'catalog.json'
[IO.File]::WriteAllText($catalogPath,(@{schemaVersion=1;publishedAt=[DateTime]::UtcNow.ToString('o');applications=@($apps)} | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
if($PrepareOnly){Write-Host 'CATALOG_PREPARED';return}
$originalToken=$env:CLOUDFLARE_API_TOKEN
try {
    if(-not $env:CLOUDFLARE_API_TOKEN){foreach($file in @((Join-Path $root '.env'),(Join-Path $root 'cloudflare\.dev.vars'))){if(Test-Path $file){$line=Get-Content $file | Where-Object {$_ -match '^CLOUDFLARE_API_TOKEN='} | Select-Object -First 1;if($line){$env:CLOUDFLARE_API_TOKEN=($line -replace '^CLOUDFLARE_API_TOKEN=','').Trim().Trim('"').Trim("'");break}}}}
    if(-not $env:CLOUDFLARE_API_TOKEN){throw 'Publication credential unavailable'}
    # Publish immutable artifacts before making their catalog entries discoverable.
    foreach($file in $assets){
        $type=if($file.EndsWith('.apk')){'application/vnd.android.package-archive'}else{'application/json'}
        & npx.cmd wrangler r2 object put "openquickhost-sync-assets/downloads/applications/$([IO.Path]::GetFileName($file))" --file $file --content-type $type --remote --config (Join-Path $root 'cloudflare\wrangler.toml')
        if($LASTEXITCODE -ne 0){throw 'Artifact upload failed; catalog was not published'}
    }
    & npx.cmd wrangler r2 object put openquickhost-sync-assets/downloads/applications/catalog.json --file $catalogPath --content-type application/json --remote --config (Join-Path $root 'cloudflare\wrangler.toml')
    if($LASTEXITCODE -ne 0){throw 'Catalog upload failed'}
    Write-Host 'APPLICATION_CATALOG_PUBLISHED'
} finally {$env:CLOUDFLARE_API_TOKEN=$originalToken}

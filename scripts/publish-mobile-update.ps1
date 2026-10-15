param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$ApkPath,
    [string]$Repo='luoluoluo22/yanzi',
    [switch]$SkipApkUpload
)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') {throw 'Invalid Android version'}
$root=Split-Path -Parent $PSScriptRoot
$apk=(Resolve-Path -LiteralPath $ApkPath).Path
$sdk=if($env:ANDROID_HOME){$env:ANDROID_HOME}else{'F:\SDK'}
$tools=Get-ChildItem (Join-Path $sdk 'build-tools') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$badging=@(& (Join-Path $tools.FullName 'aapt.exe') dump badging $apk)
if ($LASTEXITCODE -ne 0 -or $badging[0] -notmatch "name='cc.luoluoluo.yanzi.mobile'" -or $badging[0] -notmatch "versionName='$([regex]::Escape($Version))'") {
    throw 'APK package/version does not match the production release'
}
& (Join-Path $tools.FullName 'apksigner.bat') verify $apk
if ($LASTEXITCODE -ne 0) {throw 'APK signature verification failed'}
$releaseRaw=& gh release view "android-v$Version" --repo $Repo --json tagName,name,body,isDraft,assets
if ($LASTEXITCODE -ne 0) {throw 'Publish the GitHub Android release before its update manifest'}
$release=$releaseRaw | ConvertFrom-Json
if ($release.isDraft) {throw 'Draft releases cannot be offered as updates'}
$asset=@($release.assets | Where-Object {$_.name -eq "yanzi-mobile-$Version.apk"})
if ($asset.Count -ne 1) {throw 'Published Android APK asset is missing'}
$hash=(Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
if ($asset[0].digest -and $asset[0].digest -ne "sha256:$hash") {throw 'GitHub APK differs from the public update APK'}
$originalToken=$env:CLOUDFLARE_API_TOKEN
$originalAccount=$env:CLOUDFLARE_ACCOUNT_ID
$tempFile=Join-Path $env:TEMP ('yanzi-android-manifest-'+[Guid]::NewGuid().ToString('N')+'.json')
try {
    if (-not $env:CLOUDFLARE_API_TOKEN) {
        foreach($file in @((Join-Path $root '.env'),(Join-Path $root 'cloudflare\.dev.vars'))) {
            if(Test-Path $file){
                $line=Get-Content -LiteralPath $file | Where-Object {$_ -match '^CLOUDFLARE_API_TOKEN='} | Select-Object -First 1
                if($line){$env:CLOUDFLARE_API_TOKEN=($line -replace '^CLOUDFLARE_API_TOKEN=','').Trim().Trim('"').Trim("'");break}
            }
        }
    }
    if(-not $env:CLOUDFLARE_API_TOKEN){throw 'Cloudflare publish credential is unavailable'}
    $config=Join-Path $root 'cloudflare\wrangler.toml'
    $object="openquickhost-sync-assets/downloads/android/yanzi-mobile-$Version.apk"
    if(-not $SkipApkUpload){
        & npx.cmd wrangler r2 object put $object --file $apk --content-type application/vnd.android.package-archive --remote --config $config
        if($LASTEXITCODE -ne 0){throw 'Public APK upload failed; update manifest was not changed'}
    }
    $manifest=@(@{tag_name=$release.tagName;draft=$false;body=$release.body;assets=@(@{
        name="yanzi-mobile-$Version.apk";digest="sha256:$hash"
        browser_download_url="https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-$Version.apk"
    })})
    [IO.File]::WriteAllText($tempFile,(ConvertTo-Json -InputObject $manifest -Depth 8),[Text.UTF8Encoding]::new($false))
    & npx.cmd wrangler r2 object put openquickhost-sync-assets/downloads/android/releases.json --file $tempFile --content-type application/json --remote --config $config
    if($LASTEXITCODE -ne 0){throw 'Public update manifest upload failed'}
    Write-Host "PUBLIC_ANDROID_UPDATE_PUBLISHED=$Version"
} finally {
    $env:CLOUDFLARE_API_TOKEN=$originalToken
    $env:CLOUDFLARE_ACCOUNT_ID=$originalAccount
    if(Test-Path $tempFile){[IO.File]::Delete($tempFile)}
}

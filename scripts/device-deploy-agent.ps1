#Requires -Version 5.1
<#
Yanzi cross-device deployment receiver. Designed to run OUTSIDE Yanzi via Task Scheduler.
Only the pinned publisher's non-draft, non-prerelease GitHub Releases are accepted.
Do not pipe downloaded content to a shell. The versioned setup EXE must match its
GitHub-published SHA-256 digest before unattended execution.
#>
param(
    [ValidateSet('check','apply','status','rollback')]
    [string] $Mode = 'check',
    [string] $TargetVersion = '',
    [switch] $DryRun
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'luoluoluo22/yanzi'
$apiUrl = "https://api.github.com/repos/$repo/releases"
$base = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent'
$downloads = Join-Path $base 'packages'
$statusFile = Join-Path $base 'status.json'
$logFile = Join-Path $base 'deploy.log'
$exe = Join-Path $env:LOCALAPPDATA 'Yanzi\current\Yanzi.exe'
$stub = Join-Path $env:LOCALAPPDATA 'Yanzi\Yanzi.exe'
New-Item -Path $base,$downloads -ItemType Directory -Force | Out-Null
$script:status = [ordered]@{
    machine = $env:COMPUTERNAME
    state = 'idle'
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    installedVersion = ''
    targetVersion = ''
    release = ''
    source = 'github'
    message = ''
}
function Write-Log([string] $message) {
    $line = '[' + [DateTime]::UtcNow.ToString('o') + '] ' + $message
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}
function Save-State([string] $state,[string] $message) {
    $script:status.state = $state
    $script:status.message = $message
    $script:status.checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    $temp = "$statusFile.tmp.$PID"
    $json = ConvertTo-Json -InputObject $script:status -Depth 5
    [IO.File]::WriteAllText($temp,$json,(New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temp -Destination $statusFile -Force
    Write-Log "state=$state; $message"
}
function Installed-Version {
    if (-not (Test-Path -LiteralPath $exe)) { return [version]'0.0.0' }
    $v = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    try { return [version]$v }
    catch { return [version]'0.0.0' }
}
function Parse-ReleaseVersion([string] $tag) {
    if ($tag -notmatch '^v?([0-9]+\.[0-9]+\.[0-9]+)$') { throw "Invalid version tag: $tag" }
    return [version]($Matches[1] + '.0')
}
function Read-Release([string] $version) {
    $url = if ($version) { "$apiUrl/tags/v$version" } else { "$apiUrl/latest" }
    $headers = @{ 'User-Agent' = 'Yanzi-Deployment-Agent/1'; 'Accept' = 'application/vnd.github+json' }
    # An ephemeral Actions token avoids GitHub shared-runner anonymous rate limits.
    # Production receivers work without a token; never log or persist it.
    $apiToken = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }
    if (-not [string]::IsNullOrWhiteSpace($apiToken)) { $headers['Authorization'] = 'Bearer ' + $apiToken }
    $rel = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 20
    if ($rel.draft -or $rel.prerelease) { throw 'Refusing draft/prerelease' }
    $releaseVersion = Parse-ReleaseVersion $rel.tag_name
    $assetName = 'Yanzi-win-Setup-' + $releaseVersion.ToString(3) + '.exe'
    $assets = @($rel.assets | Where-Object { $_.name -eq $assetName })
    if ($assets.Count -ne 1) { throw "Expected exactly one canonical setup asset: $assetName" }
    $asset = $assets[0]
    $prefix = "https://github.com/$repo/releases/download/v$($releaseVersion.ToString(3))/"
    if (-not ([string]$asset.browser_download_url).StartsWith($prefix,[StringComparison]::Ordinal)) {
        throw 'Release asset URL is not under pinned publisher'
    }
    if ([string]$asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$') {
        throw 'Release asset has no SHA-256 digest: fail closed'
    }
    return [pscustomobject]@{
        version = $releaseVersion
        tag = $rel.tag_name
        name = $assetName
        url = [string]$asset.browser_download_url
        sha256 = $Matches[1].ToUpperInvariant()
        size = [long]$asset.size
    }
}
function Verify-File([string] $path, $release) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne $release.size) { return $false }
    return ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $release.sha256)
}
function Download-Release($release) {
    $dest = Join-Path $downloads $release.name
    if (Verify-File $dest $release) { return $dest }
    $partial = "$dest.partial"
    $urls = @($release.url,'https://ghfast.top/'+$release.url)
    foreach ($url in $urls) {
        for ($attempt=1; $attempt -le 2; $attempt++) {
            Save-State 'downloading' "target=$($release.tag); mirror=$($url -ne $release.url); attempt=$attempt"
            # curl supports resumable .partial transfer; any wrong payload fails hash and is removed.
            & curl.exe --location --fail --silent --show-error --retry 2 --connect-timeout 12 --max-time 240 -C - --output $partial $url
            $exitCode = $LASTEXITCODE
            if ($exitCode -eq 0 -and (Verify-File $partial $release)) {
                Move-Item -LiteralPath $partial -Destination $dest -Force
                return $dest
            }
            if ($exitCode -eq 0) { Write-Log "Hash/size mismatch for target=$($release.tag)"; Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Seconds 2
        }
        Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
    }
    throw "All download paths failed or checksum mismatch: $($release.tag)"
}
function Start-Yanzi {
    $launch = if (Test-Path $stub) { $stub } else { $exe }
    if (Test-Path $launch) { Start-Process -FilePath $launch | Out-Null }
}
if ($Mode -eq 'status') {
    if (Test-Path $statusFile) { Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8 }
    else { Write-Output '{"state":"not_started"}' }
    return
}
$mutex = New-Object System.Threading.Mutex($false,'Local\YanziCrossDeviceDeploy')
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { Write-Output 'DEPLOY_BUSY'; return }
    $script:status.installedVersion = (Installed-Version).ToString(3)
    Save-State 'checking' "installed=$($script:status.installedVersion)"
    if ($Mode -eq 'rollback' -and -not $TargetVersion) { throw 'Rollback requires an explicit -TargetVersion' }
    $release = Read-Release $TargetVersion
    $script:status.targetVersion = $release.version.ToString(3)
    $script:status.release = $release.tag
    $current = Installed-Version
    if ($Mode -ne 'rollback' -and $release.version -le $current) {
        Save-State 'current' "version=$current; no update required"
        Write-Output "DEPLOY_CURRENT=$current"
        return
    }
    if ($Mode -eq 'check' -or $DryRun) {
        Save-State 'available' "current=$current; target=$($release.tag); mode=$Mode"
        Write-Output "DEPLOY_AVAILABLE=$($release.tag)"
        return
    }
    $file = Download-Release $release
    Save-State 'verified' "SHA256 verified; bytes=$($release.size); target=$($release.tag)"
    $previous = $current.ToString(3)
    $script:status.previousVersion = $previous
    Save-State 'installing' "previous=$previous; target=$($release.tag)"
    $running = @(Get-Process -Name Yanzi -ErrorAction SilentlyContinue)
    # Exit Yanzi only after the complete trusted installer has been locally staged.
    foreach ($proc in $running) {
        try { $proc.CloseMainWindow() | Out-Null } catch { }
    }
    Start-Sleep -Seconds 2
    Get-Process -Name Yanzi -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $installProcess = Start-Process -FilePath $file -ArgumentList @('--silent') -PassThru -Wait
    if ($installProcess.ExitCode -ne 0) { throw "Setup exit code: $($installProcess.ExitCode)" }
    $installed = Installed-Version
    if ($installed -ne $release.version) { throw "Installed version $installed does not match target $($release.version)" }
    Start-Yanzi
    $runningAgain = $false
    for ($i=0; $i -lt 20; $i++) {
        if (Get-Process -Name Yanzi -ErrorAction SilentlyContinue) { $runningAgain=$true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $runningAgain) { throw 'New Yanzi process did not start' }
    $script:status.installedVersion = $installed.ToString(3)
    Save-State 'verified-running' "installed=$installed; previous=$previous; process started"
    Write-Output "DEPLOY_SUCCESS=$installed"
}
catch {
    $err = $_.Exception.Message
    try { Save-State 'failed' $err } catch { }
    Write-Error $err
    exit 1
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

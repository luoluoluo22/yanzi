# Yanzi developer-channel browser helper updater (GitHub/Gitee independent of host releases).
# Only committed, remotely published extension code is eligible. Installed browser profiles are never rewritten.
param(
    [switch]$CheckOnly,
    [switch]$InstallTask,
    [switch]$NoReload
)
$ErrorActionPreference = 'Stop'
$Remote = 'https://github.com/luoluoluo22/yanzi.git'
$Branch = 'codex/opencli-xhs-extension-20261027'
$BranchRef = "refs/heads/$Branch"
$base = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$root = Join-Path $base 'BrowserHelperUpdater'
$target = Join-Path $base 'BrowserHelper'
$taskName = 'YanziBrowserHelperAutoSync'
New-Item -ItemType Directory -Path $root -Force | Out-Null

if ($InstallTask) {
    $installedScript = Join-Path $root 'sync-browser-helper.ps1'
    if ([IO.Path]::GetFullPath($PSCommandPath) -ine [IO.Path]::GetFullPath($installedScript)) {
        Copy-Item -LiteralPath $PSCommandPath -Destination $installedScript -Force
    }
    $taskCommand = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $installedScript + '"'
    & schtasks.exe /Create /F /SC MINUTE /MO 15 /TN $taskName /TR $taskCommand | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Windows task registration failed: $LASTEXITCODE" }
    Write-Output "TASK_INSTALLED=$taskName"
    Write-Output "SCRIPT=$installedScript"
    exit 0
}

function Get-TreeDigest([string]$directory) {
    $records = @(
        Get-ChildItem -LiteralPath $directory -File -Recurse |
        ForEach-Object {
            $relative = $_.FullName.Substring($directory.Length + 1).Replace('\','/')
            if ($relative -notmatch '^(scratch|node_modules|\.git)/' -and
                $relative -notmatch '^\.yanzi-' -and
                $relative -ne 'yanzi-connection.json') {
                "$relative=$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
            }
        } | Sort-Object -CaseSensitive
    )
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
        return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')
    } finally { $sha.Dispose() }
}
function Read-Version([string]$directory) {
    $manifest = Join-Path $directory 'manifest.json'
    if (!(Test-Path -LiteralPath $manifest)) { return $null }
    $json = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$json.name -notmatch 'Yanzi Browser Helper') {
        throw "Unexpected extension manifest identity: $directory"
    }
    return [version]([string]$json.version)
}
function Log-Result([string]$status, [string]$sha, [string]$details) {
    $report = [ordered]@{
        atUtc = [DateTime]::UtcNow.ToString('o')
        status = $status
        commit = $sha
        browserHelper = $target
        details = $details
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'last-result.json') -Encoding UTF8
    Write-Output "STATUS=$status COMMIT=$sha DETAIL=$details"
}
$lock = $null
$temp = $null
$sha = ''
try {
    $lockPath = Join-Path $root 'update.lock'
    $lock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $remoteLine = @(& git ls-remote --exit-code --heads $Remote $Branch)
    if ($LASTEXITCODE -ne 0 -or $remoteLine.Count -ne 1) { throw "Remote update channel unavailable: $Branch" }
    $sha = ($remoteLine[0] -split '\s+')[0].ToLowerInvariant()
    if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Remote commit SHA invalid' }
    $oldShaFile = Join-Path $target '.yanzi-source-commit'
    $oldSha = if (Test-Path $oldShaFile) { (Get-Content $oldShaFile -Raw).Trim().ToLowerInvariant() } else { '' }
    $oldDigestFile = Join-Path $target '.yanzi-source-tree-sha256'
    $oldDigest = if (Test-Path $oldDigestFile) { (Get-Content $oldDigestFile -Raw).Trim() } else { '' }
    if ($sha -eq $oldSha -and $oldDigest -match '^[A-F0-9]{64}$' -and
        (Test-Path (Join-Path $target 'manifest.json')) -and
        (Get-TreeDigest $target) -eq $oldDigest) {
        if (!$CheckOnly) { Log-Result 'up-to-date' $sha 'Trusted remote SHA and on-disk file hash match' }
        else { Write-Output "STATUS=up-to-date COMMIT=$sha" }
        exit 0
    }
    if ($CheckOnly) {
        Write-Output "STATUS=update-available REMOTE=$sha INSTALLED=$oldSha"
        exit 0
    }

    $cache = Join-Path $root 'cache.git'
    if (!(Test-Path (Join-Path $cache 'HEAD'))) {
        & git init --bare $cache | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Git cache creation failed' }
    }
    & git --git-dir=$cache -c advice.detachedHead=false fetch --quiet --no-tags $Remote $BranchRef | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Remote fetch failed' }
    $fetched = (& git --git-dir=$cache rev-parse FETCH_HEAD).Trim().ToLowerInvariant()
    if ($fetched -cne $sha) { throw 'Remote SHA changed during fetch; refusing to install' }
    if ($oldSha -match '^[0-9a-f]{40}$' -and $oldSha -ne $sha) {
        & git --git-dir=$cache cat-file -e "$oldSha^{commit}" 2>$null
        if ($LASTEXITCODE -eq 0) {
            & git --git-dir=$cache merge-base --is-ancestor $oldSha $sha
            if ($LASTEXITCODE -ne 0) { throw 'Remote channel is not fast-forward; manual review required' }
        }
    }
    $temp = Join-Path $root ('work-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $archive = Join-Path $temp 'extension.zip'
    & git --git-dir=$cache archive --format=zip -o $archive $sha browser-extension
    if ($LASTEXITCODE -ne 0) { throw 'Git extension archive failed' }
    if ((Get-Item $archive).Length -gt 20000000) { throw 'Archive exceeds 20MB policy' }
    $extracted = Join-Path $temp 'extracted'
    Expand-Archive -LiteralPath $archive -DestinationPath $extracted
    $src = Join-Path $extracted 'browser-extension'
    $incomingVersion = Read-Version $src
    if ($null -eq $incomingVersion) { throw 'Incoming extension manifest missing' }
    $installedVersion = Read-Version $target
    if ($installedVersion -and $incomingVersion -lt $installedVersion) {
        Log-Result 'skipped-downgrade' $sha "Incoming version $incomingVersion older than $installedVersion"
        exit 0
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    if (!$node) { throw 'Node.js is required for pre-install JavaScript syntax verification' }
    $files = @(Get-ChildItem -LiteralPath $src -File -Recurse | Where-Object {
        $_.FullName.Substring($src.Length + 1).Replace('\','/') -notmatch '^(scratch|node_modules|\.git)/'
    })
    foreach ($file in ($files | Where-Object { $_.Extension -eq '.js' })) {
        $null = & node --check $file.FullName 2>&1
        if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax check failed: $($file.Name)" }
    }
    $next = Join-Path $base ('BrowserHelper.pending-' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $root 'previous'
    try {
        New-Item -ItemType Directory -Path $next -Force | Out-Null
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($src.Length + 1)
            $destination = Join-Path $next $relative
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
        $connection = Join-Path $target 'yanzi-connection.json'
        if (Test-Path $connection) { Copy-Item -LiteralPath $connection -Destination (Join-Path $next 'yanzi-connection.json') }
        Set-Content -LiteralPath (Join-Path $next '.yanzi-source-commit') -Value $sha -Encoding ASCII -NoNewline
        $digest = Get-TreeDigest $next
        Set-Content -LiteralPath (Join-Path $next '.yanzi-source-tree-sha256') -Value $digest -Encoding ASCII -NoNewline
        if ((Get-TreeDigest $next) -cne $digest) { throw 'Staging verification failed' }

        # Rotate just one updater rollback copy, without touching browser profiles or cloud settings.
        if (Test-Path $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
        if (Test-Path $target) { Move-Item -LiteralPath $target -Destination $backup }
        try { Move-Item -LiteralPath $next -Destination $target }
        catch {
            if (!(Test-Path $target) -and (Test-Path $backup)) { Move-Item -LiteralPath $backup -Destination $target }
            throw
        }
        if ((Get-TreeDigest $target) -cne $digest) { throw 'Post-install hash mismatch' }
    } finally {
        if (Test-Path $next) { Remove-Item -LiteralPath $next -Recurse -Force }
    }

    $reloadState = 'pending-no-active-extension'
    if (!$NoReload) {
        try {
            $settingsPath = Join-Path $base 'appsettings.local.json'
            $settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $port = [int]$settings.AgentApiPort
            if ($port -le 0) { $port = 53919 }
            if ([string]::IsNullOrWhiteSpace([string]$settings.AgentApiToken)) { throw 'Local browser API token missing' }
            $headers = @{'X-Yanzi-Token' = [string]$settings.AgentApiToken}
            $url = "http://127.0.0.1:$port/v1/browser"
            $status = Invoke-RestMethod "$url/status" -Headers $headers -TimeoutSec 6
            if ($status.extensionConnected) {
                $connections = @($status.connections | Where-Object { $_.connectionId })
                if ($connections.Count -eq 0) { $connections = @([pscustomobject]@{ connectionId = '' }) }
                $accepted = 0
                foreach ($connection in $connections) {
                    $suffix = if ($connection.connectionId) { '?connectionId=' + [uri]::EscapeDataString([string]$connection.connectionId) } else { '' }
                    $answer = Invoke-RestMethod ($url + '/reload' + $suffix) -Method Post -Headers $headers -TimeoutSec 10
                    if ($answer.status -eq 'accepted') { $accepted++ }
                }
                Start-Sleep -Seconds 2
                $status = Invoke-RestMethod "$url/status" -Headers $headers -TimeoutSec 6
                $reloadState = if ($accepted -gt 0 -and $status.extensionConnected) { "reloaded-$accepted-connected" } else { 'reload-unconfirmed' }
            }
        } catch { $reloadState = 'reload-error: ' + $_.Exception.Message }
    }
    Log-Result 'installed' $sha "Version=$incomingVersion files=$($files.Count) digest=$digest browser=$reloadState"
}
catch {
    $errorText = $_.Exception.Message
    Log-Result 'error' $sha $errorText
    throw
}
finally {
    if ($temp -and (Test-Path $temp)) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    if ($lock) { $lock.Dispose() }
}

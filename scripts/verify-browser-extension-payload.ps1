param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$RepositoryRoot = "",
    [switch]$RequireCleanGit,
    [string]$ArchivePath = ""
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$sourceRoot = Join-Path $root 'browser-extension'
$publishRoot = Join-Path $PublishDirectory 'browser-extension'
$floorFile = Join-Path $root 'scripts\browser-extension-min-version.txt'
if (-not (Test-Path -LiteralPath $floorFile -PathType Leaf)) { throw 'Missing browser extension minimum-version policy.' }
$floorText = (Get-Content -LiteralPath $floorFile -Raw -Encoding UTF8).Trim()
try { $floor = [version]$floorText } catch { throw "Invalid minimum browser extension version: $floorText" }
$sourceManifest = Join-Path $sourceRoot 'manifest.json'
$publishManifest = Join-Path $publishRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $sourceManifest -PathType Leaf)) { throw 'Missing source browser extension manifest.' }
if (-not (Test-Path -LiteralPath $publishManifest -PathType Leaf)) { throw 'Missing bundled browser extension manifest.' }
$sourceVersionText = (Get-Content -LiteralPath $sourceManifest -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop).version
$bundleVersionText = (Get-Content -LiteralPath $publishManifest -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop).version
try { $sourceVersion = [version]$sourceVersionText } catch { throw "Invalid source extension version: $sourceVersionText" }
if ($sourceVersion -lt $floor) { throw "Browser extension downgrade blocked: source=$sourceVersionText minimum=$floorText" }
if ($bundleVersionText -ne $sourceVersionText) {
    throw "Bundled browser extension version mismatch: source=$sourceVersionText package=$bundleVersionText"
}
if ($RequireCleanGit) {
    $dirty = @(& git -C $root status --porcelain --untracked-files=normal -- browser-extension)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git extension status.' }
    if (@($dirty | Where-Object { $_ }).Count -gt 0) {
        throw "Uncommitted browser extension files cannot be released: $($dirty -join '; ')"
    }
}
$tracked = @(& git -C $root ls-files -- browser-extension)
if ($LASTEXITCODE -ne 0 -or @($tracked | Where-Object { $_ }).Count -eq 0) {
    throw 'No tracked browser extension files found.'
}
$verified = 0
$archive = $null
$zipEntries = @{}
if ($ArchivePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
    foreach ($entry in $archive.Entries) {
        $path = $entry.FullName.Replace('\','/').TrimStart('/')
        if ($path -match '(^|/)browser-extension/') {
            $suffix = $path.Substring($path.IndexOf('browser-extension/', [StringComparison]::OrdinalIgnoreCase))
            $zipEntries[$suffix] = $entry
        }
    }
}
try {
    foreach ($rel in $tracked) {
        $rel = $rel.Replace('/','\')
        $source = Join-Path $root $rel
        $payload = Join-Path $PublishDirectory $rel
        if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) { throw "Published browser extension file missing: $rel" }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $publishHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
        if ($sourceHash -ne $publishHash) { throw "Published browser extension content differs from Git source: $rel" }
        if ($archive) {
            $key = $rel.Replace('\','/')
            if (-not $zipEntries.ContainsKey($key)) { throw "Browser extension file missing from release archive: $key" }
            $stream = $zipEntries[$key].Open()
            try {
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
                finally { $sha.Dispose() }
            } finally { $stream.Dispose() }
            if ($archiveHash -ne $sourceHash) { throw "Browser extension archive bytes differ from Git source: $key" }
        }
        $verified++
    }
}
finally { if ($archive) { $archive.Dispose() } }
$kind = if ($ArchivePath) { 'ARCHIVE' } else { 'PUBLISH' }
Write-Output "BROWSER_EXTENSION_$($kind)_OK version=$sourceVersionText minimum=$floorText files=$verified"

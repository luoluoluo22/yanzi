param()
$ErrorActionPreference = 'Stop'
$extension = $PSScriptRoot
$id = Split-Path $extension -Leaf
$key = if ($id -eq 'raccoon-manager') { 'raccoon' } else { 'yanzi' }
$source = Join-Path $extension 'service'
if (!(Test-Path -LiteralPath (Join-Path $source 'package-lock.json'))) {
    throw 'Raccoon service package is missing; reinstall the extension.'
}

# The Yanzi host resolves manifest.requires (node>=22.16.0), including WinGet and
# its verified per-user portable fallback. This mini-app must not download Node itself.
$node = Get-Command node.exe -ErrorAction SilentlyContinue
if (!$node -or !(Test-Path -LiteralPath $node.Source)) {
    throw 'Node is not ready. Launch the Raccoon mini-app through Yanzi so the shared dependency provider can install Node.'
}
$nodeExe = $node.Source
$nodeDirectory = Split-Path -Parent $nodeExe
$npm = Join-Path $nodeDirectory 'npm.cmd'
if (!(Test-Path -LiteralPath $npm)) {
    throw 'Node installation is incomplete: npm.cmd not found. Repair Node through Yanzi dependency management.'
}
$versionText = (& $nodeExe --version).Trim()
if ($LASTEXITCODE -ne 0 -or $versionText -notmatch '^v(\d+\.\d+\.\d+)$' -or
    [version]$Matches[1] -lt [version]'22.16.0') {
    throw 'Raccoon requires Node >= 22.16.0. Update it through Yanzi dependency management.'
}
$env:Path = $nodeDirectory + ';' + $env:Path

$runtime = Join-Path $env:LOCALAPPDATA ("OpenQuickHost\McpRuntime\" + $key)
$target = Join-Path $runtime 'app'
New-Item -Path $target -ItemType Directory -Force | Out-Null
foreach ($entry in @('src', 'scripts', 'assets', 'test', 'pipelines',
                     'package.json', 'package-lock.json', 'README.md')) {
    $from = Join-Path $source $entry
    if (Test-Path -LiteralPath $from) {
        Copy-Item -LiteralPath $from -Destination $target -Recurse -Force
    }
}

$lock = Join-Path $source 'package-lock.json'
$hash = (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash
$stamp = Join-Path $runtime 'dependencies.sha256'
if (!(Test-Path (Join-Path $target 'node_modules\@modelcontextprotocol\sdk')) -or
    !(Test-Path -LiteralPath $stamp) -or
    (Get-Content -LiteralPath $stamp -Raw).Trim() -ne $hash) {
    Push-Location $target
    try {
        & $npm ci --omit=dev --no-audit --no-fund --silent
        if ($LASTEXITCODE -ne 0) { throw ("Dependency install failed: " + $id) }
        [IO.File]::WriteAllText($stamp, $hash, [Text.UTF8Encoding]::new($false))
    }
    finally { Pop-Location }
}
Write-Output $target

param(
    [string]$CandidateRoot = 'F:\Desktop\kaifa\OpenQuickHost-ui-v2-clean-check',
    [string]$StagingRoot = (Join-Path $env:LOCALAPPDATA 'YanziRuntime\staged')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$directory = $PSScriptRoot
$manifest = Get-Content (Join-Path $directory 'release-candidate.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$verifiedCommit = (& git -C $CandidateRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $verifiedCommit -ne $manifest.commit) {
    throw "Candidate source does not match pinned Git commit."
}
if (@(& git -C $CandidateRoot status --porcelain).Count -gt 0) {
    throw "Cannot stage a dirty candidate checkout."
}

$release = Join-Path $CandidateRoot 'src\Yanzi.Runtime\bin\Release\net9.0-windows'
$shell = Join-Path $CandidateRoot 'src\OpenQuickHost\bin\Release\net9.0-windows'
$expected = @(
    @{ path = (Join-Path $release 'Yanzi.dll'); sha = $manifest.runtimeHostAssemblySha256 },
    @{ path = (Join-Path $release 'Yanzi.Runtime.exe'); sha = $manifest.runtimeExecutableSha256 },
    @{ path = (Join-Path $release 'Yanzi.UI.Wpf.dll'); sha = $manifest.publicUiAssemblySha256 },
    @{ path = (Join-Path $shell 'Yanzi.dll'); sha = $manifest.runtimeHostAssemblySha256 },
    @{ path = (Join-Path $shell 'Yanzi.UI.Wpf.dll'); sha = $manifest.publicUiAssemblySha256 }
)
foreach ($entry in $expected) {
    if (-not (Test-Path -LiteralPath $entry.path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash -ne $entry.sha) {
        throw "Release artifact missing or changed: $($entry.path)"
    }
}

$root = [IO.Path]::GetFullPath($StagingRoot)
New-Item -ItemType Directory -Force -Path $root | Out-Null
$name = 'ui-v2-' + $manifest.commit.Substring(0, 12)
$target = Join-Path $root $name
$tmp = Join-Path $root ($name + '.tmp-' + [Guid]::NewGuid().ToString('N'))
$livePointer = Join-Path (Split-Path $root) 'runtime.json'
$pointerBefore = if (Test-Path -LiteralPath $livePointer) { (Get-FileHash $livePointer -Algorithm SHA256).Hash } else { 'MISSING' }

function Compare-Package([string]$src, [string]$dst) {
    $files = @(Get-ChildItem -LiteralPath $src -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($src.TrimEnd('\').Length).TrimStart('\')
        $targetFile = Join-Path $dst $relative
        if (-not (Test-Path -LiteralPath $targetFile -PathType Leaf) -or
            (Get-FileHash $file.FullName -Algorithm SHA256).Hash -ne
                (Get-FileHash $targetFile -Algorithm SHA256).Hash) {
            throw "Release staging mismatch: $relative"
        }
    }
    $actualCount = @(Get-ChildItem -LiteralPath $dst -Recurse -File).Count
    if ($files.Count -ne $actualCount) {
        throw "Unexpected staged files ($actualCount instead of $($files.Count))"
    }
    return $files.Count
}

if (Test-Path -LiteralPath $target) {
    $a = Compare-Package $release (Join-Path $target 'runtime')
    $b = Compare-Package $shell (Join-Path $target 'shell')
    Write-Host "STAGED_ALREADY_VERIFIED=$target"
} else {
    try {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'runtime')
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'shell')
        Copy-Item -Path (Join-Path $release '*') -Destination (Join-Path $tmp 'runtime') -Recurse -Force
        Copy-Item -Path (Join-Path $shell '*') -Destination (Join-Path $tmp 'shell') -Recurse -Force
        $a = Compare-Package $release (Join-Path $tmp 'runtime')
        $b = Compare-Package $shell (Join-Path $tmp 'shell')
        Move-Item -LiteralPath $tmp -Destination $target
        Write-Host "STAGED_VERIFIED=$target"
    } finally {
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$pointerAfter = if (Test-Path -LiteralPath $livePointer) { (Get-FileHash $livePointer -Algorithm SHA256).Hash } else { 'MISSING' }
if ($pointerBefore -ne $pointerAfter) {
    throw 'Unsafe staging: production runtime.json unexpectedly changed.'
}
Write-Host "RUNTIME_FILES=$a SHELL_FILES=$b"
Write-Host "RUNTIME_POINTER_UNCHANGED=True"
Write-Host "PRODUCTION_ACTIVATED=False"

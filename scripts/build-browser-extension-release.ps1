param(
  [string]$OutputDirectory = "",
  [switch]$SkipJavaScriptCheck
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root 'browser-extension'
$manifestFile = Join-Path $source 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.manifest_version -ne 3) { throw 'Expected a Chromium Manifest V3 extension' }
if ($manifest.name -notmatch 'Yanzi Browser Helper') { throw 'Unexpected extension name' }
if ($manifest.version -notmatch '^\d+(\.\d+){1,3}$') { throw 'Invalid extension version' }

# Explicit allowlist: never leak scratch files, local sidecars, or release notes into a store package.
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -eq '.js' -or $_.Name -eq 'manifest.json' })
$folders = @('assets', 'popup', 'webapps')
foreach ($folder in $folders) {
  $folderPath = Join-Path $source $folder
  if (-not (Test-Path -LiteralPath $folderPath -PathType Container)) { throw "Missing folder: $folder" }
  $files += @(Get-ChildItem -LiteralPath $folderPath -Recurse -File | Where-Object { $_.Extension -ne '.md' })
}
$allowed = @('.js', '.json', '.html', '.css', '.png', '.svg', '.ico', '.webp', '.jpg', '.jpeg', '.gif')
foreach ($file in $files) {
  if ($allowed -notcontains $file.Extension.ToLowerInvariant()) { throw "Unexpected package file type: $($file.FullName)" }
}
$required = @($manifest.background.service_worker, $manifest.action.default_popup)
$required += @($manifest.icons.PSObject.Properties.Value)
foreach ($entry in $required) {
  if (-not $entry -or -not (Test-Path -LiteralPath (Join-Path $source $entry) -PathType Leaf)) {
    throw "Manifest refers to missing file: $entry"
  }
}
if (-not $SkipJavaScriptCheck) {
  $node = Get-Command node -ErrorAction Stop
  foreach ($js in @($files | Where-Object Extension -eq '.js')) {
    & $node.Source --check $js.FullName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax check failed: $($js.FullName)" }
  }
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '.artifacts\browser-extension-release' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory ("Yanzi-Browser-Helper-" + $manifest.version + "-edge-chromium.zip")
$stage = Join-Path $OutputDirectory ("staging-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
  foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\','/')
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
  }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
  [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
  Write-Output "EXTENSION_VERSION=$($manifest.version)"
  Write-Output "EXTENSION_FILES=$($files.Count)"
  Write-Output "RELEASE_ZIP=$zip"
  Write-Output "SHA256=$hash"
  Write-Output 'TARGET=Edge Add-ons / Chromium (Ego unpacked install supported separately)'
} finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

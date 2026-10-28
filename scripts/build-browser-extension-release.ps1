param(
  [string]$OutputDirectory = "",
  [switch]$SkipJavaScriptCheck,
  [ValidateSet("Chromium", "Firefox")][string]$Target = "Chromium"
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
$suffix = if ($Target -eq 'Firefox') { "-firefox-review.zip" } else { "-edge-chromium.zip" }
$zip = Join-Path $OutputDirectory ("Yanzi-Browser-Helper-" + $manifest.version + $suffix)
$stage = Join-Path $OutputDirectory ("staging-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
  foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\','/')
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
  }
  if ($Target -eq 'Firefox') {
    # Preserve the ORIGINAL importScripts execution order: bootstrap dependencies,
    # background main body, then post-bootstrap observers. Loading observers first
    # breaks the global yanziBrowserHost singleton in a Firefox event page.
    $backgroundPath = Join-Path $stage 'background.js'
    $backgroundSource = [IO.File]::ReadAllText($backgroundPath, [Text.Encoding]::UTF8)
    $imports = [regex]::Matches($backgroundSource, '(?m)^importScripts[^\r\n]*\r?$')
    if ($imports.Count -ne 2) {
      throw "Expected exactly 2 Chromium importScripts calls, found $($imports.Count)"
    }
    $mainStart = $imports[0].Index + $imports[0].Length
    $mainBody = $backgroundSource.Substring($mainStart, $imports[1].Index - $mainStart)
    $afterStart = $imports[1].Index + $imports[1].Length
    $bootBody = $backgroundSource.Substring($afterStart)
    $mainName = 'firefox-background-main.js'
    $bootName = 'firefox-background-boot.js'
    [IO.File]::WriteAllText((Join-Path $stage $mainName), $mainBody, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText((Join-Path $stage $bootName), $bootBody, (New-Object Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $backgroundPath -Force
    $scriptNames = @(
      'lanzou-network-observer.js', 'workflow-checkpoints.js', 'workflow-orchestrator.js',
      $mainName,
      'chatgpt-network-observer.js', 'chatgpt-background.js',
      'gemini-network-observer.js', 'gemini-background.js',
      'webapps/runtime-background.js', 'opencli-background.js',
      $bootName
    )
    foreach ($entry in $scriptNames) {
      if (-not (Test-Path -LiteralPath (Join-Path $stage $entry) -PathType Leaf)) {
        throw "Firefox background dependency missing: $entry"
      }
    }
    if (-not $SkipJavaScriptCheck) {
      & $node.Source --check (Join-Path $stage $mainName)
      if ($LASTEXITCODE -ne 0) { throw 'Firefox main background script failed syntax check' }
      & $node.Source --check (Join-Path $stage $bootName)
      if ($LASTEXITCODE -ne 0) { throw 'Firefox boot background script failed syntax check' }
    }
    $firefoxManifest = Get-Content -LiteralPath (Join-Path $stage 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $firefoxManifest.background = @{ scripts = $scriptNames }
    # AMO requires accurate built-in data-collection disclosure. This helper
    # sends requested website text, browsing metadata and AI conversations
    # to the user's local Yanzi desktop app, not to a remote store backend.
    $firefoxManifest.PSObject.Properties.Remove('minimum_chrome_version')
    $firefoxManifest | Add-Member -NotePropertyName 'browser_specific_settings' -NotePropertyValue @{
      gecko = @{
        id = 'yanzi-browser-helper@openquickhost.example'
        strict_min_version = '140.0'
        data_collection_permissions = @{
          required = @('websiteContent', 'browsingActivity', 'personalCommunications')
        }
      }
    } -Force
    $firefoxManifest | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
  }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
  [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
  Write-Output "EXTENSION_VERSION=$($manifest.version)"
  Write-Output "EXTENSION_FILES=$($files.Count)"
  Write-Output "RELEASE_ZIP=$zip"
  Write-Output "SHA256=$hash"
  if ($Target -eq 'Firefox') {
    Write-Output 'TARGET=Firefox AMO review ZIP (unsigned; Firefox QA and AMO signing required)'
  } else {
    Write-Output 'TARGET=Edge Add-ons / Chromium (Ego unpacked install supported separately)'
  }
} finally {
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}

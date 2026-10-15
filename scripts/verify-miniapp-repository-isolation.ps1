param([switch]$CheckInstalled)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$files = @(git -C $repo ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked repository files.' }

# Browser extension and reference fixture are not Windows user miniapps.
$allowedManifests = @(
    'browser-extension/manifest.json',
    'docs/yanzi-ui-official-catalog/reference-package/manifest.json'
)
$unexpected = @($files | Where-Object {
    $_.EndsWith('/manifest.json', [StringComparison]::OrdinalIgnoreCase) -and
    $allowedManifests -notcontains $_.Replace('\','/')
})
if ($unexpected.Count -gt 0) {
    throw ('Miniapp manifests are stored in the host Git repository: ' + ($unexpected -join ', '))
}
$bundledDirs = @($files | Where-Object {
    $_ -match '^extensions/' -or
    $_ -match '^website/store-packages/' -or
    $_ -match '^tools/idle-chatgpt-tasks/' -or
    $_ -match '^tools/android-automation/(extension|recovery)/' -or
    $_ -match '^src/Yanzi\.Capture/Extension/'
})
if ($bundledDirs.Count -gt 0) {
    throw ('Host repository includes miniapp packages: ' + ($bundledDirs -join ', '))
}
Write-Output 'MINIAPP_REPO_ISOLATION_PASS: host Git tree contains no installed miniapp package'

if ($CheckInstalled) {
    $root = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
    $required = @(
        'raccoon-manager','video-resume','lanzou-cloud',
        'ext_e9e37068c8284b1e808f5454181fb418',
        'capability-lab','idle-chatgpt-tasks',
        'yanzi-android-automation','yanzi-capture',
        'open-yanzi-github','open-yanzi-homepage','vpn-manager'
    )
    foreach ($id in $required) {
        $manifest = Join-Path (Join-Path $root $id) 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifest)) { throw ('Missing user miniapp: ' + $id) }
        $actualId = (Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json).id
        if ($actualId -ne $id) { throw ('User miniapp ID mismatch: ' + $id) }
    }
    Write-Output ('MINIAPP_USER_DIRECTORY_PASS: ' + $required.Count + ' migrated/current miniapps found')
}

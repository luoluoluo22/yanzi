$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$images = @('json-extension-editor.png', 'launcher-and-quick-panel.png',
    'query-prefix-preview.png', 'quick-panel-grid.png', 'readme-cover-16x9.png')
foreach ($image in $images) {
    if (Test-Path (Join-Path $root $image)) { throw "Screenshot remains at root: $image" }
    if (!(Test-Path (Join-Path $root "docs/assets/screenshots/$image"))) { throw "Missing screenshot: $image" }
}
$readme = Get-Content (Join-Path $root 'README.md') -Raw -Encoding UTF8
foreach ($match in [regex]::Matches($readme, '!\[[^\]]*\]\(([^)]+)\)')) {
    $target = $match.Groups[1].Value
    if ($target -notmatch '^https?://' -and !(Test-Path (Join-Path $root $target))) {
        throw "Broken README image: $target"
    }
}
foreach ($script in @('scripts/build-copyright-documents.ps1', 'scripts/generate_copyright_pdf.py')) {
    $content = Get-Content (Join-Path $root $script) -Raw -Encoding UTF8
    foreach ($image in $images) {
        if ($content.Contains($image) -and $content -match ('(?:Get-Base64Image "|os.path.join\(ROOT, ")' + [regex]::Escape($image))) {
            throw "Old screenshot reference in $script"
        }
    }
}
foreach ($relative in @('docs/development/local-commands.md', 'docs/ui-unification/task-progress-design-qa-2026-10-07.md', 'scripts/create_app_bundle.sh')) {
    if (!(Test-Path (Join-Path $root $relative))) { throw "Missing moved file: $relative" }
}
$parseErrors = $null
$tokens = $null
[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'scripts/build-copyright-documents.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Copyright script parsing failed.' }
& powershell -NoProfile -File (Join-Path $root 'scripts/verify-repository-artifact-cleanup.ps1')
if ($LASTEXITCODE) { throw 'Artifact hygiene failed.' }
Write-Output 'Repository layout verified.'

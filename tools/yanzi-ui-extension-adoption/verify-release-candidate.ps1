param(
    [string]$CandidateRoot = 'F:\Desktop\kaifa\OpenQuickHost-ui-v2-clean-check',
    [switch]$SkipRuntime
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'release-candidate.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not (Test-Path $CandidateRoot)) { throw "Candidate checkout unavailable: $CandidateRoot" }
$commit = (& git -C $CandidateRoot rev-parse HEAD)
if ($commit -ne $manifest.commit) { throw "Candidate commit changed: $commit" }
if (@(& git -C $CandidateRoot status --porcelain).Count -gt 0) {
    throw 'Candidate checkout has uncommitted work; cannot assert isolated release.'
}
$root = Join-Path $CandidateRoot 'src\Yanzi.Runtime\bin\Release\net9.0-windows'
$checks = @(
    @('Yanzi.dll', $manifest.runtimeHostAssemblySha256),
    @('Yanzi.Runtime.exe', $manifest.runtimeExecutableSha256),
    @('Yanzi.UI.Wpf.dll', $manifest.publicUiAssemblySha256)
)
foreach ($entry in $checks) {
    $file = Join-Path $root $entry[0]
    if (-not (Test-Path $file)) { throw "Missing candidate artifact: $file" }
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $entry[1]) {
        throw "Candidate artifact hash mismatch: $file"
    }
    Write-Host "MATCH_SHA256=$($entry[0])"
}
Push-Location $CandidateRoot
try {
    $compiler = Join-Path $CandidateRoot 'tools\yanzi-ui-extension-adoption\CompilerVerification\bin\Release\net9.0-windows\CompilerVerification.exe'
    if (-not (Test-Path $compiler)) { throw 'Clean compiler verification has not been built.' }
    & $compiler
    if ($LASTEXITCODE -ne 0) { throw 'Clean candidate dynamic extension compile failed' }
    if (-not $SkipRuntime) {
        $verifier = Join-Path $CandidateRoot 'src\Yanzi.RuntimeVerification\bin\Release\net9.0-windows\Yanzi.RuntimeVerification.exe'
        if (-not (Test-Path $verifier)) { throw 'Clean runtime verification has not been built.' }
        & $verifier (Join-Path $root 'Yanzi.Runtime.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Clean candidate runtime lifecycle regression failed' }
    }
}
finally { Pop-Location }
Write-Host 'PASS: verified clean public UI release candidate'

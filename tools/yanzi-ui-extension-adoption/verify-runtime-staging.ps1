param([string]$CandidateRoot = 'F:\Desktop\kaifa\OpenQuickHost-ui-v2-clean-check')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $env:TEMP ('yanzi-runtime-staging-check-' + [Guid]::NewGuid().ToString('N'))
$pointer = Join-Path $fixture 'runtime.json'
$originalPointer = '{"protocolVersion":1,"executable":"DO_NOT_CHANGE_EXISTING_RUNTIME"}'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
[IO.File]::WriteAllText($pointer, $originalPointer)
try {
    & (Join-Path $repo 'scripts\install-shared-runtime.ps1') -SkipBuild -ProjectRoot $CandidateRoot -InstallationRoot $fixture
    # PowerShell-only staging does not set LASTEXITCODE; terminating errors propagate.
    if ([IO.File]::ReadAllText($pointer) -cne $originalPointer) {
        throw 'Unsafe installer: it modified the live runtime pointer without -Activate'
    }
    $version = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'versions') -Directory)
    $shell = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'shells') -Directory)
    if ($version.Count -ne 1 -or $shell.Count -ne 1) {
        throw 'Expected exactly one staged Runtime and Shell version'
    }
    $manifest = Get-Content (Join-Path $PSScriptRoot 'release-candidate.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $runtimeDir = $version[0].FullName
    $shellDir = $shell[0].FullName
    foreach ($entry in @(
        @('Yanzi.dll', $runtimeDir, $manifest.runtimeHostAssemblySha256),
        @('Yanzi.Runtime.exe', $runtimeDir, $manifest.runtimeExecutableSha256),
        @('Yanzi.UI.Wpf.dll', $runtimeDir, $manifest.publicUiAssemblySha256),
        @('Yanzi.dll', $shellDir, $manifest.runtimeHostAssemblySha256),
        @('Yanzi.UI.Wpf.dll', $shellDir, $manifest.publicUiAssemblySha256)
    )) {
        $artifact = Join-Path $entry[1] $entry[0]
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf) -or
            (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash -ne $entry[2]) {
            throw "Staged artifact hash mismatch: $artifact"
        }
    }
    Write-Host 'PASS: inactive Runtime installation preserves existing runtime.json exactly'
    Write-Host 'PASS: Runtime/Shell snapshots staged with pinned release checksums'
}
finally { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }

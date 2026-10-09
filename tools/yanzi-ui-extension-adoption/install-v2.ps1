param(
    [string]$ExtensionsRoot = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'),
    [switch]$Activate,
    [string]$VerifiedHostAssembly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$manifest = Get-Content (Join-Path $PSScriptRoot 'source-hashes.v2.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding($false)
if ($Activate) {
    if (-not $VerifiedHostAssembly -or -not (Test-Path $VerifiedHostAssembly -PathType Leaf)) {
        throw 'Activation requires a verified new-host assembly; use staging until deployment.'
    }
    $testedHost = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\src\OpenQuickHost\bin\Release\net9.0-windows\Yanzi.dll'))
    if (-not (Test-Path $testedHost) -or
        (Get-FileHash $testedHost -Algorithm SHA256).Hash -ne (Get-FileHash $VerifiedHostAssembly -Algorithm SHA256).Hash) {
        throw 'The installed host differs from the tested direct-UI compiler. Activation refused.'
    }
    $productionExtensions = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions')).TrimEnd('\')
    $targetExtensions = [IO.Path]::GetFullPath($ExtensionsRoot).TrimEnd('\')
    if ($targetExtensions.Equals($productionExtensions, [StringComparison]::OrdinalIgnoreCase)) {
        $running = Get-CimInstance Win32_Process -Filter "name='Yanzi.Runtime.exe'" |
            Where-Object { $_.CommandLine -match '--runtime --tray|--runtime' } |
            Select-Object -First 1
        if (-not $running) { throw 'Cannot identify the running production Runtime host.' }
        $loadedHostDll = Join-Path (Split-Path $running.ExecutablePath) 'Yanzi.dll'
        if (-not (Test-Path $loadedHostDll) -or
            (Get-FileHash $loadedHostDll -Algorithm SHA256).Hash -ne
            (Get-FileHash $VerifiedHostAssembly -Algorithm SHA256).Hash) {
            throw 'Production Runtime is not running the verified new UI-enabled compiler. Activation refused.'
        }
    }
}
foreach ($name in @('semantic-search', 'capability-lab')) {
    $record = $manifest.$name
    $folder = Join-Path $ExtensionsRoot $name
    $source = Join-Path $folder $record.entry
    if (-not (Test-Path $source -PathType Leaf)) { throw "Missing extension source: $source" }
    $sha = (Get-FileHash $source -Algorithm SHA256).Hash
    if ($sha -eq $record.optimizedSha256) { Write-Host "ALREADY_OPTIMIZED=$name"; continue }
    if ($sha -ne $record.previousSha256) { throw "Unrecognized $name source SHA256=$sha. Refusing to overwrite changes." }
    $temp = Join-Path $env:TEMP ("yanzi-ui-v2-" + [guid]::NewGuid().ToString('N'))
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    try {
        $candidate = Join-Path $temp $record.entry
        Copy-Item $source $candidate
        $patch = Join-Path $PSScriptRoot $record.upgradePatch
        & git -C $temp apply --check $patch
        if ($LASTEXITCODE -ne 0) { throw "Patch not applicable: $name" }
        & git -C $temp apply $patch
        if ($LASTEXITCODE -ne 0) { throw "Patch failed: $name" }
        $canonical = [IO.File]::ReadAllText($candidate).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($candidate, $canonical, $utf8)
        if ((Get-FileHash $candidate -Algorithm SHA256).Hash -ne $record.optimizedSha256) {
            throw "Candidate SHA256 mismatch: $name"
        }
        if ($Activate) {
            $backup = $source + '.before-direct-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
            Copy-Item $source $backup
            try {
                Copy-Item $candidate $source -Force
                if ((Get-FileHash $source -Algorithm SHA256).Hash -ne $record.optimizedSha256) { throw 'Installed hash mismatch' }
                Write-Host "ACTIVATED=$name BACKUP=$backup"
            }
            catch {
                Copy-Item $backup $source -Force
                throw
            }
        }
        else {
            $stage = $source + '.ui-v2-pending'
            Copy-Item $candidate $stage -Force
            Write-Host "STAGED_ONLY=$name PENDING=$stage"
        }
    }
    finally { Remove-Item $temp -Force -Recurse -ErrorAction SilentlyContinue }
}
if (-not $Activate) { Write-Host 'PRODUCTION_UNCHANGED=True; activate only after verified host deployment.' }
$global:LASTEXITCODE = 0

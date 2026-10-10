[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Runtimes\node'),
    [ValidateSet('x64','arm64')][string]$Architecture = $(if ([Environment]::Is64BitOperatingSystem -and $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    [int]$Major = 24,
    [switch]$ForcePortable
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$minimum = [version]'22.16.0'

function Test-Node([string]$Exe) {
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $Exe -Parent) 'npm.cmd'))) { return $false }
    try {
        $v = (& $Exe --version 2>$null).Trim()
        if ($LASTEXITCODE -ne 0 -or $v -notmatch '^v(\d+\.\d+\.\d+)$') { return $false }
        return ([version]$Matches[1] -ge $minimum)
    } catch { return $false }
}
function Find-Portable {
    if (-not (Test-Path -LiteralPath $InstallRoot)) { return $null }
    $candidates = @(Get-ChildItem -LiteralPath $InstallRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' } |
        Sort-Object { [version]($_.Name.Substring(1)) } -Descending)
    foreach ($item in $candidates) {
        $exe = Join-Path $item.FullName 'node.exe'
        if (Test-Node $exe) { return $exe }
    }
    return $null
}
$node = Find-Portable
if ($node) { Write-Output $node; return }
if (-not $ForcePortable) {
    $system = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($system -and (Test-Node $system.Source)) { Write-Output $system.Source; return }
}
if ($Major -ne 24) { throw 'Only the tested Node 24 LTS runtime is supported for automatic installation.' }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Raccoon requires a 64-bit Windows computer.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
$mutex = New-Object Threading.Mutex($false, 'Local\YanziRaccoonNodeProvision')
$locked = $false
try {
    $locked = $mutex.WaitOne([TimeSpan]::FromMinutes(5))
    if (-not $locked) { throw 'Node installation is busy on this computer.' }
    $node = Find-Portable
    if ($node) { Write-Output $node; return }

    $index = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' -TimeoutSec 45 -UseBasicParsing
    $platform = "win-$Architecture-zip"
    $release = @($index | Where-Object {
        $_.version -match '^v24\.\d+\.\d+$' -and $_.lts -ne $false -and $_.files -contains $platform
    } | Sort-Object { [version]($_.version.Substring(1)) } -Descending | Select-Object -First 1)
    if ($release.Count -ne 1) { throw "No compatible Node 24 LTS $Architecture archive was published." }
    $version = $release[0].version
    $name = "node-$version-win-$Architecture.zip"
    $baseUrl = "https://nodejs.org/dist/$version"
    $sumText = (Invoke-WebRequest -Uri "$baseUrl/SHASUMS256.txt" -TimeoutSec 45 -UseBasicParsing).Content
    if ($sumText -is [byte[]]) { $sumText = [Text.Encoding]::UTF8.GetString($sumText) }
    $expected = $null
    foreach ($line in ($sumText -split '\r?\n')) {
        $parts = [regex]::Split($line.Trim(), '\s+')
        if ($parts.Count -eq 2 -and $parts[1] -ceq $name -and $parts[0] -match '^[a-fA-F0-9]{64}$') {
            $expected = $parts[0]; break
        }
    }
    if (-not $expected) { throw "Official SHA-256 not found for $name." }
    $scratch = Join-Path $InstallRoot ('.install-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    try {
        $archive = Join-Path $scratch $name
        Invoke-WebRequest -Uri "$baseUrl/$name" -OutFile $archive -TimeoutSec 180 -UseBasicParsing
        $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        if ($actual -ine $expected) { throw 'Node archive SHA-256 verification failed; installation rejected.' }
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $scratch 'unpacked') -Force
        $unpacked = Join-Path (Join-Path $scratch 'unpacked') "node-$version-win-$Architecture"
        if (-not (Test-Node (Join-Path $unpacked 'node.exe'))) { throw 'Downloaded Node/npm failed runtime validation.' }
        $target = Join-Path $InstallRoot $version
        if (-not (Test-Path $target)) { Move-Item -LiteralPath $unpacked -Destination $target }
        $node = Join-Path $target 'node.exe'
        if (-not (Test-Node $node)) { throw 'Installed Node runtime failed validation.' }
        Write-Output $node
    } finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

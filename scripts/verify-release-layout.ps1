param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [switch]$CheckInstalled,
    [string]$ExpectedVersion = ""
)
$ErrorActionPreference = 'Stop'
$dir = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "Missing release payload: $dir" }
foreach ($name in @('Yanzi.exe', 'Yanzi.dll', 'Yanzi.deps.json', 'Yanzi.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $dir $name) -PathType Leaf)) { throw "Release payload missing: $name" }
}
if (Test-Path (Join-Path $dir 'Runtime')) { throw 'Duplicate Runtime directory detected in release payload.' }
foreach ($name in @('Yanzi.Runtime.exe', 'Yanzi.Runtime.dll', 'Yanzi.Runtime.deps.json', 'Yanzi.Runtime.runtimeconfig.json')) {
    if (Test-Path -LiteralPath (Join-Path $dir $name)) { throw "Single-process release must not ship: $name" }
}
$app = Get-Item -LiteralPath (Join-Path $dir 'Yanzi.exe')
$hostVersion = $app.VersionInfo.FileVersion
if ($ExpectedVersion -and ($hostVersion -notlike "$ExpectedVersion*")) { throw "Unexpected host version $hostVersion, expected $ExpectedVersion" }
$files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force)
$miB = [math]::Round((($files | Measure-Object -Property Length -Sum).Sum) / 1MB, 2)
Write-Output "RELEASE_PAYLOAD_OK version=$hostVersion files=$($files.Count) MiB=$miB"
if ($CheckInstalled) {
    $official = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Yanzi\current\Yanzi.exe'))
    if (-not $app.FullName.Equals($official, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Post-install verification requires the official current installation directory.'
    }
    $runKey = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    $expectedStartup = '"' + $official + '" --tray'
    if (($runKey.Yanzi -and $runKey.Yanzi -ne $expectedStartup) -or $runKey.'Yanzi.Runtime') {
        throw "Startup registry points to stale or duplicate versions. Expected $expectedStartup"
    }
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Yanzi.exe' OR Name = 'Yanzi.Runtime.exe'")
    $shell = @($processes | Where-Object {
        $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath).Equals($official, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CommandLine -notmatch '(?i)(?:^|\s)--runtime(?:\s|$)'
    })
    if ($shell.Count -ne 1) { throw "Expected one installed Shell, found $($shell.Count)." }
    $background = @($processes | Where-Object { $_.Name -eq 'Yanzi.Runtime.exe' })
    if ($background.Count -ne 0) { throw "Legacy Runtime still running: $($background.Count)." }
    Write-Output "INSTALLED_RELEASE_OK pid=$($shell[0].ProcessId) version=$hostVersion"
}

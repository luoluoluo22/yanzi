#Requires -Version 5.1
param([switch]$RunBuild)
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$loop = Join-Path $PSScriptRoot "dev-desktop-loop.ps1"
$hostSource = Join-Path $root "src\OpenQuickHost\MainWindow.Extensions.cs"
$parseErrors = $null
$tokens = $null
[void][Management.Automation.Language.Parser]::ParseFile($loop, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw "Development loop PowerShell parsing failed." }
$script = Get-Content -LiteralPath $loop -Raw -Encoding UTF8
$source = Get-Content -LiteralPath $hostSource -Raw -Encoding UTF8
foreach ($required in @(
    '-p:SkipStopRunningApp=true',
    '-p:OutputPath=',
    '$BuildOnly',
    'Desktop staging build failed',
    'stop-desktop-build-process.ps1'
)) {
    if (-not $script.Contains($required)) { throw "Missing staging contract: $required" }
}
if ($script.IndexOf('stop-desktop-build-process.ps1') -lt $script.IndexOf('& dotnet @buildArgs')) {
    throw "Process shutdown must only be scheduled after staging build."
}
foreach ($required in @(
    'DispatcherPriority.Background',
    'await launch.Task.Unwrap()',
    'await Task.Delay(150)',
    'StartExtensionIdleTriggerService'
)) {
    if (-not $source.Contains($required)) { throw "Missing startup scheduling contract: $required" }
}
if ($RunBuild) {
    $before = @(Get-CimInstance Win32_Process -Filter "Name='Yanzi.exe'" |
        Where-Object { $_.ExecutablePath -like '*\AppData\Local\Yanzi\current\Yanzi.exe' } |
        Select-Object -ExpandProperty ProcessId)
    & powershell -NoProfile -ExecutionPolicy Bypass -File $loop -BuildOnly
    if ($LASTEXITCODE -ne 0) { throw "Staged build validation failed." }
    $after = @(Get-CimInstance Win32_Process -Filter "Name='Yanzi.exe'" |
        Where-Object { $_.ExecutablePath -like '*\AppData\Local\Yanzi\current\Yanzi.exe' } |
        Select-Object -ExpandProperty ProcessId)
    if (($before -join ',') -ne ($after -join ',')) {
        throw "Installed Yanzi process changed during BuildOnly."
    }
}
Write-Output 'YANZI_UPDATE_STARTUP_SMOOTH_VERIFIED'

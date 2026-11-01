#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceFile = Join-Path $repoRoot 'src\OpenQuickHost\MainWindow.Extensions.cs'
$source = [System.IO.File]::ReadAllText($sourceFile)
$begin = $source.IndexOf('private void Window_Deactivated(', [StringComparison]::Ordinal)
$end = $source.IndexOf('private void MainWindow_Activated(', [StringComparison]::Ordinal)
if ($begin -lt 0 -or $end -le $begin) {
    throw 'Window_Deactivated handler could not be located.'
}
$handler = $source.Substring($begin, $end - $begin)

$requiredChecks = @(
    'IsAiChatMode',
    'IsAutoHideSuppressed()',
    'Win32Native.GetForegroundWindow()',
    'foregroundWindow != new WindowInteropHelper(this).Handle',
    'WindowSensorHelper.IsCurrentProcessWindow(foregroundWindow)',
    'OwnedWindows.OfType<Window>()',
    'HideToTray()'
)
foreach ($check in $requiredChecks) {
    if (-not $handler.Contains($check)) {
        throw "Warehouse focus regression: missing '$check'."
    }
}

$foregroundCheck = $handler.IndexOf('WindowSensorHelper.IsCurrentProcessWindow(foregroundWindow)', [StringComparison]::Ordinal)
$ownedCheck = $handler.IndexOf('OwnedWindows.OfType<Window>()', [StringComparison]::Ordinal)
$hide = $handler.LastIndexOf('HideToTray()', [StringComparison]::Ordinal)
if ($foregroundCheck -gt $ownedCheck -or $ownedCheck -gt $hide) {
    throw 'Warehouse internal-window guard must run before HideToTray.'
}
Write-Host 'PASS: internal native-window focus is preserved before warehouse auto-hide.'

$project = Join-Path $repoRoot 'src\OpenQuickHost\OpenQuickHost.csproj'
& dotnet build $project -c Debug --no-restore -m:1 -v:quiet
if ($LASTEXITCODE -ne 0) {
    throw "OpenQuickHost build failed with exit code $LASTEXITCODE."
}
Write-Host 'PASS: OpenQuickHost debug build.'

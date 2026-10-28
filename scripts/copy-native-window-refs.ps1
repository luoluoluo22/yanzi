param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$candidateDirs = @()

$packRoot = Join-Path $env:ProgramFiles 'dotnet\packs\Microsoft.WindowsDesktop.App.Ref'
if (Test-Path $packRoot) {
    $candidateDirs += Get-ChildItem $packRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object {
            $dir = $_.FullName
            @('net9.0', 'net8.0', 'net6.0') | ForEach-Object {
                Join-Path $dir ("ref\" + $_)
            }
        } |
        Where-Object { Test-Path $_ }
}

$sharedRoot = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
if (Test-Path $sharedRoot) {
    $candidateDirs += Get-ChildItem $sharedRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { $_.FullName }
}

$sourceDir = $candidateDirs | Select-Object -First 1
if (-not $sourceDir) {
    throw 'No WindowsDesktop reference source found.'
}

foreach ($file in @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'System.Xaml.dll', 'System.Windows.Forms.dll')) {
    Copy-Item (Join-Path $sourceDir $file) $OutputDir -Force
}

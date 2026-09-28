$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\Yanzi.Capture\Yanzi.Capture.csproj"
$templateManifest = Join-Path $repo "src\Yanzi.Capture\Extension\manifest.json"
$extensionRoot = Join-Path $env:LOCALAPPDATA "OpenQuickHost\Extensions\yanzi-capture"
$installedManifest = Join-Path $extensionRoot "manifest.json"
$outputRoot = Join-Path $repo "src\Yanzi.Capture\bin\Release\net9.0-windows10.0.19041.0"

New-Item -ItemType Directory -Path $extensionRoot -Force | Out-Null

$existingShortcut = $null
if (Test-Path $installedManifest) {
    try {
        $existing = Get-Content $installedManifest -Raw -Encoding UTF8 | ConvertFrom-Json
        $existingShortcut = $existing.globalShortcut
    } catch {}
}

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "Yanzi.Capture class-library build failed." }

$sdkRefRoot = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.net.ref") -Directory -ErrorAction Stop | Sort-Object Name -Descending | Select-Object -First 1
$sdkLib = Join-Path $sdkRefRoot.FullName "lib\net8.0"
$artifacts = @{
    "Yanzi.Capture.dll" = (Join-Path $outputRoot "Yanzi.Capture.dll")
    "Microsoft.Windows.SDK.NET.dll" = (Join-Path $sdkLib "Microsoft.Windows.SDK.NET.dll")
    "WinRT.Runtime.dll" = (Join-Path $sdkLib "WinRT.Runtime.dll")
}
foreach ($pair in $artifacts.GetEnumerator()) {
    if (-not (Test-Path $pair.Value)) { throw "Missing capture extension artifact: $($pair.Value)" }
}

$stage = Join-Path $env:TEMP ("yanzi-capture-stage-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    foreach ($pair in $artifacts.GetEnumerator()) {
        Copy-Item $pair.Value (Join-Path $stage $pair.Key) -Force
    }
    Copy-Item $templateManifest (Join-Path $stage "manifest.json") -Force

    if (-not [string]::IsNullOrWhiteSpace($existingShortcut)) {
        $manifest = Get-Content (Join-Path $stage "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest | Add-Member -NotePropertyName "globalShortcut" -NotePropertyValue $existingShortcut -Force
        $json = $manifest | ConvertTo-Json -Depth 30
        [IO.File]::WriteAllText((Join-Path $stage "manifest.json"), $json, [Text.UTF8Encoding]::new($false))
    }

    foreach ($legacyName in @("runtime", "main.cs", "Yanzi.Capture.exe", "Yanzi.Capture.deps.json", "Yanzi.Capture.runtimeconfig.json", ".yanzi-csharp-cache")) {
        $legacy = Join-Path $extensionRoot $legacyName
        if (Test-Path $legacy) { Remove-Item $legacy -Recurse -Force }
    }

    foreach ($file in @("Yanzi.Capture.dll", "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll")) {
        Copy-Item (Join-Path $stage $file) (Join-Path $extensionRoot $file) -Force
    }
    Copy-Item (Join-Path $stage "manifest.json") $installedManifest -Force
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "Installed in-process yanzi-capture extension:"
Write-Host "  $extensionRoot"
Get-ChildItem $extensionRoot -File | Select-Object Name, @{N="KB";E={[math]::Round($_.Length / 1KB, 1)}} | Format-Table -AutoSize
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\yanzi-android-automation'
if (Test-Path $target) {
 if (-not $Force) { throw 'Extension already exists. Stop it and retry with -Force to update.' }
 $backup = "$target.backup-$(Get-Date -Format yyyyMMddHHmmss)"
 Copy-Item $target $backup -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $target 'automation\src') -Force | Out-Null
Copy-Item (Join-Path $here 'extension\manifest.json') (Join-Path $target 'manifest.json') -Force
Copy-Item (Join-Path $here 'extension\provider.cs') (Join-Path $target 'provider.cs') -Force
Copy-Item (Join-Path $here 'src\*.mjs') (Join-Path $target 'automation\src\') -Force
Write-Output "Installed extension to $target"

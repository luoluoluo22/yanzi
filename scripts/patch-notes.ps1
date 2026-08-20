$p = (Join-Path $env:USERPROFILE '.nuget\packages\velopack\1.2.0\lib\net472\Velopack.dll')
$asm = [System.Reflection.Assembly]::LoadFrom($p)
$logType = $asm.GetType('Velopack.Logging.IVelopackLogger')
if (-not $logType) { $logType = $asm.GetType('Velopack.Logging.ILogger') }
Write-Host "=== Logger Type: $($logType.FullName) ==="
$logType.GetMethods() | ForEach-Object { "$($_.Name) -> $($_.ToString())" }

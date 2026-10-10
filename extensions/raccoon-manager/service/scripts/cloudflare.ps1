param([ValidateSet('start','stop','status')][string]$Action = 'status', [switch]$Recover)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$runtimeDir = if ($env:RACCOON_RUNTIME_DIR) { $env:RACCOON_RUNTIME_DIR } else { Join-Path $projectDir '.raccoon-runtime' }
$pidFile = Join-Path $runtimeDir 'cloudflare-process.json'
$binary = Join-Path $runtimeDir 'tools\tunnel-client\cloudflared.exe'
$tokenFile = Join-Path $runtimeDir 'cloudflare-tunnel-token.txt'
$healthFile = Join-Path $runtimeDir 'cloudflare-recovery.json'
function Test-TunnelReady {
  try {
    $ready = Invoke-RestMethod -Uri 'http://127.0.0.1:20241/ready' -TimeoutSec 4
    return ($ready.readyConnections -gt 0)
  } catch { return $false }
}
function Get-TunnelProcess {
  if (-not (Test-Path -LiteralPath $pidFile)) { return $null }
  $saved = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
  $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($saved.pid)"
  if ($process -and $process.ExecutablePath -eq $binary -and $process.CommandLine.Contains($tokenFile)) { return $process }
  return $null
}
$running = Get-TunnelProcess
if ($Action -eq 'start') {
  if (Test-Path -LiteralPath (Join-Path $runtimeDir 'tunnel-paused')) { Remove-Item -LiteralPath (Join-Path $runtimeDir 'tunnel-paused') }
  if ($running) {
    if (Test-TunnelReady) {
      if (Test-Path -LiteralPath $healthFile) { Remove-Item -LiteralPath $healthFile }
      Write-Output "Cloudflare connected: PID $($running.ProcessId)"; exit 0
    }
    $now = Get-Date
    $firstFailure = $now
    if (Test-Path -LiteralPath $healthFile) {
      try {
        $state = Get-Content -LiteralPath $healthFile -Raw | ConvertFrom-Json
        if ($state.pid -eq $running.ProcessId) { $firstFailure = [DateTime]::Parse($state.firstFailure) }
      } catch {}
    }
    @{ pid = $running.ProcessId; firstFailure = $firstFailure.ToString('o'); checkedAt = $now.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $healthFile -Encoding utf8
    if ($Recover -and ($now - $firstFailure).TotalSeconds -lt 120) {
      Write-Output 'Cloudflare has no connected edges; waiting for its reconnect attempts.'; exit 0
    }
    $stamp = $now.ToString('yyyyMMdd-HHmmss')
    Stop-Process -Id $running.ProcessId -Force
    foreach ($log in @('cloudflare-stderr.log','cloudflare-stdout.log')) {
      $path = Join-Path $runtimeDir $log
      if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $runtimeDir "$stamp.$log") }
    }
    "$(Get-Date -Format o) Reconnecting Cloudflare after zero ready edges; previous PID $($running.ProcessId)." | Add-Content -LiteralPath (Join-Path $runtimeDir 'startup.log')
    Write-Output 'Restarting disconnected Cloudflare connector; MCP remains running.'
  }
  if (-not (Test-Path -LiteralPath $binary)) { throw 'cloudflared.exe missing. Install the verified tunnel-client tools first.' }
  if (-not (Test-Path -LiteralPath $tokenFile)) { throw 'Run scripts/cloudflare-setup.js first.' }
  $process = Start-Process -FilePath $binary -ArgumentList @('tunnel','--no-autoupdate','--protocol','http2','run','--token-file',"`"$tokenFile`"") -WorkingDirectory $projectDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtimeDir 'cloudflare-stdout.log') -RedirectStandardError (Join-Path $runtimeDir 'cloudflare-stderr.log') -PassThru
  @{ pid = $process.Id; startedAt = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding utf8
  Start-Sleep -Milliseconds 1000
  if ($process.HasExited) { throw 'Cloudflare exited. Check .raccoon-runtime/cloudflare-stderr.log.' }
  if (Test-Path -LiteralPath $healthFile) { Remove-Item -LiteralPath $healthFile }
  Write-Output "Cloudflare started: PID $($process.Id)"
} elseif ($Action -eq 'stop') {
  New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
  Set-Content -LiteralPath (Join-Path $runtimeDir 'tunnel-paused') -Value 'Stopped explicitly; start to resume recovery.'
  if ($running) { Stop-Process -Id $running.ProcessId -Force }
  if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile }
  Write-Output 'Cloudflare stopped.'
} else {
  if ($running) { Write-Output "Cloudflare running: PID $($running.ProcessId)" }
  else { Write-Output 'Cloudflare stopped.'; exit 1 }
}

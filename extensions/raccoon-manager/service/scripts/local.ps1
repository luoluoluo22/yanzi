param(
  [ValidateSet('start','stop','status','install-autostart','remove-autostart')]
  [string]$Action = 'status',
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$runtimeDir = if ($env:RACCOON_RUNTIME_DIR) { $env:RACCOON_RUNTIME_DIR } else { Join-Path $projectDir '.raccoon-runtime' }
$pidFile = Join-Path $runtimeDir 'service.json'
$envFile = Join-Path $runtimeDir '.env'
$nodeExe = (Get-Command node -ErrorAction Stop).Source
$entryFile = Join-Path $projectDir 'src\index.js'

function Test-RaccoonProcess($candidate) {
  return $null -ne $candidate -and
    [IO.Path]::GetFileName([string]$candidate.ExecutablePath) -ieq 'node.exe' -and
    [string]$candidate.CommandLine -like ('*' + $entryFile + '*')
}

function Get-ServiceProcess {
  if (Test-Path -LiteralPath $pidFile) {
    $saved = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($saved.pid)"
    if (Test-RaccoonProcess $process) { return $process }
  }
  # The shared runtime may use a different Node executable than `Get-Command node`.
  # Reconcile stale PID files using the actual listening socket, never an arbitrary node PID.
  $portLine = Get-Content -LiteralPath $envFile -ErrorAction SilentlyContinue |
    Where-Object { $_ -match '^RACCOON_PORT=\d+\s*$' } | Select-Object -First 1
  if (-not $portLine) { return $null }
  $port = [int](($portLine -split '=', 2)[1].Trim())
  foreach ($socket in @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($socket.OwningProcess)"
    if (Test-RaccoonProcess $process) { return $process }
  }
  return $null
}
if ($Action -eq 'start') {
  New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
  if (Test-Path -LiteralPath (Join-Path $runtimeDir 'service-paused')) { Remove-Item -LiteralPath (Join-Path $runtimeDir 'service-paused') }
  if (-not (Test-Path -LiteralPath $envFile)) {
    $secretBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($secretBytes)
    $rng.Dispose()
    $secret = [Convert]::ToBase64String($secretBytes)
    @("RACCOON_ROOT=$projectDir", 'RACCOON_TRANSPORT=http', 'RACCOON_HOST=127.0.0.1', 'RACCOON_PORT=3766', "RACCOON_TOKEN=$secret", 'RACCOON_ENABLE_SHELL=0', 'RACCOON_READ_ONLY=0', "RACCOON_AUDIT_LOG=$runtimeDir\audit.jsonl") | Set-Content -LiteralPath $envFile -Encoding utf8
    Write-Output 'Created local .env with a random token (token omitted).'
  }
  $running = Get-ServiceProcess
  if ($running) { Write-Output "Already running: PID $($running.ProcessId)"; exit 0 }
  $service = Start-Process -FilePath $nodeExe -ArgumentList @("`"$entryFile`"", '--http') -WorkingDirectory $projectDir -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtimeDir 'stdout.log') -RedirectStandardError (Join-Path $runtimeDir 'stderr.log') -PassThru
  @{ pid = $service.Id; startedAt = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding utf8
  Start-Sleep -Milliseconds 800
  if ($service.HasExited) { throw 'Service exited. Check .raccoon-runtime/stderr.log.' }
  Write-Output "Started Raccoon: PID $($service.Id)"
} elseif ($Action -eq 'stop') {
  $running = Get-ServiceProcess
  if ($running -and -not $Force) {
    & $nodeExe (Join-Path $PSScriptRoot 'deployment-guard.js')
    if ($LASTEXITCODE -ne 0) { throw 'Safe stop deferred. Service remains running; use -Force only for an explicitly accepted maintenance interruption.' }
  }
  New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
  Set-Content -LiteralPath (Join-Path $runtimeDir 'service-paused') -Value 'Stopped explicitly; start to resume recovery.'
  $running = Get-ServiceProcess
  if ($running) {
    & taskkill.exe /PID $running.ProcessId /T /F | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to stop service process tree.' }
  }
  if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile }
  Write-Output 'Stopped Raccoon.'
} elseif ($Action -eq 'status') {
  $running = Get-ServiceProcess
  if ($running) { Write-Output "Running: PID $($running.ProcessId)" }
  else { Write-Output 'Stopped'; exit 1 }
} elseif ($Action -eq 'install-autostart') {
  $scriptFile = Join-Path $PSScriptRoot 'startup-hidden.vbs'
  $scriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
  $taskAction = New-ScheduledTaskAction -Execute $scriptHost -Argument "//B //Nologo `"$scriptFile`"" -WorkingDirectory $projectDir
  $taskTrigger = New-ScheduledTaskTrigger -AtLogOn -User ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
  $recoveryTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
  $taskPrincipal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
  $taskSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
  Register-ScheduledTask -TaskName 'RaccoonMCP-Local' -Action $taskAction -Trigger @($taskTrigger, $recoveryTrigger) -Principal $taskPrincipal -Settings $taskSettings -Description 'Start at logon and recover unexpectedly stopped MCP/tunnel processes every minute; respect explicit stops.' -Force | Out-Null
  Write-Output 'Installed logon startup and one-minute recovery task RaccoonMCP-Local.'
} else {
  Unregister-ScheduledTask -TaskName 'RaccoonMCP-Local' -Confirm:$false
  Write-Output 'Removed logon startup task.'
}

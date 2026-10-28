param(
  [Parameter(Mandatory = $true)][string]$SshDestination,
  [string]$ServerDeviceId = 'server-29608dd9-386f-4440-aa27-6ab540acf790'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$session = Get-Content (Join-Path $root 'syncsession.json') -Raw | ConvertFrom-Json
$baseUrl = (Get-Content (Join-Path $root 'syncsettings.json') -Raw | ConvertFrom-Json).baseUrl
if ($baseUrl -ne 'https://sync.luoluoluo.cc.cd') { throw 'Unexpected cloud host.' }
if (-not $session.accessToken -or -not $session.userId) { throw 'Log in to Yanzi desktop first.' }
$target = 'android-f85961cb-a306-46aa-8125-33f9d8614a7e'
$authHeaders = @{Authorization = 'Bearer ' + $session.accessToken; 'X-Yanzi-Client' = 'desktop'}
$issued = $null
$success = $false
try {
  $grantRequest = @{
    applicationId = 'yanzi-server-daily-news'
    scopes = @('device.presence', 'messages.receive', 'chat.send')
    targetDeviceIds = @($target)
    fileRoots = @()
    lifetimeSeconds = 2592000
  } | ConvertTo-Json -Depth 6 -Compress
  $issued = Invoke-RestMethod -Method POST -Uri ($baseUrl + '/v1/me/devices/' + $ServerDeviceId + '/credentials') -Headers $authHeaders -UserAgent 'YanziClient-Desktop/1.0' -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($grantRequest)) -TimeoutSec 25
  if (-not $issued.ok -or -not $issued.accessToken) { throw 'Device grant issuance failed.' }
  $handoff = @{
    deviceId = $ServerDeviceId
    accountId = $session.userId
    baseUrl = $baseUrl
    targetDeviceId = $target
    accessToken = $issued.accessToken
    expiresAt = $issued.expiresAt
  } | ConvertTo-Json -Compress
  $psi = [Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = 'ssh'
  foreach ($arg in @('-T','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes',$SshDestination,'sudo -n python3 /opt/yanzi/server/yanzi-node/scripts/receive-grant.py')) { [void]$psi.ArgumentList.Add($arg) }
  $psi.UseShellExecute = $false
  $psi.RedirectStandardInput = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $process = [Diagnostics.Process]::Start($psi)
  try {
    $process.StandardInput.WriteLine($handoff)
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $null = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'SSH provisioning failed. Check SSH host key, key-based login and passwordless sudo.' }
    $result = $output.Trim() | ConvertFrom-Json
    if (-not $result.installed) { throw 'Server rejected the scoped grant.' }
    $success = $true
    Write-Output ('Server scoped credential installed; expiresAt=' + $result.expiresAt)
  } finally { $process.Dispose() }
} finally {
  if (-not $success -and $issued -and $issued.credentialId) {
    try {
      Invoke-RestMethod -Method DELETE -Uri ($baseUrl + '/v1/me/devices/credentials/' + $issued.credentialId) -Headers $authHeaders -UserAgent 'YanziClient-Desktop/1.0' -TimeoutSec 10 | Out-Null
      Write-Warning 'Unused credential has been revoked.'
    } catch { Write-Warning 'Provisioning failed; revoke the unused credential from your Yanzi account.' }
  }
}

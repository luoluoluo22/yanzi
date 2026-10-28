$ErrorActionPreference='Stop'
$old='F:\Desktop\kaifa\raccoon-mcp'
$manager=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\raccoon-manager\managed-start.ps1'
$expected=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\McpRuntime\raccoon\app\src\index.js'
$oldEntry=Join-Path $old 'src\index.js'
$health='http://127.0.0.1:3766/health'
function Process-For([string]$entry) {
  @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -like ('*'+$entry+'*') })
}
# Never interrupt a running development pipeline.
Push-Location $old
try {
  & node 'scripts\deployment-guard.js'
  if($LASTEXITCODE -ne 0){ throw 'Deployment guard denied service cutover' }
} finally { Pop-Location }
$task=Get-ScheduledTask -TaskName 'RaccoonMCP-Local' -ErrorAction SilentlyContinue
if($task -and $task.State -ne 'Disabled'){Disable-ScheduledTask -TaskName $task.TaskName | Out-Null}
$legacy=Process-For $oldEntry
if($legacy.Count -gt 1){throw 'Unexpected multiple legacy nodes; no cutover performed'}
if($legacy.Count -gt 0) { Stop-Process -Id $legacy[0].ProcessId -Force; Start-Sleep -Milliseconds 350 }
try {
  & $manager -Action start
  if($LASTEXITCODE -and $LASTEXITCODE -ne 0){throw 'Managed start failed'}
  $online=$false
  for($i=0;$i -lt 24;$i++){
    Start-Sleep -Milliseconds 250
    try { $h=Invoke-RestMethod $health -TimeoutSec 1; if($h.ok -and $h.name -eq 'raccoon-mcp'){$online=$true;break} }catch{}
  }
  if(!$online){ throw 'Portable service health not ready' }
  $newProcess=Process-For $expected
  if($newProcess.Count -ne 1) { throw 'Health response is not from portable process' }
  Write-Output ('RACCOON_CUTOVER=SUCCESS pid='+$newProcess[0].ProcessId)
} catch {
  Write-Output ('RACCOON_CUTOVER=ROLLBACK reason='+$_.Exception.Message)
  $new=Process-For $expected
  foreach($item in $new) { Stop-Process -Id $item.ProcessId -Force -ErrorAction SilentlyContinue }
  Push-Location $old
  try{ & (Join-Path $old 'scripts\local.ps1') start }finally{ Pop-Location }
  if($task -and $task.State -ne 'Disabled'){ Enable-ScheduledTask -TaskName 'RaccoonMCP-Local' | Out-Null }
  throw
}

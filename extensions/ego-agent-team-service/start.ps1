$ErrorActionPreference = 'Stop'
$base='http://127.0.0.1:43830'
try { $h=Invoke-RestMethod ($base+'/health') -TimeoutSec 2; if($h.ok){ Write-Output 'Agent team is online'; exit 0 } } catch {}
$node=(Get-Command node.exe -ErrorAction SilentlyContinue).Source
if(-not $node){$node=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Runtimes\node\v24.21.0\node.exe'}
if(-not (Test-Path $node)){throw 'Node.js is not installed'}
$root=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\ego-agent-team'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Start-Process -FilePath $node -ArgumentList @('"' + (Join-Path $PSScriptRoot 'server.mjs') + '"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'stdout.log') -RedirectStandardError (Join-Path $root 'stderr.log') | Out-Null
for($i=0;$i -lt 20;$i++){Start-Sleep -Milliseconds 300;try{$h=Invoke-RestMethod ($base+'/health') -TimeoutSec 1;if($h.ok){Write-Output 'Agent team started';exit 0}}catch{}}
throw 'Agent team failed: see stderr.log'

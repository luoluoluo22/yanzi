#requires -version 5.1
[CmdletBinding()]
param([ValidateSet('Prepare','StartBridge','Status','Probe','OpenLogin')][string]$Action='Status')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source=Join-Path $repo 'browser-extension'
$edge=@('C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
'C:\Program Files\Microsoft\Edge\Application\msedge.exe')|Where-Object {Test-Path $_}|Select-Object -First 1
if(-not $edge){throw 'Microsoft Edge not found'}
$root=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\BrowserProfiles'
$profile=Join-Path $root 'ChatGPT-Agent'
$extension=Join-Path $root 'ChatGPT-Agent-Extension'
$data=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-agent-bridge'
$origin='http://127.0.0.1:53922'
function Get-Health {
 try {$health=Invoke-RestMethod ($origin+'/health') -TimeoutSec 2
   if($health.service -eq 'yanzi-chatgpt-bridge'){return $health}}catch{}
 return $null
}
function Get-MyBrowserProcesses {
 @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
   Where-Object {$_.CommandLine -and $_.CommandLine.Contains('ChatGPT-Agent')})
}
function Prepare {
 New-Item -ItemType Directory -Path $root,$profile,$extension,$data -Force|Out-Null
 if(@(Get-MyBrowserProcesses).Count -gt 0){throw 'Isolated Edge running: refuse overwrite'}
 foreach($name in @('chatgpt-background.js','chatgpt-content.js','chatgpt-network-observer.js')){
  $file=Join-Path $source $name
  if(-not(Test-Path $file)){throw ('missing source '+$name)}
  Copy-Item -LiteralPath $file -Destination (Join-Path $extension $name) -Force
 }
 $bg=Join-Path $extension 'chatgpt-background.js'
 $text=[IO.File]::ReadAllText($bg)
 $old='ws://127.0.0.1:53921/v1/browser/ws'
 if($text.Split([string[]]@($old),[StringSplitOptions]::None).Length -ne 2){throw 'Bridge address anchor invalid'}
 [IO.File]::WriteAllText($bg,$text.Replace($old,'ws://127.0.0.1:53922/v1/browser/ws'),[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText((Join-Path $extension 'background.js'),
  'importScripts("chatgpt-network-observer.js","chatgpt-background.js");',[Text.UTF8Encoding]::new($false))
 $version=(Get-Content (Join-Path $source 'manifest.json') -Encoding UTF8 -Raw |ConvertFrom-Json).version
 $manifest=[ordered]@{
  manifest_version=3
  name='Yanzi ChatGPT Agent (isolated)'
  version=$version
  minimum_chrome_version='120'
  description='Isolated ChatGPT Agent browser: no main profile access.'
  permissions=@('tabs','scripting','webRequest','storage','alarms','contextMenus')
  host_permissions=@('https://chatgpt.com/*','http://127.0.0.1:53922/*')
  background=@{service_worker='background.js'}
 }
 [IO.File]::WriteAllText((Join-Path $extension 'manifest.json'),
  ($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
 foreach($name in @('background.js','chatgpt-background.js','chatgpt-content.js','chatgpt-network-observer.js')){
  & node --check (Join-Path $extension $name) 2>&1|Out-Null
  if($LASTEXITCODE -ne 0){throw ('JavaScript syntax invalid: '+$name)}
 }
 'PREPARED=true'
 'PROFILE='+$profile
 'ISOLATED_EXTENSION='+$extension
 'VERSION='+$version
 'CREDENTIALS_COPIED=false'
 'MAIN_BROWSER_UNTOUCHED=true'
}
function StartBridge {
 New-Item -ItemType Directory -Path $data -Force|Out-Null
 $h=Get-Health
 if(-not $h){
  $service=Join-Path $repo 'tools\chatgpt-bridge\isolated-service.mjs'
  $node=(Get-Command node -ErrorAction Stop).Source
  Start-Process -FilePath $node -ArgumentList ('"'+$service+'"') -WorkingDirectory (Split-Path $service -Parent) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $data 'service.stdout.log') -RedirectStandardError (Join-Path $data 'service.stderr.log')|Out-Null
  for($i=0;$i -lt 36;$i++){Start-Sleep -Milliseconds 250;$h=Get-Health;if($h){break}}
 }
 if(-not $h){throw 'Isolated bridge startup failed; inspect service.stderr.log'}
 'ISOLATED_BRIDGE_RUNNING=true'
 'ISOLATED_BROWSER_CONNECTED='+[bool]$h.connected
 'MAIN_BRIDGE_UNCHANGED=true'
}
function Status {
 $h=Get-Health
 try {$main=Invoke-RestMethod 'http://127.0.0.1:53921/health' -TimeoutSec 2}catch{$main=$null}
 [pscustomobject]@{
  ProfilePath=$profile
  ProfileReady=(Test-Path (Join-Path $profile 'Default'))
  ExtensionPrepared=(Test-Path (Join-Path $extension 'manifest.json'))
  IsolatedBridgeRunning=[bool]$h
  IsolatedBridgeConnected=[bool]$h.connected
  MainBridgeConnected=[bool]$main.connected
  IsolatedBrowserProcesses=@(Get-MyBrowserProcesses).Count
  LoginStatus='requires_manual_signin_if_not_authenticated'
 }|ConvertTo-Json -Compress
}
function Probe {
 if(@(Get-MyBrowserProcesses).Count -gt 0){throw 'Isolated Edge already running'}
 New-Item -ItemType Directory -Path $root,$profile -Force|Out-Null
 $page=Join-Path $root 'render-probe.html'
 [IO.File]::WriteAllText($page,'<html><body>YANZI_ISOLATED_RENDER_PASS</body></html>')
 $out=Join-Path $root 'render-probe.stdout.log'
 $err=Join-Path $root 'render-probe.stderr.log'
 $args=@('--headless=new','--disable-extensions','--disable-gpu','--no-first-run','--no-default-browser-check',('"--user-data-dir='+$profile+'"'),'--dump-dom',('"file:///'+$page.Replace('\','/')+'"'))
 $p=Start-Process -FilePath $edge -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err
 $done=$p.WaitForExit(16000)
 if(-not $done){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 $renderText=''
 for($i=0;$i -lt 20;$i++){
  try {$renderText=[IO.File]::ReadAllText($out);break}
  catch [IO.IOException] {Start-Sleep -Milliseconds 250}
 }
 $ok=$done -and $renderText.Contains('YANZI_ISOLATED_RENDER_PASS')
 'ISOLATED_RENDER_PASS='+$ok
 'MAIN_BROWSER_UNTOUCHED=true'
 if(-not $ok){throw 'Independent Edge profile render failed'}
}
function OpenLogin {
 if(-not(Test-Path (Join-Path $extension 'manifest.json'))){throw 'Run -Action Prepare first'}
 if(-not(Get-Health)){throw 'Run -Action StartBridge first'}
 if(@(Get-MyBrowserProcesses).Count -gt 0){'ISOLATED_BROWSER_ALREADY_RUNNING=true';return}
 $args=@('--no-first-run','--no-default-browser-check',('"--user-data-dir='+$profile+'"'),('"--load-extension='+$extension+'"'),'https://chatgpt.com/')
 $p=Start-Process -FilePath $edge -ArgumentList $args -PassThru
 'ISOLATED_BROWSER_PROCESS_ID='+$p.Id
 'USER_ACTION_REQUIRED=Log in through normal ChatGPT sign-in'
 'MAIN_COOKIES_COPIED=false'
}
switch($Action){
 'Prepare'{Prepare}
 'StartBridge'{StartBridge}
 'Status'{Status}
 'Probe'{Probe}
 'OpenLogin'{OpenLogin}
}
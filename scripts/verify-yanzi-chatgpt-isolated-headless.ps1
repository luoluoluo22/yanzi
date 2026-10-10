#requires -version 5.1
$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\BrowserProfiles'
$profile=Join-Path $root 'ChatGPT-Agent'
$extension=Join-Path $root 'ChatGPT-Agent-Extension'
$edge='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if(@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|Where-Object {$_.CommandLine -like '*ChatGPT-Agent*'}).Count -gt 0){
 throw 'Dedicated Edge is already running. Refusing to replace user session.'
}
$myProcess=$null
try {
 $flags=@('--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
   ('"--user-data-dir='+$profile+'"'),('--disable-extensions-except='+$extension),('"--load-extension='+$extension+'"'),'about:blank')
 $myProcess=Start-Process -FilePath $edge -ArgumentList $flags -WindowStyle Hidden -PassThru
 $health=$null
 for($i=0;$i -lt 150;$i++){
   Start-Sleep -Milliseconds 250
   try{$health=Invoke-RestMethod 'http://127.0.0.1:53922/health' -TimeoutSec 2}catch{}
   if($health.connected){break}
 }
 'ISOLATED_EXTENSION_CONNECTED='+[bool]$health.connected
 if(-not $health.connected){throw 'Isolated extension did not connect to secondary bridge'}
 'ISOLATED_EXTENSION_VERSION='+$health.extensionVersion
 $node=Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'tools\chatgpt-bridge\isolated-readiness.mjs'
 $reply=& node $node
 'READINESS='+($reply -join '')
 'READINESS_EXPECTED_EXIT='+$LASTEXITCODE
} finally {
 # Only kill processes bearing our unique isolated user-data-dir.
 $owned=@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'"|
   Where-Object {$_.CommandLine -and $_.CommandLine.Contains('ChatGPT-Agent')})
 foreach($p in $owned){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue}
 Start-Sleep -Milliseconds 500
 try{$h=Invoke-RestMethod 'http://127.0.0.1:53921/health' -TimeoutSec 3
   'MAIN_BRIDGE_STILL_CONNECTED='+[bool]$h.connected}catch{'MAIN_BRIDGE_STILL_CONNECTED=unknown'}
}

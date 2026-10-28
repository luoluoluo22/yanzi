param(
  [Parameter(Mandatory=$true)]
  [ValidateSet("tv.danmaku.bili","com.xingin.xhs","com.ss.android.ugc.aweme")]
  [string]$PackageName,
  [string]$TargetDeviceId="android-f85961cb-a306-46aa-8125-33f9d8614a7e",
  [string]$SourceDeviceId="desktop-66655fb4194741b3a3d8e82310551759",
  [string]$OutputRoot=""
)
$ErrorActionPreference="Stop"
if(-not $OutputRoot) {
  $OutputRoot=Join-Path $env:LOCALAPPDATA "OpenQuickHost\NetworkCaptures\K70\native-public-feed"
}
$sessionPath=Join-Path $env:LOCALAPPDATA "OpenQuickHost\syncsession.json"
if(-not (Test-Path $sessionPath)){throw "Yanzi desktop session is unavailable."}
$session=Get-Content $sessionPath -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $session.accessToken){throw "Yanzi desktop session requires login."}
$headers=@{Authorization="Bearer $($session.accessToken)";"X-Yanzi-Client"="desktop";"User-Agent"="YanziClient-Desktop/1.0"}
$base="https://sync.luoluoluo.cc.cd/v1/me/mobile/messages"
$body=@{
  protocolVersion=1
  kind="capability.invoke"
  sourceDeviceId=$SourceDeviceId
  targetDeviceId=$TargetDeviceId
  clientMessageId=[guid]::NewGuid().ToString("N")
  expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(100).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
  payload=@{name="mobile.screen.inspect";arguments=@{packageName=$PackageName}}
}
$json=$body|ConvertTo-Json -Depth 8 -Compress
$utf8=[System.Text.Encoding]::UTF8.GetBytes($json)
$sent=Invoke-RestMethod $base -Method Post -Headers $headers -Body $utf8 -ContentType "application/json; charset=utf-8" -TimeoutSec 20
if(-not $sent.messageId){throw "Capability command not accepted."}
for($attempt=0;$attempt -lt 26;$attempt++){
  Start-Sleep -Milliseconds 950
  $answer=Invoke-RestMethod ($base+"/"+$sent.messageId) -Headers $headers -TimeoutSec 12
  if($attempt -eq 0 -or $attempt -eq 10 -or $attempt -eq 20){
    Write-Output ("INSPECT_DELIVERY_STATE="+$answer.status+" POLL="+($attempt+1))
  }
  if($answer.status -eq "completed"){
    $exec=$answer.payload.executionResult
    if(-not $exec.success){
      $err="capability_error"
      try{$err=($exec.output|ConvertFrom-Json).error}catch{}
      throw "Mobile public feed inspection failed: $err"
    }
    $v=$exec.output|ConvertFrom-Json
    if($v.packageName -ne $PackageName){throw "Response package mismatch"}
    if(@($v.nodes).Count -gt 85){throw "Too many returned nodes"}
    New-Item -ItemType Directory -Path $OutputRoot -Force|Out-Null
    $name=($PackageName -replace '\.','_')+"-"+(Get-Date -Format "yyyyMMdd-HHmmss")+".json"
    $file=Join-Path $OutputRoot $name
    [IO.File]::WriteAllText($file,($v|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    # Prune only files created by this tool older than 7 days.
    Get-ChildItem $OutputRoot -File -Filter "*.json" |
      Where-Object {$_.LastWriteTime -lt (Get-Date).AddDays(-7) -and $_.Name -match '^(tv_danmaku_bili|com_xingin_xhs|com_ss_android_ugc_aweme)-\d{8}-\d{6}\.json$'} |
      Remove-Item -Force
    Write-Output ("NATIVE_INSPECT_OK=True APP="+$v.packageName+
      " VISITED="+$v.visitedNodes+" TEXT_NODES="+$v.returnedNodes+
      " ELAPSED_MS="+$v.elapsedMs+" TRUNCATED="+$v.truncated)
    Write-Output ("PRIVATE_LOCAL_RESULT="+$file)
    return
  }
  if($answer.status -in @("failed","cancelled","expired")){
    throw "Cloud capability state: $($answer.status)"
  }
}
throw "Phone capability response timed out; do not count as zero content"

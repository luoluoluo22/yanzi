param(
  [ValidateSet("status","capture","tree","tap","swipe","back","home","recents","notifications")]
  [string]$Action="status",
  [Parameter(Mandatory=$true)][string]$TargetDeviceId,
  [Parameter(Mandatory=$true)][string]$SourceDeviceId,
  [int]$X=-1,[int]$Y=-1,[int]$X2=-1,[int]$Y2=-1,
  [ValidateRange(100,1500)][int]$DurationMs=350
)
$ErrorActionPreference="Stop"
$sessionPath=Join-Path $env:LOCALAPPDATA "OpenQuickHost\syncsession.json"
if(-not (Test-Path $sessionPath)){throw "燕子桌面登录会话不存在，请先登录。"}
$session=Get-Content $sessionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $session.accessToken){throw "燕子桌面登录已失效，请重新登录。"}
$headers=@{Authorization="Bearer $($session.accessToken)";"X-Yanzi-Client"="desktop";"User-Agent"="YanziClient-Desktop/1.0"}
$root="https://sync.luoluoluo.cc.cd/v1/me/mobile/messages"
function Invoke-Remote([string]$name,[hashtable]$arguments) {
  $req=@{
    protocolVersion=1;kind="capability.invoke";
    sourceDeviceId=$SourceDeviceId;
    targetDeviceId=$TargetDeviceId;
    clientMessageId=[guid]::NewGuid().ToString("N");
    expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(100).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    payload=@{name=$name;arguments=$arguments}
  }
  $msg=Invoke-RestMethod $root -Method Post -Headers $headers -Body ($req | ConvertTo-Json -Depth 7) -ContentType "application/json" -TimeoutSec 20
  if(-not $msg.messageId){throw "远程命令未受理。"}
  for($i=0;$i -lt 22;$i++){
    Start-Sleep -Milliseconds 1000
    $resp=Invoke-RestMethod ($root+"/"+$msg.messageId) -Headers $headers -TimeoutSec 18
    if($resp.status -eq "completed"){
      $e=$resp.payload.executionResult
      if(-not $e.success){
        $reason=$e.output
        try {$reason=($reason | ConvertFrom-Json).error} catch {}
        throw "手机端拒绝命令：$reason"
      }
      if($e.output){return ($e.output | ConvertFrom-Json)}
      return $null
    }
    if($resp.status -in @("failed","cancelled","expired")){throw "远程命令状态：$($resp.status)"}
  }
  throw "手机响应超时，请检查联网、后台运行及省电限制。"
}
$perm=Invoke-Remote "mobile.screen.status" @{}
Write-Output "无障碍已连接：$($perm.accessibilityConnected)；远控授权：$($perm.enabledByUser)；可用：$($perm.ready)"
if(-not $perm.accessibilityConnected){
  Write-Output "请在 K70 的【系统设置 → 无障碍 → 燕子移动端】开启服务，也可以由【燕子 → 我的 → 无障碍服务】打开设置页。"
}
if(-not $perm.enabledByUser){
  Write-Output "请在 K70 的【燕子 → 我的 → 远程屏幕控制】中手动确认允许（和无障碍授权是两项独立设置）。"
}
if($Action -eq "status"){return}
if(-not $perm.ready){throw "缺少上述授权：屏幕读取和控制命令未发送。"}
$name="mobile.screen.action";$args=@{action=$Action}
switch($Action){
 "capture" {$name="mobile.screen.capture";$args=@{}}
 "tree" {$name="mobile.screen.tree";$args=@{}}
 "tap" {if($X -lt 0 -or $Y -lt 0){throw "缺少点击坐标 X/Y。"};$name="mobile.screen.tap";$args=@{x=$X;y=$Y}}
 "swipe" {if(@($X,$Y,$X2,$Y2) | Where-Object {$_ -lt 0}){throw "缺少滑动坐标。"};$name="mobile.screen.swipe";$args=@{x=$X;y=$Y;x2=$X2;y2=$Y2;durationMs=$DurationMs}}
}
$result=Invoke-Remote $name $args
if($Action -eq "capture"){
  if($result.mimeType -ne "image/jpeg"){throw "截图格式不是 JPEG。"}
  $dir=Join-Path $env:LOCALAPPDATA "OpenQuickHost\AgentCaptures"
  New-Item $dir -ItemType Directory -Force | Out-Null
  Get-ChildItem $dir -File -Filter "yanzi-remote-k70-*.jpg" | Where-Object {$_.LastWriteTime -lt (Get-Date).AddDays(-1)} | Remove-Item -Force
  $file=Join-Path $dir ("yanzi-remote-k70-"+(Get-Date -Format "yyyyMMdd-HHmmss")+".jpg")
  [IO.File]::WriteAllBytes($file,[Convert]::FromBase64String($result.image))
  Write-Output "截图成功：$file；原始屏幕：$($result.width)x$($result.height)"
} elseif($Action -eq "tree"){
  Write-Output "界面节点：$($result.count)"
  $result.nodes | Select-Object -First 25 className,text,contentDescription,bounds,clickable | ConvertTo-Json -Depth 4
} else {Write-Output ("操作完成："+($result | ConvertTo-Json -Compress -Depth 3))}

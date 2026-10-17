param(
  [ValidateSet("status","list","read","write","photos","preview","legacy-list")]
  [string]$Action="status",
  [Parameter(Mandatory=$true)][string]$SourceDeviceId,
  [Parameter(Mandatory=$true)][string]$TargetDeviceId,
  [string]$Path="",
  [string]$Text="",
  [int]$Offset=0,
  [ValidateRange(1,49152)][int]$Length=16384
)
$ErrorActionPreference="Stop"
$sessionPath=Join-Path $env:LOCALAPPDATA "OpenQuickHost\syncsession.json"
if(-not(Test-Path $sessionPath)){throw "Desktop Yanzi login session missing"}
$s=Get-Content $sessionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $s.accessToken){throw "Desktop Yanzi token missing"}
$h=@{Authorization=("Bearer "+$s.accessToken);"X-Yanzi-Client"="desktop";"User-Agent"="YanziClient-Desktop/1.0"}
$base="https://sync.luoluoluo.cc.cd/v1/me/mobile/messages"
$map=@{"status"="mobile.storage.status";"list"="mobile.storage.list";"read"="mobile.storage.read";"write"="mobile.storage.write";"photos"="mobile.photos.list";"preview"="mobile.photos.preview";"legacy-list"="mobile.files.list"}
$name=$map[$Action]
$arguments=@{}
if($Action -in @("list","read","write","preview")){$arguments.path=$Path}
if($Action -eq "read"){$arguments.offset=$Offset;$arguments.length=$Length}
if($Action -eq "write"){
  if([string]::IsNullOrWhiteSpace($Path)){throw "Supply -Path; only new files are supported"}
  $bytes=[Text.Encoding]::UTF8.GetBytes($Text)
  if($bytes.Length -gt 32768){throw "Write text exceeds 32768 bytes"}
  $arguments.content=[Convert]::ToBase64String($bytes)
}
$envelope=@{
  protocolVersion=1;kind="capability.invoke";sourceDeviceId=$SourceDeviceId;
  targetDeviceId=$TargetDeviceId;clientMessageId=[guid]::NewGuid().ToString("N");
  expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(100).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
  payload=@{name=$name;arguments=$arguments}
}
$created=Invoke-RestMethod $base -Method Post -Headers $h -Body ($envelope|ConvertTo-Json -Depth 8) -ContentType "application/json" -TimeoutSec 20
if(-not $created.messageId){throw "Cloud did not accept capability request"}
for($i=0;$i -lt 24;$i++){
  Start-Sleep -Milliseconds 1200
  $m=Invoke-RestMethod ($base+"/"+$created.messageId) -Headers $h -TimeoutSec 18
  if($m.status -eq "completed"){
    $e=$m.payload.executionResult
    if(-not $e.success){
      $err=$e.output
      try{$err=($err|ConvertFrom-Json).error}catch{}
      throw "Phone capability failed: $err"
    }
    $result=$e.output|ConvertFrom-Json
    switch($Action){
      "status"{
        $result|ConvertTo-Json -Depth 4
        if(-not $result.readEnabledByUser){Write-Output "Enable Yanzi > My > Remote File Read on the phone."}
        if(-not $result.androidAllFilesGranted){Write-Output "Android Settings > Special app access > All files access > Yanzi > Allow."}
        if(-not $result.writeEnabledByUser){Write-Output "To create files, also enable Yanzi > My > Remote New File."}
      }
      "list"{$result|ConvertTo-Json -Depth 6}
      "legacy-list"{$result|ConvertTo-Json -Depth 5}
      "photos"{$result|ConvertTo-Json -Depth 5}
      "write"{$result|ConvertTo-Json -Depth 4}
      "read"{
        $bytes=[Convert]::FromBase64String($result.content)
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-","").ToLowerInvariant()}finally{$sha.Dispose()}
        Write-Output "Read succeeded: path=$Path offset=$($result.offset) bytes=$($bytes.Length) fileSize=$($result.size) eof=$($result.eof) chunkSha256=$hash"
        if($bytes.Length -le 1024){
          Write-Output ("UTF8="+[Text.Encoding]::UTF8.GetString($bytes))
        }
      }
      "preview"{
        if($result.mimeType -ne "image/jpeg"){throw "Unsupported preview MIME"}
        $dir=Join-Path $env:LOCALAPPDATA "OpenQuickHost\AgentCaptures"
        New-Item $dir -ItemType Directory -Force|Out-Null
        Get-ChildItem $dir -Filter "yanzi-photo-preview-*.jpg" -File|Where-Object{$_.LastWriteTime -lt (Get-Date).AddDays(-1)}|Remove-Item -Force
        $file=Join-Path $dir ("yanzi-photo-preview-"+(Get-Date -Format yyyyMMdd-HHmmss)+".jpg")
        [IO.File]::WriteAllBytes($file,[Convert]::FromBase64String($result.content))
        Write-Output "Saved photo preview: $file ($($result.width)x$($result.height))"
      }
    }
    exit 0
  }
  if($m.status -in @("failed","expired","cancelled")){throw "Phone request $name status=$($m.status)"}
}
throw "Phone request timed out"

param(
    [ValidateSet("status","find","click","type","wait","verify","task-start","task-status","task-cancel","task-history")]
    [string]$Action="status",
    [Parameter(Mandatory=$true)][string]$TargetDeviceId,
    [Parameter(Mandatory=$true)][string]$SourceDeviceId,
    [string]$PackageName="",
    [string]$ResourceId="",
    [string]$SelectorText="",
    [string]$SelectorDescription="",
    [string]$SelectorClass="",
    [switch]$Contains,
    [switch]$EditableOnly,
    [switch]$AllowFirst,
    [switch]$ExpectAbsent,
    [string]$TextValue="",
    [string]$StepsFile="",
    [string[]]$AllowedPackages=@(),
    [int]$HistoryLimit=10,
    [string]$TaskId="",
    [int]$TimeoutMs=5000
)
$ErrorActionPreference="Stop"
$session=Get-Content (Join-Path $env:LOCALAPPDATA "OpenQuickHost\syncsession.json") -Raw -Encoding UTF8|ConvertFrom-Json
if(-not $session.accessToken){throw "Desktop login required"}
$headers=@{Authorization="Bearer $($session.accessToken)";"X-Yanzi-Client"="desktop";"User-Agent"="YanziClient-Desktop/1.0"}
$root="https://sync.luoluoluo.cc.cd/v1/me/mobile/messages"
$names=@{
    "status"="mobile.ui.status";"find"="mobile.ui.find";"click"="mobile.ui.click";
    "type"="mobile.ui.type";"wait"="mobile.ui.wait";"verify"="mobile.ui.verify";
    "task-start"="mobile.ui.task.start";"task-status"="mobile.ui.task.status";
    "task-cancel"="mobile.ui.task.cancel";"task-history"="mobile.ui.task.history"
}
function Call-Capability([string]$name,[hashtable]$parameters){
    $envelope=@{
        protocolVersion=1;kind="capability.invoke";sourceDeviceId=$SourceDeviceId;
        targetDeviceId=$TargetDeviceId;clientMessageId=[guid]::NewGuid().ToString("N");
        expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(105).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        payload=@{name=$name;arguments=$parameters}
    }
    $json=$envelope|ConvertTo-Json -Depth 16 -Compress
    if($name -eq "mobile.ui.task.start"){
        # PowerShell 5 wraps top-level JSON arrays in {"value":...,"Count":...}.
        # Preserve the validated source JSON array exactly, without transforming it to a PSCustomObject.
        $json=$json.Replace('"__YANZI_RAW_UI_STEPS_JSON__"',$script:RawUiStepsJson)
    }
    # Windows PowerShell 5 defaults to legacy encodings for string bodies; always transmit UTF-8.
    $bodyBytes=[System.Text.Encoding]::UTF8.GetBytes($json)
    $msg=Invoke-RestMethod $root -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8" -TimeoutSec 20
    if(-not $msg.messageId){throw "Cloud did not accept $name"}
    for($i=0;$i -lt 24;$i++){
        Start-Sleep -Milliseconds 1100
        $m=Invoke-RestMethod ($root+"/"+$msg.messageId) -Headers $headers -TimeoutSec 15
        if($m.status -eq "completed"){
            $e=$m.payload.executionResult
            if(-not $e.success){
                $failure=$e.output
                try{$failure=($failure|ConvertFrom-Json).error}catch{}
                throw ("Phone rejected "+$name+": "+$failure)
            }
            return ($e.output|ConvertFrom-Json)
        }
        if($m.status -in @("failed","expired","cancelled")){
            $failure=$m.payload.executionResult.output
            try{$failure=($failure|ConvertFrom-Json).error}catch{}
            throw ("Cloud command "+$name+" "+$m.status+": "+$failure)
        }
    }
    throw "Phone command timed out. Check Yanzi background service."
}
if($Action -eq "status"){
    $response=Call-Capability $names[$Action] @{}
    $response|ConvertTo-Json -Depth 5
    if(-not $response.accessibilityConnected){Write-Output "Enable Android Settings > Accessibility > Yanzi."}
    if(-not $response.screenControlEnabled){Write-Output "Enable Yanzi > My > Remote Screen Control."}
    if(-not $response.automationEnabledByUser){Write-Output "Enable Yanzi > My > Remote UI Automation (new in 0.2.74)."}
    return
}
$args=@{}
if($PackageName){$args.packageName=$PackageName}
if($Action -in @("find","click","type","wait","verify")){
    $selector=@{}
    if($ResourceId){$selector.resourceId=$ResourceId}
    if($SelectorText){$selector.text=$SelectorText}
    if($SelectorDescription){$selector.description=$SelectorDescription}
    if($SelectorClass){$selector.className=$SelectorClass}
    if($Contains){$selector.contains=$true}
    if($EditableOnly){$selector.editableOnly=$true}
    if($selector.Count -eq 0){throw "Specify ResourceId / SelectorText / SelectorDescription / SelectorClass"}
    $args.selector=$selector
    if($AllowFirst){$args.allowFirst=$true}
}
if($Action -eq "type"){$args.value=$TextValue}
if($Action -in @("wait","verify")){$args.expectFound=(-not $ExpectAbsent)}
if($Action -eq "wait"){$args.timeoutMs=$TimeoutMs}
if($Action -eq "task-start"){
    if(-not $StepsFile){throw "For task-start specify -StepsFile (UTF-8 JSON array of steps)."}
    $json=Get-Content $StepsFile -Raw -Encoding UTF8
    if(-not $json.TrimStart().StartsWith("[")){throw "The task file must contain a JSON array of steps."}
    # Validate JSON, then splice the raw JSON array into the outgoing envelope.
    $null=ConvertFrom-Json -InputObject $json
    $script:RawUiStepsJson=$json.Trim()
    $args.steps="__YANZI_RAW_UI_STEPS_JSON__"
    if($AllowedPackages.Count -gt 0){$args.allowedPackages=[string[]]$AllowedPackages}
}
if($Action -in @("task-status","task-cancel") -and $TaskId){$args.taskId=$TaskId}
if($Action -eq "task-history"){$args.limit=[Math]::Max(1,[Math]::Min(20,$HistoryLimit))}
$response=Call-Capability $names[$Action] $args
$response|ConvertTo-Json -Depth 7

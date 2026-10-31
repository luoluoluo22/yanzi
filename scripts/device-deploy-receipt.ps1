#Requires -Version 5.1
<#
Publish one minimal, authenticated installation receipt to a PRIVATE GitHub repo.
The existing device-deploy-agent.ps1 remains the authority for signed release downloads.
Only version, state, process status, time, and a sanitized failure type are uploaded.
No API keys, command lines, local files, messages, or user content are included.
#>
param(
    [string]$ReceiptRepo = 'luoluoluo22/yanzi-deployment-receipts',
    [string]$StatusPath = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent\status.json'),
    [string]$CachePath = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent\receipt-cache.json'),
    [switch]$Force,
    [switch]$DryRun
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function New-Receipt {
    if (-not (Test-Path -LiteralPath $StatusPath)) { throw "Missing deployment agent status" }
    $s = Get-Content -LiteralPath $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $machine=$env:COMPUTERNAME.ToUpperInvariant()
    if ($machine -notin @('PC-202409131508','CHINAMI-3IF988E')) { throw "Unexpected deployment device" }
    if (-not [string]::Equals([string]$s.machine,$machine,[StringComparison]::OrdinalIgnoreCase)) {
        throw "Status receipt machine mismatch"
    }
    $exe=Join-Path $env:LOCALAPPDATA 'Yanzi\current\Yanzi.exe'
    $fileVersion=''
    if (Test-Path -LiteralPath $exe) {
        try {$fileVersion = ([version](Get-Item -LiteralPath $exe).VersionInfo.FileVersion).ToString(3)} catch {}
    }
    $running=@(Get-Process -Name Yanzi -ErrorAction SilentlyContinue).Count -gt 0
    $installed=[string]$s.installedVersion
    $reportedState=[string]$s.state
    $verified=$running -and $installed -ne '' -and $installed -eq $fileVersion -and
        $reportedState -in @('current','verified-running')
    $state = if ($verified) { 'verified-running' } elseif ($reportedState -eq 'failed') {
        'failed'
    } else { 'pending' }
    # No potentially sensitive or untrusted raw exception strings leave this machine.
    return [ordered]@{
        schemaVersion=1; machine=$machine
        state=$state; agentState=$reportedState
        installedVersion=$installed; targetVersion=[string]$s.targetVersion
        fileVersion=$fileVersion; processRunning=[bool]$running
        release=[string]$s.release; agentCheckedAtUtc=[string]$s.checkedAtUtc
        checkedAtUtc=[DateTime]::UtcNow.ToString('o')
        failureKind=if($state -eq 'failed'){'deployment-agent-failed'}else{''}
    }
}
function Call-Gh([string[]]$Arguments) {
    $output=@(& gh @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "GitHub API operation failed (code $LASTEXITCODE)" }
    return ($output -join [Environment]::NewLine)
}
try {
    $receipt=New-Receipt
    $payload=ConvertTo-Json -InputObject $receipt -Depth 5 -Compress
    $fingerprint=(@($receipt.machine,$receipt.state,$receipt.agentState,$receipt.installedVersion,
        $receipt.targetVersion,$receipt.fileVersion,$receipt.processRunning,$receipt.release) -join '|')
    if (-not $Force -and (Test-Path -LiteralPath $CachePath)) {
        $cached=Get-Content -LiteralPath $CachePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $lastUpload=[DateTime]::MinValue
        try {$lastUpload=[DateTime]::Parse([string]$cached.uploadedAtUtc).ToUniversalTime()} catch {}
        # Keep a bounded 6-hour heartbeat without committing every scheduled poll.
        if ($cached.fingerprint -eq $fingerprint -and [DateTime]::UtcNow.Subtract($lastUpload).TotalHours -lt 6) {
            Write-Output 'RECEIPT_UNCHANGED=1'; exit 0
        }
    }
    if ($DryRun) { Write-Output $payload; exit 0 }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI unavailable" }
    if ($ReceiptRepo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid private receipt repository' }
    $repoInfo=Call-Gh @('api',"repos/$ReceiptRepo",'--jq','.private')
    if ($repoInfo.Trim() -ne 'true') { throw 'Refusing to publish device receipt outside a private repo' }
    $path="devices/$($receipt.machine).json"
    $endpoint="repos/$ReceiptRepo/contents/$path"
    $sha=''
    # 404 is expected before the device posts its very first receipt.
    $priorPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $previous=@(& gh api $endpoint --jq '.sha' 2>$null)
    } finally { $ErrorActionPreference=$priorPreference }
    if ($LASTEXITCODE -eq 0 -and $previous.Count -gt 0) { $sha=([string]$previous[0]).Trim() }
    $content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
    $args=@('api',$endpoint,'--method','PUT','-f',"message=chore(receipts): $($receipt.machine) $($receipt.installedVersion) $($receipt.state)",
        '-f',"content=$content")
    if ($sha) { $args+=@('-f',"sha=$sha") }
    $null=Call-Gh $args
    $cache=[ordered]@{ fingerprint=$fingerprint;uploadedAtUtc=[DateTime]::UtcNow.ToString('o') }
    $dir=Split-Path -Parent $CachePath
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $temp="$CachePath.tmp.$PID"
    [IO.File]::WriteAllText($temp,(ConvertTo-Json -InputObject $cache -Compress),
        (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temp -Destination $CachePath -Force
    Write-Output "RECEIPT_UPLOADED=$($receipt.machine):$($receipt.installedVersion):$($receipt.state)"
} catch {
    # Never change the original deployment result on a telemetry problem.
    Write-Error ("RECEIPT_FAILED="+$_.Exception.Message)
    exit 1
}

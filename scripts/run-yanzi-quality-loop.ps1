#requires -version 5.1
<#
Runs a bounded, reproducible quality gate and returns evidence into the exact
registered ChatGPT conversation. No global desktop input is injected.
By default it saves feedback to the outbox; binding is NEVER guessed.
Use: -OriginId <UUID> -TaskId <stable-task-name> -Round 1
#>
[CmdletBinding()]
param(
    [string]$TaskId = 'yanzi-ui-quality',
    [int]$Round = 1,
    [string]$OriginId = '',
    [switch]$UseLatestOrigin,
    [switch]$NoBuild,
    [switch]$SkipOcr,
    [ValidateRange(0,120)][int]$DeliveryWaitSeconds = 15,
    [string]$Output = '.tmp/quality-loop'
)
$ErrorActionPreference = 'Stop'
if ($TaskId -notmatch '^[a-zA-Z0-9_.-]{3,70}$') { throw 'TaskId must be 3–70 simple characters' }
if ($Round -lt 1 -or $Round -gt 20) { throw 'Round must be 1–20' }
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repo
try {
    $dir=[IO.Path]::GetFullPath((Join-Path $repo $Output))
    New-Item -ItemType Directory -Path $dir -Force|Out-Null
    $started=[DateTimeOffset]::UtcNow
    $commit=((& git rev-parse HEAD) -join '').Trim()
    $dirty=@(& git status --porcelain=v1)
    $checks=New-Object System.Collections.Generic.List[object]
    function Invoke-QualityCheck([string]$name,[scriptblock]$command) {
        $logfile=Join-Path $dir ($TaskId+'-r'+$Round+'-'+$name+'.log')
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $oldPreference=$ErrorActionPreference
        $result=@()
        $exitCode=1
        try {
            $ErrorActionPreference='Continue'
            $result=@(& $command 2>&1)
            $exitCode=if($LASTEXITCODE -is [int]){$LASTEXITCODE}else{0}
            if (@($result | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] }).Count -gt 0 -and $exitCode -eq 0) {
                $exitCode=1
            }
        } catch {
            $result+=($_ | Out-String)
            $exitCode=1
        } finally {
            $ErrorActionPreference=$oldPreference
            $clock.Stop()
        }
        $lines=($result | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
        [IO.File]::WriteAllText($logfile,$lines,[Text.UTF8Encoding]::new($false))
        $check=[pscustomobject]@{name=$name;passed=($exitCode -eq 0);exitCode=$exitCode;durationMs=$clock.ElapsedMilliseconds;log=$logfile;tail=($lines -split '\r?\n' | Select-Object -Last 12) -join ' | '}
        $checks.Add($check)
        Write-Output ('QUALITY_CHECK '+$name+' '+$(if($check.passed){'PASS'}else{'FAIL'})+' '+$clock.ElapsedMilliseconds+'ms')
    }
    if (-not $NoBuild) {
        Invoke-QualityCheck 'build-capture' { dotnet build src/Yanzi.Capture/Yanzi.Capture.csproj -c Release --nologo -v:q }
        Invoke-QualityCheck 'build-uitests' { dotnet build tools/yanzi-ui-test-samples/Yanzi.UiTestSamples.csproj -c Release --nologo -v:q }
    }
    Invoke-QualityCheck 'chatgpt-bridge' { node --test tools/chatgpt-bridge/test/origin-routing.test.mjs tools/chatgpt-bridge/test/background.test.mjs tools/chatgpt-bridge/test/server.test.mjs }
    Invoke-QualityCheck 'hidden-ui' { powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-hidden.ps1 -NoBuild }
    if (-not $SkipOcr) {
        Invoke-QualityCheck 'ocr-headless' { powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-capture-ocr-headless.ps1 }
    }
    $passed=@($checks | Where-Object passed).Count
    $failed=@($checks | Where-Object { -not $_.passed }).Count
    $report=[ordered]@{
        schemaVersion=1
        taskId=$TaskId
        round=$Round
        startedUtc=$started.ToString('o')
        finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        gitCommit=$commit
        workingTreeDirtyFiles=$dirty.Count
        status=if($failed -eq 0){'tests_passed'}else{'repair_required'}
        checks=$checks.ToArray()
        passedCount=$passed
        failedCount=$failed
        deploymentApproved=$false
        desktopPhysicalE2eVerified=$false
    }
    $json=($report|ConvertTo-Json -Depth 12)
    $reportPath=Join-Path $dir ($TaskId+'-r'+$Round+'-report.json')
    [IO.File]::WriteAllText($reportPath,$json,[Text.UTF8Encoding]::new($false))
    $brief=@(
      '[燕子质量闭环 / 自动验收反馈]',
      ('任务：'+$TaskId+'，轮次：'+$Round+'，状态：'+$report.status),
      ('Git：'+$commit.Substring(0,12)+'，未提交文件数：'+$dirty.Count),
      ('测试：通过 '+$passed+'，失败 '+$failed),
      '检查项目：'
    )
    foreach($item in $checks) {
        $brief+=(' - '+$item.name+'：'+$(if($item.passed){'PASS'}else{'FAIL'})+'，'+$item.durationMs+'ms')
        if(-not $item.passed) {$brief+=('   末尾日志：'+$item.tail.Substring(0,[Math]::Min(500,$item.tail.Length)))}
    }
    $brief+=('完整报告：'+$reportPath)
    $brief+='注意：未执行真实桌面鼠标拉框 E2E，也未获准自动发布。'
    $brief+='请核对报告并判断是否达到用户目标。若失败，修复后重新运行质量脚本（轮次加一），保持原对话上下文；若全部通过，仍需报告未覆盖的用户级验收项。'
    $text=($brief -join [Environment]::NewLine)
    $outboxPath=Join-Path $dir ($TaskId+'-r'+$Round+'-feedback.txt')
    [IO.File]::WriteAllText($outboxPath,$text,[Text.UTF8Encoding]::new($false))
    Write-Output ('QUALITY_REPORT='+$reportPath)
    Write-Output ('QUALITY_OUTBOX='+$outboxPath)
    Write-Output ('QUALITY_RESULT='+$report.status)
    if ($UseLatestOrigin -and $OriginId) {throw 'Cannot specify OriginId and UseLatestOrigin simultaneously'}
    if ($OriginId -or $UseLatestOrigin) {
        $tokenFile=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\chatgpt-bridge\api-token.txt'
        if(-not(Test-Path $tokenFile)){throw 'Bridge token file not found; report kept in outbox'}
        $token=[IO.File]::ReadAllText($tokenFile).Trim()
        $headers=@{Authorization=('Bearer '+$token);'X-Bridge-Request'='1'}
        $base='http://127.0.0.1:53921'
        if($UseLatestOrigin) {
            $all=@(Invoke-RestMethod "$base/api/origins" -Headers $headers -TimeoutSec 8)
            if($all.Count -ne 1) {throw 'Cannot infer originating conversation; select an explicit OriginId. Report retained in outbox.'}
            $OriginId=[string]$all[0].id
        }
        $body=@{originId=$OriginId;deliveryKey=($TaskId+':'+$Round+':'+$commit.Substring(0,12));text=$text}|ConvertTo-Json -Compress
        try {
            $job=Invoke-RestMethod "$base/api/feedback" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 15
            Write-Output ('QUALITY_DELIVERY_JOB='+$job.jobId)
            Write-Output ('QUALITY_DELIVERY_STATUS='+$job.status)
            Write-Output ('QUALITY_DELIVERY_DUPLICATE='+$job.duplicate)
            $receiptPath=Join-Path $dir ($TaskId+'-r'+$Round+'-delivery.json')
            $receipt=[ordered]@{originId=$OriginId;deliveryKey=($TaskId+':'+$Round+':'+$commit.Substring(0,12));
                jobId=$job.jobId;lastObservedStatus=$job.status;createdUtc=[DateTimeOffset]::UtcNow.ToString('o')}
            $deadline=(Get-Date).AddSeconds($DeliveryWaitSeconds)
            while((Get-Date) -lt $deadline -and $receipt.lastObservedStatus -in @('queued','running')){
                Start-Sleep -Seconds 1
                try {
                    $current=Invoke-RestMethod ($base+'/api/jobs/'+$job.jobId) -Headers $headers -TimeoutSec 5
                    $receipt.lastObservedStatus=$current.status
                    if($current.status -eq 'success') {
                        $receipt.conversationVerified=($current.data.conversationId -eq (
                            ((@((Invoke-RestMethod ($base+'/api/origins') -Headers $headers -TimeoutSec 5)) |
                                Where-Object id -EQ $OriginId | Select-Object -First 1).conversationId)))
                    }
                    if($current.status -notin @('queued','running')){
                        $receipt.completedUtc=[DateTimeOffset]::UtcNow.ToString('o')
                        $receipt.reason=[string]$current.message
                    }
                } catch { $receipt.lastPollError=$_.Exception.GetType().Name; break }
            }
            [IO.File]::WriteAllText($receiptPath,($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
            Write-Output ('QUALITY_DELIVERY_FINAL_STATUS='+$receipt.lastObservedStatus)
            Write-Output ('QUALITY_DELIVERY_RECEIPT='+$receiptPath)
        } catch {
            Write-Warning ('Feedback is saved but not delivered: '+$_.Exception.Message)
            Write-Output 'QUALITY_DELIVERY_STATUS=outbox_only'
        }
    } else {
        Write-Output 'QUALITY_DELIVERY_STATUS=awaiting_explicit_origin'
    }
    if($failed -gt 0){exit 1}
} finally {Pop-Location}

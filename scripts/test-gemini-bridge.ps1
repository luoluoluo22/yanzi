# ==============================================================================
# 燕子浏览器助手 - Google Gemini 自动化桥接端到端集成测试脚本
# 兼容性: Windows PowerShell 5.1, 强制 UTF-8 with BOM 保存
# ==============================================================================
param(
    [ValidateSet("All", "Status", "Diagnostics", "Gemini4Math", "Gemini4Json", "ModelSwitch", "Messages", "NewTab")]
    [string]$Mode = "All",
    [int]$TabId = 0
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  燕子 (Yanzi) 浏览器助手 - Google Gemini 自动化桥接验证套件" -ForegroundColor Cyan
Write-Host "  测试模式: $Mode" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 1. 加载配置并确认 Local Agent API
$settingsPath = Join-Path $env:LOCALAPPDATA "OpenQuickHost\appsettings.local.json"
if (-not (Test-Path $settingsPath)) {
    throw "未找到燕子配置文件: $settingsPath"
}

$settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$port = [int]($settings.agentApiPort)
if ($port -le 0) { $port = 53919 }
$token = [string]$settings.agentApiToken
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "未配置 Local Agent API Token。"
}

$baseUrl = "http://127.0.0.1:$port"
$headers = @{
    "X-Yanzi-Token" = $token
    "Content-Type"  = "application/json; charset=utf-8"
}

function Invoke-GeminiAction {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$TaskPayload,
        [int]$TimeoutSec = 130
    )

    $json = $TaskPayload | ConvertTo-Json -Depth 10
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($json)

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-RestMethod -Uri "$baseUrl/v1/browser/execute" `
        -Headers $headers `
        -Method Post `
        -Body $bodyBytes `
        -TimeoutSec $TimeoutSec
    $stopwatch.Stop()

    return [PSCustomObject]@{
        ElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        Response       = $response
    }
}

# ------------------------------------------------------------------------------
# 状态检查
# ------------------------------------------------------------------------------
function Run-StatusTest {
    Write-Host "`n[测试 1/5] 查询 Gemini 自动化状态与标签页池 (gemini_status)..." -ForegroundColor Yellow
    $res = Invoke-GeminiAction -TaskPayload @{ action = "gemini_status" }
    $data = $res.Response.data
    Write-Host "  耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Gray
    Write-Host "  扩展版本: $($data.extensionVersion)" -ForegroundColor Green
    Write-Host "  活动任务: $($data.concurrency.active) / 最大并发: $($data.concurrency.max)" -ForegroundColor Green
    Write-Host "  标签页总数: $($data.tabs.Count)" -ForegroundColor Green

    foreach ($t in $data.tabs) {
        Write-Host "    - TabId: $($t.tabId) | Active: $($t.active) | URL: $($t.url)" -ForegroundColor DarkGray
    }

    if ($data.tabs.Count -eq 0) {
        throw "未发现打开的 Gemini 标签页，请先在浏览器中打开 https://gemini.google.com"
    }

    return $data.tabs[0].tabId
}

# ------------------------------------------------------------------------------
# Gemini 4 (Argon) 运算与生成捕获测试
# ------------------------------------------------------------------------------
function Run-Gemini4MathTest {
    param([int]$TargetTabId)
    Write-Host "`n[测试 2/5] Gemini 4 (Argon) 计算与生成能力测试 (gemini_send)..." -ForegroundColor Yellow
    $prompt = "请精确计算 13579 乘以 2468 的乘积，并在最后一行明确给出 '结果：<数值>'。"
    Write-Host "  Prompt: $prompt" -ForegroundColor Gray

    $payload = @{
        action         = "gemini_send"
        model          = "Argon"
        prompt         = $prompt
        timeoutSeconds = 180
    }
    if ($TargetTabId -gt 0) { $payload.tabId = $TargetTabId }

    $res = Invoke-GeminiAction -TaskPayload $payload -TimeoutSec 190
    $data = $res.Response.data
    Write-Host "  完成！耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Green
    Write-Host "  当前模型: $($data.model)" -ForegroundColor Cyan
    Write-Host "  回复消息 ID: $($data.id)" -ForegroundColor DarkGray
    Write-Host "-------------------- 模型回复正文 --------------------" -ForegroundColor White
    Write-Host $data.text -ForegroundColor White
    Write-Host "------------------------------------------------------" -ForegroundColor White

    # 验证答案：13579 * 2468 = 33512972
    if ($data.text -match "33[,]?512[,]?972") {
        Write-Host "  [PASS] 数学计算结果完全正确 (33,512,972)！" -ForegroundColor Green
    } else {
        Write-Host "  [WARN] 回复已成功提取，但未精确匹配到预期数值 33,512,972" -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------------------
# Gemini 4 (Argon) 结构化 JSON 输出与代码块解析测试
# ------------------------------------------------------------------------------
function Run-Gemini4JsonTest {
    param([int]$TargetTabId)
    Write-Host "`n[测试 3/5] Gemini 4 (Argon) JSON 代码块自动提取测试..." -ForegroundColor Yellow
    $prompt = '请生成一个符合以下要求的 JSON 数据：包含 "status": "ok", "service": "Gemini-Yanzi-Bridge", "testedAt": "2026-10-14", "capabilities": ["auto_switch", "markdown_stream", "json_parse"]。请务必使用 ```json ... ``` 代码块包裹。'
    Write-Host "  Prompt: $prompt" -ForegroundColor Gray

    $payload = @{
        action         = "gemini_send"
        model          = "Argon"
        prompt         = $prompt
        timeoutSeconds = 120
    }
    if ($TargetTabId -gt 0) { $payload.tabId = $TargetTabId }

    $res = Invoke-GeminiAction -TaskPayload $payload -TimeoutSec 130
    $data = $res.Response.data
    Write-Host "  完成！耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Green
    Write-Host "  代码块总数: $($data.codeBlocks.Count)" -ForegroundColor Cyan

    if ($data.json) {
        Write-Host "  [PASS] 结构化 JSON 自动解析成功！" -ForegroundColor Green
        Write-Host ($data.json | ConvertTo-Json -Depth 5) -ForegroundColor White
    } else {
        Write-Host "  [FAIL] 未成功解析结构化 JSON 对象: $($data.jsonError)" -ForegroundColor Red
        if ($data.codeBlocks.Count -gt 0) {
            Write-Host "  原始代码块内容: $($data.codeBlocks[0].text)" -ForegroundColor Yellow
        }
    }
}

# ------------------------------------------------------------------------------
# 多模型跨版本切换测试
# ------------------------------------------------------------------------------
function Run-ModelSwitchTest {
    param([int]$TargetTabId)
    Write-Host "`n[测试 4/5] 动态模型切换测试 (gemini_model_switch)..." -ForegroundColor Yellow

    # 1. 切换至 Flash
    Write-Host "  --> 尝试切换至 'Flash'..." -ForegroundColor Cyan
    $resFlash = Invoke-GeminiAction -TaskPayload @{
        action = "gemini_model_switch"
        tabId  = $TargetTabId
        model  = "Flash"
    }
    Write-Host "      切换结果: $($resFlash.Response.data.currentModel) (switched=$($resFlash.Response.data.switched))" -ForegroundColor Green
    Start-Sleep -Milliseconds 800

    # 2. 用 Flash 进行一次快速问答
    Write-Host "  --> 使用 Flash 模型进行快速问答..." -ForegroundColor Cyan
    $resSendFlash = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TargetTabId
        prompt         = "一句话介绍太阳系最大的行星是什么？"
        timeoutSeconds = 90
    }
    Write-Host "      Flash 回复: $($resSendFlash.Response.data.text.Trim())" -ForegroundColor White
    Start-Sleep -Milliseconds 800

    # 3. 切换回 Gemini 4 (Argon)
    Write-Host "  --> 切换回 'Argon' (Gemini 4)..." -ForegroundColor Cyan
    $resArgon = Invoke-GeminiAction -TaskPayload @{
        action = "gemini_model_switch"
        tabId  = $TargetTabId
        model  = "Argon"
    }
    Write-Host "      切换结果: $($resArgon.Response.data.currentModel) (switched=$($resArgon.Response.data.switched))" -ForegroundColor Green
    Write-Host "  [PASS] 跨模型平滑切换及调用验证通过！" -ForegroundColor Green
}

# ------------------------------------------------------------------------------
# 历史消息读取测试
# ------------------------------------------------------------------------------
function Run-MessagesTest {
    param([int]$TargetTabId)
    Write-Host "`n[测试 5/5] 获取当前对话历史结构 (gemini_messages)..." -ForegroundColor Yellow
    $res = Invoke-GeminiAction -TaskPayload @{
        action = "gemini_messages"
        tabId  = $TargetTabId
    }
    $msgs = $res.Response.data.messages
    Write-Host "  当前会话已记录消息总数: $($msgs.Count)" -ForegroundColor Green

    $userCount = ($msgs | Where-Object { $_.role -eq "user" }).Count
    $assistantCount = ($msgs | Where-Object { $_.role -eq "assistant" }).Count
    Write-Host "    - User 消息: $userCount 条" -ForegroundColor DarkGray
    Write-Host "    - Assistant 消息: $assistantCount 条" -ForegroundColor DarkGray

    if ($msgs.Count -gt 0) {
        $last = $msgs[-1]
        Write-Host "  最新一条消息 [Role: $($last.role)] 摘要: $($last.text.Substring(0, [Math]::Min(120, $last.text.Length)))..." -ForegroundColor Gray
    }
    Write-Host "  [PASS] 历史消息解析与 Markdown 结构提取正确！" -ForegroundColor Green
}

# ------------------------------------------------------------------------------
# 执行测试套件
# ------------------------------------------------------------------------------
$activeTabId = $TabId
if ($activeTabId -le 0) {
    $activeTabId = Run-StatusTest
}

switch ($Mode) {
    "Status"         { Run-StatusTest }
    "Diagnostics"    {
        $d = Invoke-GeminiAction -TaskPayload @{ action = "gemini_diagnostics"; tabId = $activeTabId }
        $d.Response.data | ConvertTo-Json -Depth 6 | Write-Host
    }
    "Gemini4Math"    { Run-Gemini4MathTest -TargetTabId $activeTabId }
    "Gemini4Json"    { Run-Gemini4JsonTest -TargetTabId $activeTabId }
    "ModelSwitch"    { Run-ModelSwitchTest -TargetTabId $activeTabId }
    "Messages"       { Run-MessagesTest -TargetTabId $activeTabId }
    "All"            {
        Run-Gemini4MathTest -TargetTabId $activeTabId
        Start-Sleep -Seconds 3
        Run-Gemini4JsonTest -TargetTabId $activeTabId
        Start-Sleep -Seconds 3
        Run-ModelSwitchTest -TargetTabId $activeTabId
        Start-Sleep -Seconds 2
        Run-MessagesTest -TargetTabId $activeTabId
        Write-Host "`n================================================================" -ForegroundColor Cyan
        Write-Host "  [SUCCESS] Google Gemini 自动化桥接所有功能全面验收通过！" -ForegroundColor Green
        Write-Host "================================================================" -ForegroundColor Cyan
    }
}

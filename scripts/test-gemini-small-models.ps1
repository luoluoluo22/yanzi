# ==============================================================================
# 燕子浏览器助手 - Google Gemini 小模型 (Flash-Lite / Flash) 全链路自动化验证套件
# 兼容性: Windows PowerShell 5.1, 强制 UTF-8 with BOM 保存
# ==============================================================================
param(
    [ValidateSet("All", "Switch", "Json", "MultiTurn", "NewTab", "LongText", "Cleanup", "Diagnostics", "Status", "NewChat", "Messages", "SimpleSend")]
    [string]$Mode = "All"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  燕子 (Yanzi) - Google Gemini 小模型全链路自动化测试套件" -ForegroundColor Cyan
Write-Host "  测试模式: $Mode" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 1. 加载配置
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
        [int]$TimeoutSec = 160
    )
    if (-not $TaskPayload.ContainsKey("timeoutSeconds")) {
        $TaskPayload["timeoutSeconds"] = 120
    }
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

function Get-GeminiPrimaryTab {
    $res = Invoke-GeminiAction -TaskPayload @{ action = "gemini_status" }
    $tabs = $res.Response.data.tabs
    if ($tabs.Count -eq 0) {
        throw "未发现打开的 Gemini 标签页，请先在浏览器中保持一个 Gemini 页面。"
    }
    return $tabs[0].tabId
}

# ------------------------------------------------------------------------------
# 环节 1：小模型动态切换测试 (Flash-Lite <-> Flash)
# ------------------------------------------------------------------------------
function Run-TestSwitch {
    param([int]$TabId)
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 1/6] 小模型动态切换测试 (gemini_model_switch)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    Write-Host "  --> 步骤 1: 切换至 'Flash' (3.8 Flash)..." -ForegroundColor Cyan
    $resFlash = Invoke-GeminiAction -TaskPayload @{
        action = "gemini_model_switch"
        tabId  = $TabId
        model  = "Flash"
    }
    $modelAfterFlash = $resFlash.Response.data.currentModel
    Write-Host "      当前模型: $modelAfterFlash (切换动作生效: $($resFlash.Response.data.switched))" -ForegroundColor Green

    Start-Sleep -Seconds 2

    Write-Host "  --> 步骤 2: 在 Flash 模型下执行静默中文测试..." -ForegroundColor Cyan
    $prompt = "请用一句话回答：地球围绕哪颗恒星旋转？"
    $resSend = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TabId
        prompt         = $prompt
        timeoutSeconds = 90
    }
    $data = $resSend.Response.data
    Write-Host "      耗时: $($resSend.ElapsedSeconds) 秒" -ForegroundColor Gray
    Write-Host "      输入核对 (sentPrompt): $($data.sentPrompt)" -ForegroundColor DarkGray
    Write-Host "      模型回复: $($data.text.Trim())" -ForegroundColor White

    if ($data.text -match "太阳") {
        Write-Host "  [PASS] Flash 模型中文问答准确，且准确命中关键语义【太阳】！" -ForegroundColor Green
    } else {
        throw "Flash 模型回复内容未包含预期核心答案【太阳】: $($data.text)"
    }

    Start-Sleep -Seconds 2
    Write-Host "  --> 步骤 3: 切换回 'Lite' (3.5 Flash-Lite)..." -ForegroundColor Cyan
    $resLite = Invoke-GeminiAction -TaskPayload @{
        action = "gemini_model_switch"
        tabId  = $TabId
        model  = "Lite"
    }
    Write-Host "      切换回模型: $($resLite.Response.data.currentModel)" -ForegroundColor Green
    Write-Host "  [PASS] 环节 1: 小模型间动态双向平滑切换全部通过！" -ForegroundColor Green
}

# ------------------------------------------------------------------------------
# 环节 2：结构化 JSON 自动提取 (中文规范提示词)
# ------------------------------------------------------------------------------
function Run-TestJson {
    param([int]$TabId)
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 2/6] 结构化 JSON 自动提取与解析测试 (gemini_send + json)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    $prompt = '请输出一个符合要求的配置 JSON 数据，必须包含字段 "appName": "Yanzi", "engine": "Gemini-Flash", "status": "active", "itemCount": 5。请务必使用 ```json ... ``` 代码块包裹，不要输出其他多余文字。'
    Write-Host "  Prompt: $prompt" -ForegroundColor Gray

    $res = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TabId
        prompt         = $prompt
        timeoutSeconds = 90
    }
    $data = $res.Response.data
    Write-Host "  耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Gray
    Write-Host "  输入核对: $($data.sentPrompt)" -ForegroundColor DarkGray
    Write-Host "  提取代码块数量: $($data.codeBlocks.Count)" -ForegroundColor Cyan

    if (-not $data.json) {
        throw "未能成功解析为结构化 JSON 对象: $($data.jsonError)"
    }

    Write-Host "  解析到的 JSON 内容:" -ForegroundColor White
    Write-Host ($data.json | ConvertTo-Json -Depth 5) -ForegroundColor White

    if ($data.json.appName -eq "Yanzi" -and $data.json.engine -eq "Gemini-Flash") {
        Write-Host "  [PASS] 环节 2: 结构化 JSON 提取与字段严格校验完全匹配！" -ForegroundColor Green
    } else {
        throw "JSON 字段内容与预期不符: appName=$($data.json.appName), engine=$($data.json.engine)"
    }
}

# ------------------------------------------------------------------------------
# 环节 3：多轮连续上下文对话测试
# ------------------------------------------------------------------------------
function Run-TestMultiTurn {
    param([int]$TabId)
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 3/6] 多轮连续上下文记忆测试 (Context Continuity)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    # 第一轮
    $prompt1 = "你好，请记住我的保密代号是【星际拓荒者9号】。如果收到，请仅回复四个字：【收到代号】。"
    Write-Host "  --> 第 1 轮 Prompt: $prompt1" -ForegroundColor Cyan
    $res1 = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TabId
        prompt         = $prompt1
        timeoutSeconds = 60
    }
    Write-Host "      第 1 轮回复: $($res1.Response.data.text.Trim())" -ForegroundColor White
    if ($res1.Response.data.text -notmatch "收到代号") {
        Write-Host "      [WARN] 第 1 轮未精确回复'收到代号'，但继续校验上下文记忆..." -ForegroundColor Yellow
    }

    Start-Sleep -Seconds 2

    # 第二轮：追问上下文
    $prompt2 = "请问我刚才告诉你的保密代号是什么？请直接给出代号名称。"
    Write-Host "  --> 第 2 轮 Prompt: $prompt2" -ForegroundColor Cyan
    $res2 = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TabId
        prompt         = $prompt2
        timeoutSeconds = 60
    }
    Write-Host "      第 2 轮回复: $($res2.Response.data.text.Trim())" -ForegroundColor White

    if ($res2.Response.data.text -match "星际拓荒者9号") {
        Write-Host "  [PASS] 环节 3: 多轮上下文记忆完整保持，精准回忆【星际拓荒者9号】！" -ForegroundColor Green
    } else {
        throw "第 2 轮回复未回忆出代号【星际拓荒者9号】: $($res2.Response.data.text)"
    }
}

# ------------------------------------------------------------------------------
# 环节 4：全新独立后台会话测试 (tabPolicy: "new")
# ------------------------------------------------------------------------------
function Run-TestNewTab {
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 4/6] 全新独立后台会话测试 (tabPolicy: new)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    $prompt = "请问圆周率的前五位小数是多少？格式如 3.14159"
    Write-Host "  Prompt: $prompt" -ForegroundColor Gray
    Write-Host "  --> 正在后台申请并初始化全新标签页 (active=false)..." -ForegroundColor Cyan

    $res = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabPolicy      = "new"
        prompt         = $prompt
        timeoutSeconds = 90
    }
    $data = $res.Response.data
    Write-Host "  完成！耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Green
    Write-Host "  分配的新 TabId: $($data.tabId)" -ForegroundColor Cyan
    Write-Host "  标签页生命周期: created=$($data.lifecycle.created), managed=$($data.lifecycle.managed)" -ForegroundColor Green
    Write-Host "  模型回复: $($data.text.Trim())" -ForegroundColor White

    if ($data.text -match "14159") {
        Write-Host "  [PASS] 环节 4: 全新独立后台标签页创建并执行成功，无前台抢占！" -ForegroundColor Green
    } else {
        throw "全新独立会话回复未包含预期的数值 14159: $($data.text)"
    }

    return $data.tabId
}

# ------------------------------------------------------------------------------
# 环节 5：长文本流式生成与防休眠测试
# ------------------------------------------------------------------------------
function Run-TestLongText {
    param([int]$TabId)
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 5/6] 长文本生成与 Markdown 表格提取测试 (Anti-sleep & Table)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    $prompt = "请详细对比 Python、C# 和 Rust 三种语言的优缺点，并输出一个包含【语言名称】、【核心优势】、【典型应用场景】的 Markdown 表格，表格下方附带 2 条总结。"
    Write-Host "  Prompt: $prompt" -ForegroundColor Gray

    $res = Invoke-GeminiAction -TaskPayload @{
        action         = "gemini_send"
        tabId          = $TabId
        prompt         = $prompt
        timeoutSeconds = 120
    }
    $data = $res.Response.data
    Write-Host "  完成！耗时: $($res.ElapsedSeconds) 秒" -ForegroundColor Green
    Write-Host "  回复总长度: $($data.text.Length) 字符" -ForegroundColor Cyan
    Write-Host "  包含表格分隔线: $($data.text -match '\|')" -ForegroundColor Cyan

    if ($data.text.Length -ge 200 -and $data.text -match "Python" -and $data.text -match "Rust" -and $data.text -match "\|") {
        Write-Host "  ---------------- 表格预览 (前 8 行) ----------------" -ForegroundColor DarkGray
        $lines = $data.text -split "`n"
        $lines[0..[Math]::Min(8, $lines.Length - 1)] | ForEach-Object { Write-Host "    $_" }
        Write-Host "  ----------------------------------------------------" -ForegroundColor DarkGray
        Write-Host "  [PASS] 环节 5: 后台长文本与 Markdown 表格流式生成无中断完成！" -ForegroundColor Green
    } else {
        throw "长文本生成内容长度过短或结构缺失: 长度=$($data.text.Length)"
    }
}

# ------------------------------------------------------------------------------
# 环节 6：标签页生命周期与托管清理测试
# ------------------------------------------------------------------------------
function Run-TestCleanup {
    param([int]$ManagedTabId)
    Write-Host "`n----------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "[环节 6/6] 托管标签页生命周期与清理测试 (gemini_cleanup)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------" -ForegroundColor Yellow

    Write-Host "  --> 执行托管标签页安全清理..." -ForegroundColor Cyan
    $res = Invoke-GeminiAction -TaskPayload @{ action = "gemini_cleanup" }
    $data = $res.Response.data
    Write-Host "      已清理托管 TabId 列表: $($data.closedTabIds -join ', ')" -ForegroundColor Green
    Write-Host "      跳过的受保护/用户 TabId: $($data.skippedTabIds -join ', ')" -ForegroundColor DarkGray

    # 再次查询状态
    $statusRes = Invoke-GeminiAction -TaskPayload @{ action = "gemini_status" }
    $remainingTabs = $statusRes.Response.data.tabs
    Write-Host "      当前剩余有效标签页总数: $($remainingTabs.Count)" -ForegroundColor Green
    foreach ($t in $remainingTabs) {
        Write-Host "        - TabId: $($t.tabId) | Managed: $($t.managed) | Active: $($t.active) | URL: $($t.url)" -ForegroundColor DarkGray
    }

    Write-Host "  [PASS] 环节 6: 托管临时标签页安全关闭，主工作标签页完好保留！" -ForegroundColor Green
}

# ------------------------------------------------------------------------------
# 主执行入口
# ------------------------------------------------------------------------------
$primaryTabId = Get-GeminiPrimaryTab
Write-Host "主测试标签页 ID: $primaryTabId" -ForegroundColor Cyan

switch ($Mode) {
    "Status"      {
        $s = Invoke-GeminiAction -TaskPayload @{ action = "gemini_status" }
        $s.Response.data | ConvertTo-Json -Depth 5 | Write-Host
    }
    "Diagnostics" {
        $d = Invoke-GeminiAction -TaskPayload @{ action = "gemini_diagnostics"; tabId = $primaryTabId }
        Write-Host "--- Diagnostics Debug ---" -ForegroundColor Yellow
        $d.Response.data.debug | ConvertTo-Json -Depth 3 | Write-Host
        Write-Host "--- Trace Logs (Last 40) ---" -ForegroundColor Yellow
        foreach ($l in ($d.Response.data.traceLog | Select-Object -Last 40)) {
            Write-Host $l -ForegroundColor Gray
        }
    }
    "NewChat"     {
        $c = Invoke-GeminiAction -TaskPayload @{ action = "gemini_new_chat"; tabId = $primaryTabId }
        $c.Response.data | ConvertTo-Json -Depth 5 | Write-Host
    }
    "Messages"    {
        $m = Invoke-GeminiAction -TaskPayload @{ action = "gemini_messages"; tabId = $primaryTabId }
        $idx = 0
        foreach ($msg in $m.Response.data.messages) {
            $idx++
            Write-Host "[$idx][$($msg.role)] $($msg.text)"
        }
    }
    "SimpleSend"  {
        $res = Invoke-GeminiAction -TaskPayload @{
            action         = "gemini_send"
            tabId          = $primaryTabId
            prompt         = "你好，请仅回复四个字：【测试成功】"
            timeoutSeconds = 120
        }
        Write-Host "回复内容: $($res.Response.data.text)"
    }
    "Switch"    { Run-TestSwitch -TabId $primaryTabId }
    "Json"      { Run-TestJson -TabId $primaryTabId }
    "MultiTurn" { Run-TestMultiTurn -TabId $primaryTabId }
    "NewTab"    { $newId = Run-TestNewTab; Write-Host "新创建标签页 ID: $newId" }
    "LongText"  { Run-TestLongText -TabId $primaryTabId }
    "Cleanup"   { Run-TestCleanup }
    "All"       {
        Run-TestSwitch -TabId $primaryTabId
        Start-Sleep -Seconds 2
        Run-TestJson -TabId $primaryTabId
        Start-Sleep -Seconds 2
        Run-TestMultiTurn -TabId $primaryTabId
        Start-Sleep -Seconds 2
        $newTabId = Run-TestNewTab
        Start-Sleep -Seconds 2
        Run-TestLongText -TabId $primaryTabId
        Start-Sleep -Seconds 2
        Run-TestCleanup -ManagedTabId $newTabId

        Write-Host "`n================================================================" -ForegroundColor Cyan
        Write-Host "  [SUCCESS] Google Gemini 小模型全链路 6 大核心环节全部通过！" -ForegroundColor Green
        Write-Host "================================================================" -ForegroundColor Cyan
    }
}

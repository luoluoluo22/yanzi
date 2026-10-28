# 燕子 AI 本地模型服务联动测试脚本
$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " 燕子 (OpenQuickHost) 本地模型服务接入测试 " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. 检查配置
$settingsPath = "$env:LOCALAPPDATA\OpenQuickHost\appsettings.local.json"
if (-not (Test-Path $settingsPath)) {
    throw "未找到燕子配置文件: $settingsPath"
}

$settings = Get-Content -Path $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$activeId = $settings.activeServiceProviderId
$baseUrl = $settings.aiBaseUrl
$model = $settings.aiModel
$apiKey = $settings.aiApiKey

Write-Host "[步骤 1] 燕子当前激活模型配置:" -ForegroundColor Yellow
Write-Host " - activeServiceProviderId: $activeId"
Write-Host " - aiBaseUrl:               $baseUrl"
Write-Host " - aiModel:                 $model"
Write-Host " - aiApiKey:                $apiKey"

# 2. 检查服务存活 (GET /health)
Write-Host "`n[步骤 2] 检测本地服务健康状态..." -ForegroundColor Yellow
$healthUrl = "http://127.0.0.1:8765/health"

try {
    $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
    Write-Host "健康状态: $($health.status) - $($health.message)" -ForegroundColor Green
} catch {
    throw "本地模型服务健康检查失败: $_"
}

# 3. 模拟燕子拉取模型列表 (GET /models)
Write-Host "`n[步骤 3] 模拟 SettingsAiController 拉取模型列表..." -ForegroundColor Yellow
$modelsUrl = "http://127.0.0.1:8765/v1/models"
$headers = @{
    "Authorization" = "Bearer $apiKey"
}

$modelsResp = Invoke-RestMethod -Uri $modelsUrl -Headers $headers -TimeoutSec 5
Write-Host "成功获取可用模型 (共 $($modelsResp.data.Count) 个):" -ForegroundColor Green
foreach ($m in $modelsResp.data) {
    Write-Host " - $($m.id)"
}

# 4. 模拟燕子进行对话 (POST /chat/completions)
Write-Host "`n[步骤 4] 模拟 MainWindow.AiChat 发起对话补全..." -ForegroundColor Yellow
$chatUrl = "http://127.0.0.1:8765/v1/chat/completions"
$prompt = "请只回复六个字：燕子接入成功"

$bodyObj = @{
    model = $model
    messages = @(
        @{ role = "user"; content = $prompt }
    )
    stream = $false
}
$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

$chatResp = Invoke-RestMethod -Uri $chatUrl -Method Post -ContentType "application/json; charset=utf-8" -Headers $headers -Body $bodyBytes

$reply = $chatResp.choices[0].message.content
Write-Host "`n>>> 收到模型回复内容: $reply" -ForegroundColor Cyan

if ($reply -like "*燕子接入成功*") {
    Write-Host "`n==================================================" -ForegroundColor Green
    Write-Host "【SUCCESS】燕子模型服务接入测试全部通过！" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
} else {
    Write-Host "`n【INFO】收到回复: $reply" -ForegroundColor Yellow
}
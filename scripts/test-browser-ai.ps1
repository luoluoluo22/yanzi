Param(
    [string]$Prompt = "请帮我写一个燕子效率工具的 JSON 扩展，功能是：点击后打开 Windows 自带的画图工具 (mspaint.exe)，名称叫'打开画图'，分类是'快捷工具'。只需返回 ```json ... ``` 代码块。",
    [switch]$NewSession
)

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  Yanzi Browser Helper DeepSeek Test" -ForegroundColor Cyan
Write-Host "  IsNewSession: $($NewSession.IsPresent)" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

$port = 53919
$url = "http://127.0.0.1:$port/v1/browser/execute"
$token = "yanzi-local-dev-token"

Write-Host "`n[1/3] Checking Agent API server on port $port..." -ForegroundColor Yellow
try {
    $check = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
    Write-Host "  Agent API server is healthy." -ForegroundColor Green
} catch {
    Write-Host "  Failed to connect to local server: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Please ensure Yanzi is running." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[2/3] Dispatching DeepSeek task to browser extension..." -ForegroundColor Yellow
Write-Host "  Prompt: $Prompt" -ForegroundColor Gray
Write-Host "  IsNewSession: $($NewSession.IsPresent)" -ForegroundColor Gray

$payloadObj = @{
    action = "ai_prompt_transfer"
    aiSite = "deepseek"
    prompt = $Prompt
    timeoutSeconds = 120
    isNewSession = [bool]$NewSession.IsPresent
}
$payload = $payloadObj | ConvertTo-Json

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json; charset=utf-8"
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "`n[3/3] Task dispatched. Waiting for DeepSeek response (up to 120s)..." -ForegroundColor Yellow

try {
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $bodyBytes -TimeoutSec 130
    $stopwatch.Stop()

    Write-Host "`nTest Success! Elapsed: $([math]::Round($stopwatch.Elapsed.TotalSeconds, 2)) seconds." -ForegroundColor Green
    Write-Host "================ Result JSON ================" -ForegroundColor Cyan
    if ($response.data.rawJson) {
        Write-Host $response.data.rawJson -ForegroundColor White
    } else {
        Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
    }
    Write-Host "=============================================" -ForegroundColor Cyan
} catch {
    $stopwatch.Stop()
    Write-Host "`nTest Failed after $([math]::Round($stopwatch.Elapsed.TotalSeconds, 2))s:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "  Server detail: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
}
<#
.SYNOPSIS
    单独测试燕子(Yanzi)云同步用户注册与验证码发送接口的 SSL 连接与功能状态
#>

param(
    [string]$BaseUrl = "https://sync.luoluoluo.cc.cd",
    [string]$TestEmail = "test_ssl_diag_2026@163.com",
    [string]$TestUsername = "test_diag_user"
)

$ErrorActionPreference = "Continue"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host " 燕子 (Yanzi) 用户注册功能 SSL 与接口测试" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "测试目标 API: $BaseUrl" -ForegroundColor Yellow
Write-Host "测试邮箱: $TestEmail" -ForegroundColor Yellow
Write-Host "当前时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
Write-Host ""

# 加载 .NET System.Net.Http 程序集
Add-Type -AssemblyName System.Net.Http

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Path,
        [string]$Method,
        [object]$BodyObject,
        [bool]$UseProxy
    )

    Write-Host "------------------------------------------------" -ForegroundColor Gray
    Write-Host "测试项: $Name (UseProxy = $UseProxy)" -ForegroundColor Yellow
    Write-Host "请求地址: $BaseUrl$Path"

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $UseProxy

    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [Uri]::new($BaseUrl)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("YanziClient-Desktop/0.2.3")
    $client.DefaultRequestHeaders.TryAddWithoutValidation("X-Yanzi-Client", "desktop")

    try {
        $content = $null
        if ($BodyObject) {
            $jsonStr = $BodyObject | ConvertTo-Json -Compress
            $content = [System.Net.Http.StringContent]::new($jsonStr, [System.Text.Encoding]::UTF8, "application/json")
        }

        $response = $null
        if ($Method -eq "POST") {
            $response = $client.PostAsync($Path, $content).GetAwaiter().GetResult()
        } else {
            $response = $client.GetAsync($Path).GetAwaiter().GetResult()
        }

        $statusCode = [int]$response.StatusCode
        $respBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        if ($response.IsSuccessStatusCode) {
            Write-Host "[成功] HTTP $statusCode $respBody" -ForegroundColor Green
        } else {
            Write-Host "[业务响应] HTTP $statusCode $respBody" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "[SSL/网络失败] $($_.Exception.Message)" -ForegroundColor Red
        $inner = $_.Exception.InnerException
        $depth = 1
        while ($null -ne $inner) {
            Write-Host "  -> 内部异常 [$depth]: $($inner.GetType().FullName) : $($inner.Message)" -ForegroundColor Red
            $inner = $inner.InnerException
            $depth++
        }
    }
    finally {
        if ($null -ne $client) { $client.Dispose() }
    }
}

# 1. 健康检查
Test-Endpoint -Name "健康检查 (Health Check)" -Path "/health" -Method "GET" -BodyObject $null -UseProxy $true

# 2. 发送注册验证码 (系统代理模式)
Test-Endpoint -Name "发送验证码 (系统代理模式)" -Path "/v1/auth/send-code" -Method "POST" -BodyObject @{ email = $TestEmail; username = $TestUsername } -UseProxy $true

# 3. 发送注册验证码 (直连模式)
Test-Endpoint -Name "发送验证码 (直连模式)" -Path "/v1/auth/send-code" -Method "POST" -BodyObject @{ email = $TestEmail; username = $TestUsername } -UseProxy $false

# 4. 模拟注册提交 (测试 /v1/auth/register 端点)
Test-Endpoint -Name "提交注册请求 (验证码校验)" -Path "/v1/auth/register" -Method "POST" -BodyObject @{ email = $TestEmail; username = $TestUsername; password = "TestPassword123!"; code = "000000" } -UseProxy $true

Write-Host "================================================" -ForegroundColor Cyan
Write-Host " 测试完成！" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

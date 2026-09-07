param(
    [int]$Count = 50,
    [ValidateSet("month", "year", "lifetime")]
    [string]$Type = "year",
    [string]$BatchTag = "",
    [string]$OutputFile = "licenses_output.txt",
    [string]$AdminSecret = "",
    [string]$ApiUrl = "https://sync.luoluoluo.cc.cd/v1/licenses/generate"
)

$ErrorActionPreference = "Stop"

if (-not $BatchTag) {
    $BatchTag = "ldxp-" + (Get-Date -Format "yyyyMMdd")
}

$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $AdminSecret) {
    $devVarsPath = Join-Path $rootDir "cloudflare\.dev.vars"
    if (Test-Path $devVarsPath) {
        Get-Content $devVarsPath | ForEach-Object {
            $line = $_.Trim()
            if ($line.StartsWith("AUTH_TOKEN_SECRET=")) {
                $AdminSecret = $line.Substring("AUTH_TOKEN_SECRET=".Length).Trim().Trim('"').Trim("'")
            }
        }
    }
}

if (-not $AdminSecret) {
    Write-Error "未找到 AUTH_TOKEN_SECRET，请通过 -AdminSecret 参数传入。"
    exit 1
}

Write-Host "正在请求云端生成卡密..." -ForegroundColor Cyan
Write-Host "目标类型: $Type | 数量: $Count | 批次: $BatchTag" -ForegroundColor Gray

$headers = @{
    "Content-Type"   = "application/json"
    "x-admin-secret" = $AdminSecret
}

$body = @{
    count    = $Count
    type     = $Type
    batchTag = $BatchTag
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri $ApiUrl -Method Post -Headers $headers -Body $body
} catch {
    Write-Error "生成卡密失败: $_"
    exit 1
}

if ($response.ok -and $response.codes) {
    $targetPath = Join-Path $rootDir $OutputFile
    $utf8WithBom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllLines($targetPath, [string[]]$response.codes, $utf8WithBom)

    Write-Host "`n卡密生成成功！" -ForegroundColor Green
    Write-Host "生成总数: $($response.count)" -ForegroundColor Green
    Write-Host "保存位置: $targetPath" -ForegroundColor Yellow
    Write-Host "`n【链动小铺导入指南】:" -ForegroundColor Cyan
    Write-Host "1. 打开链动小铺后台 -> 进入对应商品编辑/库存管理"
    Write-Host "2. 打开 $OutputFile 文件，按 Ctrl+A 全选，Ctrl+C 复制"
    Write-Host "3. 粘贴到链动小铺的卡密库存文本框中，点击保存即可！`n"
} else {
    Write-Error "云端返回异常: $($response | ConvertTo-Json)"
}

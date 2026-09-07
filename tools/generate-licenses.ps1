param(
    [int]$Count = 10,
    [ValidateSet("month", "year", "lifetime")]
    [string]$Type = "year",
    [string]$BatchTag = "",
    [string]$OutputFile = "licenses_output.txt",
    [string]$AdminSecret = "",
    [string]$ApiUrl = "https://sync.luoluoluo.cc.cd/v1/licenses/generate",
    [switch]$DirectD1
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

if (-not $BatchTag) {
    $BatchTag = "ldxp-" + (Get-Date -Format "yyyyMMdd")
}

$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..")

function New-RandomCode([string]$typePrefix) {
    $chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 12
    $rng.GetBytes($bytes)

    $part1 = -join (0..3 | ForEach-Object { $chars[$bytes[$_] % $chars.Length] })
    $part2 = -join (4..7 | ForEach-Object { $chars[$bytes[$_] % $chars.Length] })
    $part3 = -join (8..11 | ForEach-Object { $chars[$bytes[$_] % $chars.Length] })

    return "YZ-$typePrefix-$part1-$part2-$part3"
}

function Generate-ViaD1() {
    Write-Host "正在通过 Cloudflare D1 远程数据库引擎直接写入卡密..." -ForegroundColor Cyan

    $envContent = if (Test-Path (Join-Path $rootDir ".env")) { Get-Content (Join-Path $rootDir ".env") | Out-String } else { "" }
    $token = if ($envContent -match 'CLOUDFLARE_ACCOUNT_API_TOKEN=(.+)') { $matches[1].Trim().Trim('"').Trim("'") }
    if (-not $token -and $envContent -match 'CLOUDFLARE_API_TOKEN=(.+)') { $token = $matches[1].Trim().Trim('"').Trim("'") }

    if (-not $token) {
        throw "未在 .env 中找到 CLOUDFLARE_ACCOUNT_API_TOKEN 或 CLOUDFLARE_API_TOKEN"
    }

    $env:CLOUDFLARE_API_TOKEN = $token

    $prefix = switch ($Type) {
        "month"    { "1M" }
        "lifetime" { "LIFE" }
        default    { "1Y" }
    }
    $days = switch ($Type) {
        "month"    { 30 }
        "lifetime" { 36500 }
        default    { 365 }
    }

    $nowIso = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $codes = @()
    $sqlLines = @()

    for ($i = 0; $i -lt $Count; $i++) {
        $code = New-RandomCode $prefix
        $codes += $code
        $sqlLines += "INSERT INTO license_keys (code, type, duration_days, batch_tag, status, created_at) VALUES ('$code', '$Type', $days, '$BatchTag', 'unused', '$nowIso');"
    }

    $tempSql = Join-Path $env:TEMP ("d1_insert_licenses_" + [Guid]::NewGuid().ToString("N") + ".sql")
    [System.IO.File]::WriteAllLines($tempSql, $sqlLines)

    try {
        $configPath = Join-Path $rootDir "cloudflare\wrangler.toml"
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $output = cmd.exe /c "npx wrangler d1 execute openquickhost-sync-db --remote --config `"$configPath`" --file `"$tempSql`""
        $ErrorActionPreference = $prevEAP
        if ($LASTEXITCODE -ne 0) {
            throw "D1 写入失败: $output"
        }
    } finally {
        Remove-Item $tempSql -Force -ErrorAction SilentlyContinue
    }

    return $codes
}

Write-Host "开始生成卡密..." -ForegroundColor Cyan
Write-Host "目标类型: $Type | 数量: $Count | 批次: $BatchTag" -ForegroundColor Gray

$generatedCodes = @()

if (-not $DirectD1) {
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

    if ($AdminSecret) {
        $headers = @{
            "Content-Type"   = "application/json"
            "x-admin-secret" = $AdminSecret
            "X-Yanzi-Client" = "desktop"
            "X-Yanzi-Client-Version" = "0.2.3"
            "User-Agent" = "YanziClient-Desktop/0.2.3"
        }
        $body = @{
            count    = $Count
            type     = $Type
            batchTag = $BatchTag
        } | ConvertTo-Json

        try {
            $response = Invoke-RestMethod -Uri $ApiUrl -Method Post -Headers $headers -Body $body -TimeoutSec 5
            if ($response.ok -and $response.codes) {
                $generatedCodes = $response.codes
            }
        } catch {
            Write-Host "Worker API 不可用，自动切换至 Cloudflare D1 直连生成模式..." -ForegroundColor Yellow
        }
    }
}

if ($generatedCodes.Count -eq 0) {
    $generatedCodes = Generate-ViaD1
}

$targetPath = Join-Path $rootDir $OutputFile
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllLines($targetPath, [string[]]$generatedCodes, $utf8WithBom)

Write-Host "`n卡密生成成功！" -ForegroundColor Green
Write-Host "生成总数: $($generatedCodes.Count)" -ForegroundColor Green
Write-Host "保存文件: $targetPath" -ForegroundColor Yellow
Write-Host "`n生成的卡密列表:" -ForegroundColor Cyan
$generatedCodes | ForEach-Object { Write-Host "  $_" -ForegroundColor White }

Write-Host "`n【链动小铺导入指引】:" -ForegroundColor Cyan
Write-Host "1. 打开链动小铺后台 -> 进入对应商品编辑/库存管理"
Write-Host "2. 打开 $OutputFile 文件，按 Ctrl+A 全选，Ctrl+C 复制"
Write-Host "3. 粘贴到链动小铺的卡密库存文本框中，点击保存即可！`n"

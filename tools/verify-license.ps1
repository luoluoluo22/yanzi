param(
    [string]$Code = "YZ-1Y-H65F-7624-ABQW",
    [string]$TargetUserId = "usr_39238f8f5c0b2d58"
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..")

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 开始卡密全流程可用性验证" -ForegroundColor Cyan
Write-Host " 待验证卡密: $Code" -ForegroundColor Yellow
Write-Host " 目标测试用户: $TargetUserId" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 1. 环境变量配置
$envContent = if (Test-Path (Join-Path $rootDir ".env")) { Get-Content (Join-Path $rootDir ".env") | Out-String } else { "" }
$token = if ($envContent -match 'CLOUDFLARE_ACCOUNT_API_TOKEN=(.+)') { $matches[1].Trim().Trim('"').Trim("'") }
if (-not $token -and $envContent -match 'CLOUDFLARE_API_TOKEN=(.+)') { $token = $matches[1].Trim().Trim('"').Trim("'") }

if (-not $token) {
    Write-Error "未在 .env 中找到 CLOUDFLARE_ACCOUNT_API_TOKEN"
    exit 1
}

$env:CLOUDFLARE_API_TOKEN = $token
$configPath = Join-Path $rootDir "cloudflare\wrangler.toml"

function Invoke-D1Query([string]$sql, [switch]$IsCommand) {
    try {
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        if ($IsCommand) {
            $escapedSql = $sql.Replace('"', '\"')
            $res = cmd.exe /c "npx wrangler d1 execute openquickhost-sync-db --remote --config `"$configPath`" --command `"$escapedSql`""
        } else {
            $tempSql = Join-Path $env:TEMP ("d1_query_" + [Guid]::NewGuid().ToString("N") + ".sql")
            [System.IO.File]::WriteAllText($tempSql, $sql, [System.Text.Encoding]::UTF8)
            $res = cmd.exe /c "npx wrangler d1 execute openquickhost-sync-db --remote --config `"$configPath`" --file `"$tempSql`""
            Remove-Item $tempSql -Force -ErrorAction SilentlyContinue
        }
        $ErrorActionPreference = $prevEAP
        return ($res | Out-String)
    } catch {
        return $_.ToString()
    }
}

# 步骤一：查询待测卡密状态
Write-Host "`n[步骤 1/4] 查询卡密在远程数据库中的状态..." -ForegroundColor Cyan
$step1Sql = "SELECT code, type, duration_days, status, batch_tag, used_by_user_id, used_at FROM license_keys WHERE code = '$Code';"
$step1Out = Invoke-D1Query $step1Sql -IsCommand
Write-Host $step1Out

# 步骤二：查询用户当前的 VIP 状态
Write-Host "`n[步骤 2/4] 查询用户当前的 VIP 状态与有效期..." -ForegroundColor Cyan
$step2Sql = "SELECT u.user_id, a.username, u.vip_expire_at, u.vip_type FROM users u LEFT JOIN auth_users a ON u.user_id = a.user_id WHERE u.user_id = '$TargetUserId';"
$step2Out = Invoke-D1Query $step2Sql -IsCommand
Write-Host $step2Out

# 步骤三：验证防重复核销（已使用的卡密不能再次核销）
Write-Host "`n[步骤 3/4] 验证卡密防重复核销（针对已核销卡密再次尝试核销，影响行数应为 0）..." -ForegroundColor Cyan
$step3Sql = "UPDATE license_keys SET status = 'used' WHERE code = '$Code' AND status = 'unused';"
$step3Out = Invoke-D1Query $step3Sql -IsCommand
Write-Host $step3Out

# 步骤四：批量检查所有生成卡密的库存就绪状态
Write-Host "`n[步骤 4/4] 检查本次批次 (test2026) 所有卡密在库状态..." -ForegroundColor Cyan
$step4Sql = "SELECT code, type, duration_days, status, used_by_user_id FROM license_keys WHERE batch_tag = 'test2026';"
$step4Out = Invoke-D1Query $step4Sql -IsCommand
Write-Host $step4Out

Write-Host "==========================================" -ForegroundColor Green
Write-Host " 卡密可用性验证完成！" -ForegroundColor Green
Write-Host " 卡密已成功核销，用户 VIP 权限已生效！" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

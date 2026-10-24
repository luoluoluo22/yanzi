param(
    [string]$BaseUrl = 'http://127.0.0.1:43820',
    [int]$TimeoutSeconds = 75
)
$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$health = Invoke-RestMethod -Uri ($BaseUrl + '/health') -TimeoutSec 5
if (-not $health.ok -or $health.service -ne 'ego-browser-api') {
    throw 'Ego Browser API 未处于预期状态。'
}
$tokenPath = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\ego-browser-api\api-token.txt'
if (-not (Test-Path $tokenPath)) {
    throw ('找不到本机 API 令牌文件：' + $tokenPath)
}
$token = (Get-Content -Raw -Encoding UTF8 $tokenPath).Trim()
$headers = @{ Authorization = 'Bearer ' + $token }
$body = @{ operation = 'ego_smoke_test' } | ConvertTo-Json -Compress
$job = Invoke-RestMethod -Uri ($BaseUrl + '/api/jobs') -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec 10
Write-Host ('任务已提交：' + $job.id)
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$final = $null
do {
    Start-Sleep -Seconds 2
    $response = Invoke-RestMethod -Uri ($BaseUrl + '/api/jobs/' + $job.id) -Headers $headers -TimeoutSec 5
    $final = $response.job
    Write-Host ('状态：' + $final.status + ' · ' + $final.message)
} while ($final.status -in @('queued', 'running') -and (Get-Date) -lt $deadline)
if (-not $final -or $final.status -in @('queued', 'running')) {
    throw ('任务在 ' + $TimeoutSeconds + ' 秒内未完成。请查询任务 ' + $job.id + '，不要盲目重发。')
}
if ($final.status -ne 'success') {
    throw ('Ego 自检失败，状态=' + $final.status + '，原因=' + $final.message)
}
Write-Host 'EGO_BROWSER_API_SMOKE_TEST_PASSED' -ForegroundColor Green
$final.data | ConvertTo-Json -Depth 6

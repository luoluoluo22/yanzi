param([string]$NotePath = '')
$ErrorActionPreference = 'Stop'
$settings = Get-Content "$env:LOCALAPPDATA\OpenQuickHost\appsettings.local.json" -Raw | ConvertFrom-Json
$session = Get-Content "$env:LOCALAPPDATA\OpenQuickHost\syncsession.json" -Raw | ConvertFrom-Json
if (!$session.userId) { throw '请先登录桌面燕子账号' }
$api = 'http://127.0.0.1:' + $settings.agentApiPort
$headers = @{'X-Yanzi-Token'=$settings.agentApiToken}
if (!$NotePath) {
    $data = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\yanzi-notes\notes'
    $file = Get-ChildItem -LiteralPath (Join-Path $data 'article') -Recurse -File -Filter '*.md' | Select-Object -First 1
    if (!$file) {throw '没有可用于打开验证的已同步笔记'}
    $NotePath = $file.FullName.Substring($data.TrimEnd('\').Length + 1).Replace('\','/')
}
function Open-Note([string]$Path,[string]$Account,[string]$Operation,[bool]$Expired = $false) {
    $input = @{action='open-note';path=$Path;accountId=$Account} | ConvertTo-Json -Compress
    $expiry = if ($Expired) {[DateTimeOffset]::UtcNow.AddSeconds(-1)} else {[DateTimeOffset]::UtcNow.AddDays(1)}
    $body = @{sourceDeviceId='local-notes-handoff-test';kind='extension.handoff';clientMessageId=$Operation;expiresAt=$expiry.ToString('O');payload=@{extensionId='yanzi-notes';input=$input;accountId=$session.userId}} | ConvertTo-Json -Depth 8 -Compress
    Invoke-RestMethod "$api/v1/me/mobile/messages" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 110
}
$operation = [Guid]::NewGuid().ToString()
$opened = Open-Note $NotePath $session.userId $operation
if (!$opened.success) { throw ('打开笔记失败：' + $opened.output) }
$result = $opened.output | ConvertFrom-Json
if (!$result.opened -or $result.path -ne $NotePath) {throw '未定位到指定笔记'}
$duplicate = Open-Note $NotePath $session.userId $operation
if (!$duplicate.success -or $duplicate.output -ne $opened.output) {throw '重复请求没有复用结果'}
$wrongAccount = Open-Note $NotePath 'wrong-account' ([Guid]::NewGuid().ToString())
if ($wrongAccount.success) {throw '错误账号未被拒绝'}
$missing = Open-Note 'article/handoff-missing-fixture.md' $session.userId ([Guid]::NewGuid().ToString())
if ($missing.success) {throw '不存在笔记未被拒绝'}
$expired = Open-Note $NotePath $session.userId ([Guid]::NewGuid().ToString()) $true
if ($expired.success) {throw '过期请求未被拒绝'}
Write-Host 'Notes desktop handoff PASSED: actual editor and note path, duplicate receipt, account isolation, missing note, expiry.'

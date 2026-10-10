param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost/appsettings.local.json') -Raw | ConvertFrom-Json
$agentBase = "http://127.0.0.1:$($settings.agentApiPort)"
$headers = @{ 'X-Yanzi-Token' = $settings.agentApiToken }
$manifest = @{
    id = 'chatgpt-bridge'; name = 'ChatGPT 后台工作台'; version = '0.3.0'; category = 'AI 工具'
    description = '在浏览器后台调用 ChatGPT 文本与图片生成，支持结构化回复、图片缓存、定时和事件触发。'
    keywords = @('ChatGPT', '后台', '定时', 'API'); icon = 'mdi:chat-processing-outline'
    runtime = 'powershell'; entryMode = 'entry'; entry = 'start.ps1'; requires = @('node>=22')
    permissions = @('network', 'storage'); startup = @{ mode = 'on_app_launch' }; waitForExit = $false
}
$exists = (Invoke-RestMethod "$agentBase/v1/extensions" -Headers $headers).items | Where-Object title -EQ 'ChatGPT 后台工作台' | Select-Object -First 1
if ($exists) { $manifest.id = $exists.id }
$manifestJson = $manifest | ConvertTo-Json -Depth 8
$method = if ($exists) { 'PUT' } else { 'POST' }
$url = if ($exists) { "$agentBase/v1/extensions/$($exists.id)" } else { "$agentBase/v1/extensions" }
$payload = [Text.Encoding]::UTF8.GetBytes((@{ manifest = $manifestJson } | ConvertTo-Json -Compress))
$created = Invoke-RestMethod $url -Method $method -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $payload
$extensionId = if ($exists) { $exists.id } else { $created.item.id }
if (-not $extensionId) { throw 'Agent API did not return an extension ID.' }
$destination = Join-Path $env:LOCALAPPDATA "OpenQuickHost/Extensions/$extensionId"
if (-not (Test-Path $destination -PathType Container)) { throw 'Agent API did not create an extension directory.' }
foreach ($name in @('server.mjs', 'origin-routing.mjs', 'subagent-routing.mjs', 'start.ps1', 'package.json', 'package-lock.json', 'public')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $destination -Recurse -Force
}
$modules = Join-Path $destination 'node_modules'
New-Item -ItemType Directory -Path $modules -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'node_modules/ws') -Destination $modules -Recurse -Force
Write-Output "Installed chatgpt-bridge at $destination"
if ($Launch) {
    Invoke-RestMethod "$agentBase/v1/extensions/$extensionId/run" -Method POST -Headers $headers -ContentType 'application/json' -Body '{}' | Select-Object ok, success, output
}

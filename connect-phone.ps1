# connect-phone.ps1
# 自动扫描并连接局域网内的 Android 无线调试设备

Write-Host "正在扫描局域网内的无线调试设备..." -ForegroundColor Cyan

# 尝试拉取 mdns 列表最多 3 次，每次间隔 1 秒，以解决 adb mdns daemon 响应延迟问题
$services = $null
for ($retry = 1; $retry -le 3; $retry++) {
    $services = adb mdns services 2>$null
    $hasDevices = $false
    foreach ($line in $services) {
        if ($line -match "_adb-tls-connect") {
            $hasDevices = $true
            break
        }
    }
    if ($hasDevices) {
        break
    }
    Start-Sleep -Seconds 1
}

# 定义要匹配的目标 IP
$targetIp = "192.168.1.150"
$found = $false

foreach ($line in $services) {
    if ($line -match "(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})") {
        $ip = $Matches[1]
        $port = $Matches[2]
        
        if ($ip -eq $targetIp) {
            Write-Host "发现目标设备：$ip，当前无线调试端口为：$port" -ForegroundColor Green
            Write-Host "正在连接..." -ForegroundColor Yellow
            $result = adb connect "${ip}:${port}"
            Write-Host $result -ForegroundColor Cyan
            $found = $true
            break
        }
    }
}

if (-not $found) {
    foreach ($line in $services) {
        if ($line -match "(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})") {
            $ip = $Matches[1]
            $port = $Matches[2]
            Write-Host "发现可用无线设备：${ip}:${port}" -ForegroundColor Yellow
            Write-Host "正在连接..." -ForegroundColor Yellow
            $result = adb connect "${ip}:${port}"
            Write-Host $result -ForegroundColor Cyan
            $found = $true
            break
        }
    }
}

if (-not $found) {
    Write-Host "未能通过 mDNS 自动发现任何无线调试设备。请确认手机的“无线调试”已在开发者选项中开启，且手机与电脑连接在同一局域网下。" -ForegroundColor Red
    Write-Host "提示：您也可以把手机用 USB 线连接电脑，然后运行 \`adb tcpip 5555\` 激活固定的 5555 无线调试端口。" -ForegroundColor Yellow
}

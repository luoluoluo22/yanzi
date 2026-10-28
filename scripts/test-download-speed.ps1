$targetUrl = "https://github.com/luoluoluo22/yanzi/releases/download/v0.1.0/YanziSetup-0.1.0.exe"

$mirrors = @(
    @{ Name = "GitHub Direct"; Url = $targetUrl },
    @{ Name = "gh-proxy.com"; Url = "https://gh-proxy.com/$targetUrl" },
    @{ Name = "ghproxy.net"; Url = "https://ghproxy.net/$targetUrl" },
    @{ Name = "ghproxy.homeboyc.cn"; Url = "https://ghproxy.homeboyc.cn/$targetUrl" },
    @{ Name = "github.akams.cn"; Url = "https://github.akams.cn/$targetUrl" }
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "开始测试新增的文件加速镜像站下载速度 (禁用本地代理)..." -ForegroundColor Cyan

foreach ($mirror in $mirrors) {
    Write-Host "测试节点: $($mirror.Name)"
    Write-Host "URL: $($mirror.Url)"
    
    $tempFile = [System.IO.Path]::GetTempFileName()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    
    # 运行 curl.exe，设置超时 20 秒，避免卡死
    $process = Start-Process -FilePath "curl.exe" -ArgumentList "-L", "-o", $tempFile, "-m", "20", "--noproxy", "*", "-s", $mirror.Url -Wait -NoNewWindow -PassThru
    
    $stopwatch.Stop()
    $fileInfo = New-Object System.IO.FileInfo($tempFile)
    
    if ($fileInfo.Length -gt 0) {
        $sizeMB = $fileInfo.Length / 1MB
        $speed = $sizeMB / $stopwatch.Elapsed.TotalSeconds
        Write-Host ("耗时: {0:N2} 秒, 下载大小: {1:N2} MB, 平均速度: {2:N2} MB/s" -f $stopwatch.Elapsed.TotalSeconds, $sizeMB, $speed) -ForegroundColor Green
    } else {
        Write-Host "下载失败或无数据返回 (退出代码: $($process.ExitCode))" -ForegroundColor Red
    }
    
    if (Test-Path $tempFile) {
        Remove-Item $tempFile -Force
    }
    Write-Host "----------------------------------------"
}

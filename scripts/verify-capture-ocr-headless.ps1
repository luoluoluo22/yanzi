#requires -version 5.1
<#
Safe non-interactive OCR integration test.
Creates a synthetic image in memory, stores it as a temporary file, then asks the
currently running Yanzi host to recognize the exact file using ocr.recognize.
No desktop screenshot, global input, foreground window or clipboard changes.
Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-capture-ocr-headless.ps1
NOTE: This is NOT a physical mouse-drag or clipboard/Toast end-to-end test.
#>
[CmdletBinding()]
param(
    [string]$SettingsPath = (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json'),
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'
$path = Join-Path ([IO.Path]::GetTempPath()) ('yanzi-ocr-background-test-' + [guid]::NewGuid().ToString('N') + '.png')
$bitmap = $null
$graphics = $null
$fontEn = $null
$fontZh = $null
$elapsed = [Diagnostics.Stopwatch]::StartNew()
try {
    if (-not (Test-Path -LiteralPath $SettingsPath)) { throw 'Yanzi settings not found. Cannot locate current host API configuration.' }
    $settings = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.enableAgentApi -eq $false) { throw 'Local Yanzi Agent API is disabled.' }
    $port = [int]$settings.agentApiPort
    $token = [string]$settings.agentApiToken
    if ($port -lt 1 -or $port -gt 65535 -or [string]::IsNullOrWhiteSpace($token)) {
        throw 'Local Agent API port or credential not available in settings.'
    }
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap(1220, 320)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $fontEn = New-Object System.Drawing.Font('Arial', 66, [System.Drawing.FontStyle]::Bold)
    $fontZh = New-Object System.Drawing.Font('Microsoft YaHei', 49, [System.Drawing.FontStyle]::Regular)
    $graphics.DrawString('YANZI OCR 2026', $fontEn, [System.Drawing.Brushes]::Black, 32, 20)
    $graphics.DrawString('燕子后台识别测试', $fontZh, [System.Drawing.Brushes]::Black, 32, 156)
    $graphics.Flush()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    # Release drawing resources before invoking the model. No windows are opened.
    $graphics.Dispose(); $graphics = $null
    $fontEn.Dispose(); $fontEn = $null
    $fontZh.Dispose(); $fontZh = $null
    $bitmap.Dispose(); $bitmap = $null

    $uri = 'http://127.0.0.1:' + $port + '/v1/capabilities/invoke'
    $headers = @{ Authorization = 'Bearer ' + $token }
    $payload = @{ name = 'ocr.recognize'; payload = @{ imagePath = $path; includeLines = $false } } |
        ConvertTo-Json -Compress -Depth 5
    $reply = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -TimeoutSec $TimeoutSec
    if ($reply.success -ne $true) { throw 'PaddleOCR capability did not succeed.' }
    $recognized = [string]$reply.data.text
    $normalized = [regex]::Replace($recognized.ToUpperInvariant(), '\s+', '')
    $latinOk = $normalized.Contains('YANZI') -and $normalized.Contains('OCR') -and $normalized.Contains('2026')
    $chineseOk = $recognized.Contains('燕子')
    $elapsed.Stop()
    Write-Output 'BACKGROUND_TEST=true'
    Write-Output 'FOREGROUND_MOUSE_OR_KEYBOARD_INPUT=false'
    Write-Output 'DESKTOP_SCREEN_CAPTURE=false'
    Write-Output 'CLIPBOARD_ACCESS=false'
    Write-Output 'VISIBLE_WINDOW=false'
    Write-Output 'OCR_HOST_SUCCESS=true'
    Write-Output "OCR_ENGLISH_MATCH=$latinOk"
    Write-Output "OCR_CHINESE_MATCH=$chineseOk"
    Write-Output "OCR_CHAR_COUNT=$($recognized.Length)"
    Write-Output "ELAPSED_MS=$($elapsed.ElapsedMilliseconds)"
    if (-not $latinOk -or -not $chineseOk) {
        Write-Output ('OCR_TEXT=' + ($recognized -replace '[\r\n]+', ' / '))
        throw 'Background OCR recognition did not match the known reference image.'
    }
    Write-Output 'BACKGROUND_OCR_PASS=true'
}
finally {
    if ($graphics) { $graphics.Dispose() }
    if ($fontEn) { $fontEn.Dispose() }
    if ($fontZh) { $fontZh.Dispose() }
    if ($bitmap) { $bitmap.Dispose() }
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}

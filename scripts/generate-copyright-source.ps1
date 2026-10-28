<#
.SYNOPSIS
    一键自动提取燕子 (Yanzi) 项目源码，生成符合国家版权局软著申请规范的《源程序量化文档》（前30页+后30页共3000行）。
#>
param(
    [string]$SoftwareName = "燕子桌面效率启动器宿主软件",
    [string]$Version = "V0.3.13"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputDoc = Join-Path $root "软著申报材料_源程序文档.txt"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  正在扫描 C# 核心源代码用于生成软著源码文档..." -ForegroundColor Cyan
Write-Host "=============================================="

# 扫描 src 下所有核心 .cs 源码
$csFiles = Get-ChildItem -Path (Join-Path $root "src\OpenQuickHost") -Filter "*.cs" -Recurse |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } |
    Sort-Object FullName

$allLines = [System.Collections.Generic.List[string]]::new()

foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -Encoding UTF8
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        # 过滤纯空行
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }
        $allLines.Add($line)
    }
}

$totalLines = $allLines.Count
Write-Host "成功收集到有效源代码总行数: $totalLines 行" -ForegroundColor Green

$linesPerPage = 50
$targetTotalLines = 3000 # 60页 * 50行 = 3000行

$selectedLines = [System.Collections.Generic.List[string]]::new()

if ($totalLines -le $targetTotalLines) {
    Write-Host "源码总量 <= 3000 行，将包含全部源码。" -ForegroundColor Yellow
    $selectedLines.AddRange($allLines)
} else {
    Write-Host "源码总量 > 3000 行，自动截取【前30页(1500行)】与【后30页(1500行)】..." -ForegroundColor Yellow
    for ($i = 0; $i -lt 1500; $i++) {
        $selectedLines.Add($allLines[$i])
    }
    for ($i = $totalLines - 1500; $i -lt $totalLines; $i++) {
        $selectedLines.Add($allLines[$i])
    }
}

$outContent = [System.Text.StringBuilder]::new()
$outContent.AppendLine("==========================================================================") | Out-Null
$outContent.AppendLine("软件全称：$SoftwareName") | Out-Null
$outContent.AppendLine("软件版本：$Version") | Out-Null
$outContent.AppendLine("文档名称：计算机软件著作权登记申请 - 源程序技术文档") | Out-Null
$outContent.AppendLine("提取标准：符合国家版权局软著规范（每页50行，标准代码格式）") | Out-Null
$outContent.AppendLine("==========================================================================") | Out-Null
$outContent.AppendLine() | Out-Null

$pageNum = 1
$lineCounter = 0

foreach ($codeLine in $selectedLines) {
    if ($lineCounter -eq 0) {
        $outContent.AppendLine("--- 第 $pageNum 页 (软件全称: $SoftwareName $Version) ---") | Out-Null
    }
    $outContent.AppendLine($codeLine) | Out-Null
    $lineCounter++

    if ($lineCounter -ge $linesPerPage) {
        $lineCounter = 0
        $pageNum++
        $outContent.AppendLine() | Out-Null
    }
}

[System.IO.File]::WriteAllText($outputDoc, $outContent.ToString(), [System.Text.Encoding]::UTF8)

Write-Host "`n✅ 源程序文档生成完毕！" -ForegroundColor Green
Write-Host "📄 文件路径: $outputDoc" -ForegroundColor Cyan
Write-Host "📊 总页数: $pageNum 页，可以直接复制进 Word 或软著申请系统！" -ForegroundColor Green

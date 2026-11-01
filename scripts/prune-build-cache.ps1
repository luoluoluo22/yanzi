param(
    [int]$ThresholdMB = 2048,
    [switch]$Force,
    [switch]$Apply,
    [int]$KeepRecentHours = 48,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
if (-not $Apply) { Write-Output 'YANZI_CACHE_DRY_RUN: No deletion without explicit -Apply.'; exit 0 }
$root = (git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
if (-not $root) { $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }

# 1. 扫描可清理的缓存目标与计算体积
$cacheDirs = @()
$targets = @('.artifacts', '.tmp', 'artifacts', 'mobile/android/app/build', 'mobile/android/.gradle', '.vs')
foreach ($t in $targets) {
    $p = Join-Path $root $t
    if (Test-Path $p) { $cacheDirs += (Resolve-Path $p).Path }
}

# 查找所有 C# 项目下的 bin 和 obj (排除 node_modules 和 .git)
Get-ChildItem -Path $root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object {
        ($_.Name -eq "bin" -or $_.Name -eq "obj") -and
        $_.FullName -notmatch "node_modules" -and
        $_.FullName -notmatch "\\\.git\\"
    } | ForEach-Object { $cacheDirs += $_.FullName }

$cacheDirs = $cacheDirs | Select-Object -Unique

# 统计总占用
$totalBytes = 0
foreach ($dir in $cacheDirs) {
    if (Test-Path $dir) {
        $measure = Get-ChildItem -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
        if ($measure.Sum) { $totalBytes += $measure.Sum }
    }
}

$currentMB = [math]::Round($totalBytes / 1MB, 2)

if (-not $Force -and $currentMB -lt $ThresholdMB) {
    if (-not $Quiet) {
        Write-Output "YANZI_CACHE_CHECK: Current cache size is ${currentMB} MB (Threshold: ${ThresholdMB} MB). No pruning needed."
    }
    exit 0
}

Write-Output "YANZI_CACHE_PRUNE_START: Current cache size is ${currentMB} MB (Exceeds ${ThresholdMB} MB threshold or Force requested). Cleaning..."

$freedBytes = 0
$cleanedCount = 0

# 清理 bin 和 obj 目录与临时归档
foreach ($dir in $cacheDirs) {
    if (Test-Path $dir) {
        # 如果是 .artifacts 或 artifacts，只清理非当前活跃或超过 KeepRecentHours 的旧历史镜像/测试包
        if ($dir -match "\\\.artifacts$" -or $dir -match "\\artifacts$") {
            $cutoff = (Get-Date).AddHours(-$KeepRecentHours)
            Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if ($Force -or $_.LastWriteTime -lt $cutoff) {
                    $m = Get-ChildItem -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
                    if ($m.Sum) { $freedBytes += $m.Sum }
                    Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
                    $cleanedCount++
                }
            }
        } else {
            $m = Get-ChildItem -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
            if ($m.Sum) { $freedBytes += $m.Sum }
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
            $cleanedCount++
        }
    }
}

# 触发轻量级 Git 垃圾回收
try {
    git -C $root gc --auto --quiet 2>$null
} catch {}

$freedMB = [math]::Round($freedBytes / 1MB, 2)
Write-Output "YANZI_CACHE_PRUNE_COMPLETE: Cleaned $cleanedCount targets; freed ${freedMB} MB space."

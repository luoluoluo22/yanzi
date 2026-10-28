<#
.SYNOPSIS
    一键生成符合国家版权局软著申报规范的《源程序文档.pdf》和《用户操作手册.pdf》。
#>
param(
    [string]$SoftwareName = "燕子桌面快捷效率宿主软件",
    [string]$Version = "V0.3.13"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$edgeExe = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edgeExe)) {
    $edgeExe = "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
}

$tempDir = Join-Path $root ".artifacts\copyright_temp"
if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir -Force | Out-Null }

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  1. 正在生成符合软著规范的【源程序文档.pdf】..." -ForegroundColor Cyan
Write-Host "=============================================="

# 1. 读取源码并分页（60页，每页50行）
$csFiles = Get-ChildItem -Path (Join-Path $root "src\OpenQuickHost") -Filter "*.cs" -Recurse |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } |
    Sort-Object FullName

$allLines = [System.Collections.Generic.List[string]]::new()
foreach ($file in $csFiles) {
    $lines = Get-Content -Path $file.FullName -Encoding UTF8
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        $encoded = [System.Net.WebUtility]::HtmlEncode($line)
        $allLines.Add($encoded)
    }
}

$totalLines = $allLines.Count
$selectedLines = [System.Collections.Generic.List[string]]::new()
if ($totalLines -le 3000) {
    $selectedLines.AddRange($allLines)
} else {
    for ($i = 0; $i -lt 1500; $i++) { $selectedLines.Add($allLines[$i]) }
    for ($i = $totalLines - 1500; $i -lt $totalLines; $i++) { $selectedLines.Add($allLines[$i]) }
}

$linesPerPage = 50
$pagesCount = [Math]::Ceiling($selectedLines.Count / $linesPerPage)

$sourceHtml = @"
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  @page {
    size: A4;
    margin: 20mm 15mm 20mm 15mm;
  }
  body {
    font-family: "Consolas", "Courier New", "SimSun", monospace;
    font-size: 8.5pt;
    line-height: 1.32;
    color: #111;
    margin: 0;
    padding: 0;
  }
  .page {
    page-break-after: always;
    height: 257mm;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
  }
  .header {
    display: flex;
    justify-content: space-between;
    font-family: "SimSun", "Microsoft YaHei", sans-serif;
    font-size: 8.5pt;
    color: #555;
    border-bottom: 1px solid #aaa;
    padding-bottom: 3px;
    margin-bottom: 5px;
  }
  .content {
    flex-grow: 1;
    overflow: hidden;
    white-space: pre-wrap;
    word-break: break-all;
  }
  .footer {
    display: flex;
    justify-content: space-between;
    font-family: "SimSun", "Microsoft YaHei", sans-serif;
    font-size: 8.5pt;
    color: #555;
    border-top: 1px solid #aaa;
    padding-top: 3px;
    margin-top: 5px;
  }
</style>
</head>
<body>
"@

for ($p = 0; $p -lt $pagesCount; $p++) {
    $pageNum = $p + 1
    $pageLines = $selectedLines | Select-Object -Skip ($p * $linesPerPage) -First $linesPerPage
    $codeBlock = ($pageLines -join "`n")

    $sourceHtml += @"
  <div class="page">
    <div class="header">
      <span>软件全称：$SoftwareName</span>
      <span>版本号：$Version</span>
    </div>
    <div class="content">$codeBlock</div>
    <div class="footer">
      <span>源程序鉴别材料（前30页及后30页）</span>
      <span>第 $pageNum 页 / 共 $pagesCount 页</span>
    </div>
  </div>
"@
}

$sourceHtml += "</body></html>"
$sourceHtmlPath = Join-Path $tempDir "source_code.html"
[System.IO.File]::WriteAllText($sourceHtmlPath, $sourceHtml, [System.Text.Encoding]::UTF8)

$sourcePdfPath = Join-Path $root "软著申报材料_源程序文档.pdf"
$userDataDir = Join-Path $tempDir "edge_user_data_source"

& $edgeExe --headless --disable-gpu --user-data-dir="$userDataDir" --print-to-pdf="$sourcePdfPath" --no-pdf-header-footer "$sourceHtmlPath"
Write-Host "✅ 【源程序文档.pdf】生成完成: $sourcePdfPath" -ForegroundColor Green


Write-Host "`n==============================================" -ForegroundColor Cyan
Write-Host "  2. 正在生成符合软著规范的【用户操作手册.pdf】..." -ForegroundColor Cyan
Write-Host "=============================================="

# 辅助函数：将图片转为 Base64 内嵌
function Get-Base64Image($relPath) {
    $full = Join-Path $root $relPath
    if (Test-Path $full) {
        $bytes = [System.IO.File]::ReadAllBytes($full)
        $b64 = [Convert]::ToBase64String($bytes)
        return "data:image/png;base64,$b64"
    }
    return ""
}

$imgCover = Get-Base64Image "readme-cover-16x9.png"
$imgLauncher = Get-Base64Image "launcher-and-quick-panel.png"
$imgGrid = Get-Base64Image "quick-panel-grid.png"
$imgEditor = Get-Base64Image "json-extension-editor.png"
$imgQuery = Get-Base64Image "query-prefix-preview.png"

$manualHtml = @"
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  @page {
    size: A4;
    margin: 20mm 15mm 20mm 15mm;
  }
  body {
    font-family: "Microsoft YaHei", "SimSun", sans-serif;
    font-size: 10pt;
    line-height: 1.6;
    color: #222;
  }
  .page {
    page-break-after: always;
  }
  .cover {
    height: 250mm;
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
    text-align: center;
  }
  .cover h1 {
    font-size: 26pt;
    margin-bottom: 10px;
    color: #1e3a8a;
  }
  .cover h2 {
    font-size: 16pt;
    color: #4b5563;
    font-weight: normal;
    margin-bottom: 40px;
  }
  .cover-info {
    font-size: 11pt;
    line-height: 2;
    color: #374151;
    margin-top: 50px;
  }
  .header-bar {
    display: flex;
    justify-content: space-between;
    font-size: 8.5pt;
    color: #666;
    border-bottom: 1px solid #ccc;
    padding-bottom: 4px;
    margin-bottom: 15px;
  }
  h2.section-title {
    font-size: 14pt;
    color: #1e40af;
    border-left: 4px solid #2563eb;
    padding-left: 8px;
    margin-top: 20px;
    margin-bottom: 10px;
  }
  h3.sub-title {
    font-size: 11pt;
    color: #1f2937;
    margin-top: 14px;
    margin-bottom: 6px;
  }
  p {
    margin: 6px 0;
    text-indent: 2em;
  }
  .img-box {
    text-align: center;
    margin: 12px 0;
  }
  .img-box img {
    max-width: 90%;
    max-height: 85mm;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
    box-shadow: 0 2px 6px rgba(0,0,0,0.08);
  }
  .caption {
    font-size: 8.5pt;
    color: #6b7280;
    margin-top: 4px;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    margin: 10px 0;
    font-size: 9pt;
  }
  th, td {
    border: 1px solid #d1d5db;
    padding: 6px 10px;
    text-align: left;
  }
  th {
    background-color: #f3f4f6;
  }
</style>
</head>
<body>

  <!-- 封面 -->
  <div class="page cover">
    <h1>$SoftwareName</h1>
    <h2>用户操作与使用说明书</h2>
    <div style="width: 80px; height: 3px; background: #2563eb; margin: 20px auto;"></div>
    <div class="cover-info">
      <p style="text-indent:0;"><strong>软件版本：</strong>$Version</p>
      <p style="text-indent:0;"><strong>开发著作权人：</strong>罗名扬</p>
      <p style="text-indent:0;"><strong>文档类型：</strong>软件著作权登记鉴定材料</p>
      <p style="text-indent:0;"><strong>发布日期：</strong>2026年08月</p>
    </div>
  </div>

  <!-- 第一章：软件概述与运行环境 -->
  <div class="page">
    <div class="header-bar">
      <span>$SoftwareName $Version 用户操作说明书</span>
      <span>第一章 概述与运行环境</span>
    </div>
    <h2 class="section-title">第一章 软件概述与运行环境</h2>
    <h3 class="sub-title">1.1 软件总体概述</h3>
    <p>“$SoftwareName”是一款专为 Windows 操作系统深度定制的高性能桌面快捷效率启动、手势拓扑感知与多端云同步系统。软件基于现代 .NET 技术栈与原生 Win32 底层挂钩技术构建，具备毫秒级全局响应、模块化扩展以及轻量化内存驻留等显著优势。</p>
    <p>本系统旨在消除用户在日常桌面操作中频繁查找应用、切换多级目录与键鼠来回移动的高操作成本，通过创新的全键盘拼音检索、鼠标矢量手势、环形轮盘快捷菜单与沉浸式桌面小组件，大幅提升桌面工作流与个人数字生产力。</p>
    
    <h3 class="sub-title">1.2 运行环境要求</h3>
    <table>
      <tr><th>硬件类别</th><th>推荐配置要求</th></tr>
      <tr><td>处理器 (CPU)</td><td>Intel Core i3 / AMD Ryzen 3 及以上 x64 架构处理器</td></tr>
      <tr><td>内存 (RAM)</td><td>4GB 及以上物理内存</td></tr>
      <tr><td>硬盘存储</td><td>至少 500MB 可用固态/机械硬盘空间</td></tr>
      <tr><td>显示分辨率</td><td>1280×720 及以上分辨率（完美支持 4K/高 DPI 缩放）</td></tr>
      <tr><td>操作系统</td><td>Windows 10 64位 / Windows 11 64位</td></tr>
      <tr><td>运行依赖库</td><td>内置自包含运行时（或 .NET Desktop Runtime 9.0）</td></tr>
    </table>

    <div class="img-box">
      <img src="$imgCover" />
      <div class="caption">图 1-1 燕子桌面效率启动系统整体架构概览</div>
    </div>
  </div>

  <!-- 第二章：安装、启动与主界面操作 -->
  <div class="page">
    <div class="header-bar">
      <span>$SoftwareName $Version 用户操作说明书</span>
      <span>第二章 安装与主启动器</span>
    </div>
    <h2 class="section-title">第二章 软件安装与主启动器检索</h2>
    <h3 class="sub-title">2.1 安装与首次启动</h3>
    <p>运行官方发布的安装程序 <code>Yanzi-win-Setup.exe</code>，系统将自动完成静默快速解压与桌面快捷方式注册。启动后软件将常驻 Windows 系统托盘区，默认全局热键为 <code>Alt + Space</code>（可自定义）。</p>
    
    <h3 class="sub-title">2.2 全局启动器与命令检索操作</h3>
    <p>按下全局呼出热键后，屏幕正中央将平滑唤出搜索面板。用户只需键入中文全拼、简拼首字母或英文关键字，系统即刻通过多级流水线并行索引已安装软件、本地文件与自定义小程序。</p>
    <div class="img-box">
      <img src="$imgLauncher" />
      <div class="caption">图 2-1 全局热键唤醒启动器检索与操作面板</div>
    </div>
    <p>用户可通过键盘上下方向键或鼠标悬停预览匹配条目，回车键直接执行启动，或使用 <code>Ctrl + K</code> 呼出专属动作快捷菜单。</p>
  </div>

  <!-- 第三章：前台应用感知鼠标手势系统 -->
  <div class="page">
    <div class="header-bar">
      <span>$SoftwareName $Version 用户操作说明书</span>
      <span>第三章 鼠标手势与应用感知</span>
    </div>
    <h2 class="section-title">第三章 前台应用感知鼠标手势与冲突拦截</h2>
    <h3 class="sub-title">3.1 鼠标轨迹手势识别</h3>
    <p>软件内置自研的鼠标手势拓扑识别引擎，支持按住鼠标右键（或中键/Ctrl+左键）在屏幕任意位置绘制方向序列（如 ↑、↓→、Z字形 等）。系统将动态绘制平滑抗锯齿矢量轨迹，并在松开按键后毫秒级判定触发对应动作。</p>
    
    <h3 class="sub-title">3.2 前台窗口多应用感知与黑白名单拦截</h3>
    <p>系统深度集成了 Windows 活动窗口句柄嗅探技术：</p>
    <p><strong>（1）白名单应用限定：</strong>支持将特定手势绑定至指定应用（如仅在 Edge 浏览器中触发网页后退）。当目标应用处于前台时手势生效，其他应用中静默放行。</p>
    <p><strong>（2）黑名单应用禁用：</strong>用户可将全屏游戏、绘图或远程控制软件添加至黑名单。当前台处于黑名单程序时，手势系统完全放行原生右键拖拽，彻底杜绝按键冲突。</p>
    <div class="img-box">
      <img src="$imgGrid" />
      <div class="caption">图 3-1 常用手势快速绑定与多应用前台规则配置</div>
    </div>
  </div>

  <!-- 第四章：JSON 自定义小程序扩展与桌面组件 -->
  <div class="page">
    <div class="header-bar">
      <span>$SoftwareName $Version 用户操作说明书</span>
      <span>第四章 小程序扩展与桌面组件</span>
    </div>
    <h2 class="section-title">第四章 JSON 自定义小程序扩展与桌面组件</h2>
    <h3 class="sub-title">4.1 JSON 小程序扩展热加载</h3>
    <p>软件支持开放的标准 JSON 扩展协议。用户点击状态栏 <code>+</code> 按钮即可进入可视化扩展编辑器，配置指令 ID、名称、图标、打开路径或参数模板，保存后即可即时热生效。</p>
    <div class="img-box">
      <img src="$imgEditor" />
      <div class="caption">图 4-1 JSON 小程序可视化配置与管理编辑器</div>
    </div>
    <h3 class="sub-title">4.2 沉浸式桌面小组件与多端云同步</h3>
    <p>软件提供了半透明沉浸式桌面小组件（燕幕），支持便签快速记录、置顶悬浮与多屏漫游。所有手势配置、小程序和便签均通过端到端加密与 Cloudflare / WebDAV 保持多设备实时同步。</p>
  </div>

</body>
</html>
"@

$manualHtmlPath = Join-Path $tempDir "user_manual.html"
[System.IO.File]::WriteAllText($manualHtmlPath, $manualHtml, [System.Text.Encoding]::UTF8)

$manualPdfPath = Join-Path $root "软著申报材料_用户操作手册.pdf"
$userDataDirManual = Join-Path $tempDir "edge_user_data_manual"

& $edgeExe --headless --disable-gpu --user-data-dir="$userDataDirManual" --print-to-pdf="$manualPdfPath" --no-pdf-header-footer "$manualHtmlPath"
Write-Host "✅ 【用户操作手册.pdf】生成完成: $manualPdfPath" -ForegroundColor Green

param(
    [switch]$SkipBuild,
    [switch]$Launch,
    [switch]$NoShortcut,
    [switch]$NoExtension
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Yanzi.UI.Gallery\Yanzi.UI.Gallery.csproj'
$source = Join-Path $repo 'src\Yanzi.UI.Gallery\bin\Release\net9.0-windows'
$installRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Tools\YanziUiGallery'
$extensionRoot = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\yanzi-ui-gallery'

if (-not $SkipBuild) {
    & dotnet build $project -c Release -p:SkipStopRunningApp=true --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Gallery build failed ($LASTEXITCODE)." }
}

foreach ($needed in @('Yanzi.UI.Gallery.exe', 'Yanzi.UI.Gallery.dll', 'Yanzi.UI.Gallery.runtimeconfig.json', 'Yanzi.UI.Gallery.deps.json', 'Yanzi.UI.Wpf.dll')) {
    if (-not (Test-Path (Join-Path $source $needed))) {
        throw "Missing build asset: $needed"
    }
}

# Immutable release folder avoids locked-file replacement when user is evaluating the UI.
$releaseName = 'preview-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
$release = Join-Path $installRoot $releaseName
New-Item -ItemType Directory -Force -Path $release | Out-Null
Copy-Item (Join-Path $source '*') $release -Force -Recurse
# Nested showcase images must survive installation, not just local bin output.
foreach ($photo in @('office-1.jpg', 'office-2.jpg', 'office-3.jpg', 'person-1.jpg', 'person-2.jpg', 'person-3.jpg')) {
    if (-not (Test-Path (Join-Path $release ('Assets\' + $photo)))) {
        throw "Gallery image resource missing after install: $photo"
    }
}
$exe = Join-Path $release 'Yanzi.UI.Gallery.exe'

if (-not (Test-Path $exe)) { throw "Gallery copy failed." }

if (-not $NoShortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shortcutPath = Join-Path $desktop '燕子 UI 组件评估中心.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $release
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = '打开燕子公共 UI 组件评估中心'
    $shortcut.Save()
    Write-Host "Desktop shortcut: $shortcutPath"
}

if (-not $NoExtension) {
    New-Item -ItemType Directory -Force -Path $extensionRoot | Out-Null
    $manifestPath = Join-Path $extensionRoot 'manifest.json'
    if (Test-Path $manifestPath) {
        $existing = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($existing.id -ne 'yanzi-ui-gallery') {
            throw "Manifest id mismatch: $manifestPath"
        }
    }

    $manifest = [ordered]@{
        id = 'yanzi-ui-gallery'
        name = '组件评估'
        version = '0.7.9'
        category = '开发工具'
        description = '查看并体验燕子公共 UI 组件：按钮、输入、选择、列表、弹窗、颜色主题，并记录评估建议。'
        keywords = @('UI', '组件库', '设计系统', '组件评估', '视觉预览', 'ui-gallery')
        icon = 'mdi:palette-outline'
        accentHex = '#3B82F6'
        openTarget = $exe
        runtime = $null
        uiMode = $null
        entryMode = $null
        entry = $null
        permissions = @()
        isPublished = $false
    }
    $json = $manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Yanzi extension: $manifestPath"
}

Write-Host "Installed executable: $exe"
if ($Launch) {
    $proc = Start-Process -FilePath $exe -WorkingDirectory $release -PassThru
    Write-Host "Gallery running PID: $($proc.Id)"
}

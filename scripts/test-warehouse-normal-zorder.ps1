#Requires -Version 5.1
# Offline contract test for the warehouse's normal Windows Z-order.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ext = [IO.File]::ReadAllText((Join-Path $root 'src\OpenQuickHost\MainWindow.Extensions.cs'))
$chat = [IO.File]::ReadAllText((Join-Path $root 'src\OpenQuickHost\MainWindow.AiChat.cs'))

function Assert-WindowContract([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL: $name" }
    Write-Output "PASS: $name"
}

$show = [regex]::Match($ext, '(?s)public void ShowPanel\(\).*?(?=\s+public void ShowMousePanel\()').Value
$front = [regex]::Match($ext, '(?s)public void BringMainWindowToFront\(\).*?(?=\s+private void StartStartupExtensions\()').Value
$picker = [regex]::Match($chat, '(?s)private Task<string\[\]\?> ShowAttachmentPickerAsync\(\).*?(?=\s+// ====================)').Value

Assert-WindowContract ($show.Length -gt 0) 'warehouse show routine found'
Assert-WindowContract ($show.Contains('Topmost = false;') -and -not $show.Contains('Topmost = true;')) 'warehouse show never promotes topmost'
Assert-WindowContract ($front.Contains('HWND_NOTOPMOST') -and -not $front.Contains('HWND_TOPMOST')) 'foreground promotion stays in normal Z-order'
Assert-WindowContract ($front.Contains('SetForegroundWindow(handle)')) 'hotkey activation still brings warehouse forward'
Assert-WindowContract ($show.Contains('if (!IsVisible || !IsActive) return;')) 'queued activation cannot steal focus back from modal picker'
Assert-WindowContract ($picker.Contains('dialog.ShowDialog(this)')) 'native file picker has the warehouse as owner'
Assert-WindowContract (-not $picker.Contains('new System.Threading.Thread')) 'native file picker is on owner UI dispatcher'
Assert-WindowContract ($ext.Contains('if (_isPinned || IsAiChatMode || !IsVisible || IsRadialPickerMode)')) 'AI chat remains visible on focus transfer'
Write-Output 'WAREHOUSE_NORMAL_ZORDER_CONTRACT_PASSED'

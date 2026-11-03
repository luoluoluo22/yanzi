#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '..\src\OpenQuickHost\MainWindow.Extensions.cs'
$source = Get-Content -LiteralPath $path -Raw -Encoding UTF8

function Assert-Contains([string]$text, [string]$needle, [string]$test) {
    if (-not $text.Contains($needle)) { throw "FAIL: $test" }
    Write-Output "PASS: $test"
}

$front = [regex]::Match($source, '(?s)public void BringMainWindowToFront\(\)(.*?)private void StartStartupExtensions').Groups[1].Value
$show = [regex]::Match($source, '(?s)public void ShowPanel\(\)(.*?)public void ShowMousePanel').Groups[1].Value
$toggle = [regex]::Match($source, '(?s)public void TogglePanelVisibility\(\)(.*?)private bool _isHookAdded').Groups[1].Value
if (-not $front -or -not $show -or -not $toggle) { throw 'Warehouse entrypoints missing' }

Assert-Contains $front 'Topmost = false;' 'warehouse is not permanently topmost'
Assert-Contains $front 'Win32Native.HWND_NOTOPMOST' 'leave topmost z-order band'
Assert-Contains $front 'Win32Native.HWND_TOP' 'raise in normal z-order band'
Assert-Contains $front 'WindowSensorHelper.ForceSetForegroundWindow(handle)' 'fallback for Windows foreground lock'
Assert-Contains $front 'Win32Native.GetForegroundWindow() == handle' 'verify actual foreground before focusing input'
if ($front.Contains('Win32Native.HWND_TOPMOST')) { throw 'FAIL: legacy HWND_TOPMOST must not return' }
Assert-Contains $show 'Topmost = false;' 'show path stays non-topmost'
Assert-Contains $toggle 'if (!IsVisible)' 'hidden window is shown'
Assert-Contains $toggle 'else if (IsActive && Win32Native.GetForegroundWindow()' 'only truly foreground warehouse hides'
Assert-Contains $source 'NormalizeSearchScopePopupZOrder();' 'popup z-order normalization is invoked'
Assert-Contains $source 'Win32Native.SetWindowPos(popupHandle, Win32Native.HWND_NOTOPMOST' 'separate tab popup is non-topmost'
Write-Output 'WAREHOUSE_FOREGROUND_CONTRACT_PASSED'

#requires -version 5.1
<#
Real desktop OCR acceptance test, not a mock.
Usage:
powershell -STA -NoProfile -ExecutionPolicy Bypass -File scripts/verify-capture-ocr-e2e.ps1 `
  -X1 735 -Y1 365 -X2 1310 -Y2 550 -ExpectedText '能力实验室' -AllowForegroundControl
Requires a visible text sample within the selected rectangle and a live Yanzi host.
The script sends physical hotkeys and a real mouse-down/move/up sequence,
then checks NEW capture diagnostics and the actual clipboard.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$X1,
    [Parameter(Mandatory=$true)][int]$Y1,
    [Parameter(Mandatory=$true)][int]$X2,
    [Parameter(Mandatory=$true)][int]$Y2,
    [Parameter(Mandatory=$true)][string]$ExpectedText,
    [ValidateSet('F3','Ctrl+Alt+F3')][string]$Hotkey = 'F3',
    [switch]$AllowForegroundControl
)
$ErrorActionPreference='Stop'
# This test sends global input, alters the system clipboard and changes the foreground UI.
# Never run without explicit opt-in. Use verify-capture-ocr-headless.ps1 for safe background testing.
if (-not $AllowForegroundControl) {
    throw 'INTERACTIVE_TEST_BLOCKED: This script controls the real desktop and clipboard. Use verify-capture-ocr-headless.ps1 instead, or explicitly pass -AllowForegroundControl when the desktop is available for testing.'
}
if ($X2 -le $X1+8 -or $Y2 -le $Y1+8) { throw 'Invalid mouse selection rectangle' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YanziCaptureTestInput
{
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
function Press([byte]$vk) {
    [YanziCaptureTestInput]::keybd_event($vk,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 85
    [YanziCaptureTestInput]::keybd_event($vk,0,2,[UIntPtr]::Zero)
}
$log=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\logs\yanzi-capture-perf.log'
if (-not (Test-Path $log)) {throw 'Capture diagnostics log not found'}
$baseline=(Get-Item $log).Length
# Always clear an existing capture workspace; the normal F1 workspace otherwise
# intercepts OCR requests and the test would only validate ordinary screenshot selection.
Press 0x1b
Start-Sleep -Milliseconds 260
if ($Hotkey -eq 'F3') {Press 0x72}
else {
    [YanziCaptureTestInput]::keybd_event(0x11,0,0,[UIntPtr]::Zero)
    [YanziCaptureTestInput]::keybd_event(0x12,0,0,[UIntPtr]::Zero)
    Press 0x72
    [YanziCaptureTestInput]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
    [YanziCaptureTestInput]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
}
Start-Sleep -Milliseconds 450
[void][YanziCaptureTestInput]::SetCursorPos($X1,$Y1)
Start-Sleep -Milliseconds 100
[YanziCaptureTestInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
try {
    for($i=1;$i -le 28;$i++) {
        [void][YanziCaptureTestInput]::SetCursorPos(
            [int]($X1+($X2-$X1)*$i/28),
            [int]($Y1+($Y2-$Y1)*$i/28))
        Start-Sleep -Milliseconds 18
    }
}
finally {
    [YanziCaptureTestInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
Write-Output "PHYSICAL_DRAG_OK rect=$X1,$Y1,$X2,$Y2"
$events = ''
$text = ''
$deadline=(Get-Date).AddSeconds(12)
while((Get-Date) -lt $deadline) {
    $fs=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        [void]$fs.Seek([Math]::Min($baseline,$fs.Length),[IO.SeekOrigin]::Begin)
        $reader=New-Object IO.StreamReader($fs,[Text.Encoding]::UTF8,$false)
        $events=$reader.ReadToEnd()
    } finally {$fs.Dispose()}
    try {$text=[Windows.Forms.Clipboard]::GetText()}catch{}
    if ($events -match 'event=ocr\.provider\.success' -and
        $events -match 'event=ocr\.clipboard\.(copied|failed)' -and
        $text.Contains($ExpectedText)) {break}
    Start-Sleep -Milliseconds 250
}
$crop=$events -match 'event=capture\.crop\.materialized'
$recognized=$events -match 'event=ocr\.provider\.success'
$copied=$events -match 'event=ocr\.clipboard\.copied.*backend=win32'
$failed=$events -match 'event=ocr\.clipboard\.failed'
$matched=$text.Contains($ExpectedText)
Write-Output "CROP_MATERIALIZED=$crop"
Write-Output "PADDLE_OCR_FINISHED=$recognized"
Write-Output "NATIVE_CLIPBOARD_SUCCESS=$copied"
Write-Output "FAILED_TOAST_CAUSE_LOGGED=$failed"
Write-Output "CLIPBOARD_EXPECTED_TEXT_FOUND=$matched"
Write-Output "OCR_CLIPBOARD_CHAR_COUNT=$($text.Length)"
if (-not ($crop -and $recognized -and $copied -and -not $failed -and $matched)) {
    throw 'OCR end-to-end test FAILED: drag alone is never sufficient for passing.'
}
Write-Output 'E2E_PASS=true'

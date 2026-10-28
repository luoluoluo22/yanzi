#requires -version 5.1
# Real-session GUI acceptance: F1 -> region drag -> Ctrl+C -> image clipboard -> Ctrl+V into RichTextBox.
# Changes foreground focus and clipboard; run only with explicit user permission.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'STA_REQUIRED' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YanziImagePasteInput {
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
}
'@
function ReadCaptureLog { $fs=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite); try { $reader=New-Object IO.StreamReader($fs,[Text.Encoding]::UTF8,$false); return $reader.ReadToEnd() } finally {$fs.Dispose()} }
function Pump([int]$milliseconds) { $end=(Get-Date).AddMilliseconds($milliseconds); while((Get-Date) -lt $end) {[Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 35} }
function Key([byte]$vk) {[YanziImagePasteInput]::keybd_event($vk,0,0,[UIntPtr]::Zero); Pump 80; [YanziImagePasteInput]::keybd_event($vk,0,2,[UIntPtr]::Zero)}
function CtrlKey([byte]$vk) {[YanziImagePasteInput]::keybd_event(0x11,0,0,[UIntPtr]::Zero); Pump 90; Key $vk; Pump 60; [YanziImagePasteInput]::keybd_event(0x11,0,2,[UIntPtr]::Zero)}
function Drag([int]$x1,[int]$y1,[int]$x2,[int]$y2) {
  [void][YanziImagePasteInput]::SetCursorPos($x1,$y1); Pump 180;
  [YanziImagePasteInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);
  for($i=1;$i -le 20;$i++){[void][YanziImagePasteInput]::SetCursorPos([int]($x1+($x2-$x1)*$i/20),[int]($y1+($y2-$y1)*$i/20)); Pump 26}
  [YanziImagePasteInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
$log=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Logs\yanzi-capture-perf.log'
if(!(Test-Path $log)){throw 'CAPTURE_LOG_NOT_FOUND'}
$baseline=(Get-Item $log).Length
$screen=[Windows.Forms.Screen]::PrimaryScreen.Bounds
if($screen.Width -lt 800 -or $screen.Height -lt 650) {throw "SCREEN_TOO_SMALL:$($screen.Width)x$($screen.Height)"}
$testForm=$null; $pasteForm=$null
try {
 $testForm=New-Object Windows.Forms.Form
 $testForm.Text='YANZI_CLIPBOARD_E2E_SOURCE'
 $testForm.StartPosition='Manual'
 $testForm.Location=New-Object Drawing.Point(180,155)
 $testForm.Size=New-Object Drawing.Size(780,525)
 $testForm.BackColor=[Drawing.Color]::DeepSkyBlue
 $testForm.TopMost=$true
 $caption=New-Object Windows.Forms.Label
 $caption.Text='YANZI IMAGE COPY TEST - CTRL+C -> CTRL+V'
 $caption.ForeColor=[Drawing.Color]::Black
 $caption.BackColor=[Drawing.Color]::Yellow
 $caption.Font=New-Object Drawing.Font('Segoe UI',19,[Drawing.FontStyle]::Bold)
 $caption.Location=New-Object Drawing.Point(40,60)
 $caption.Size=New-Object Drawing.Size(685,160)
 $testForm.Controls.Add($caption)
 $testForm.Show(); $testForm.Activate(); Pump 600
 $testForm.TopMost=$false
 Write-Output 'SOURCE_WINDOW_SHOWN=True'
 Key 0x1b # Escape a stale capture window if one is open
 Pump 230
 Key 0x70 # F1
 Pump 1600
 Write-Output 'CAPTURE_F1_SENT=True'
 Drag 250 265 780 475
 Pump 650
 # Check that the active desktop belongs to screenshot UI before keyboard copy.
 $win=[YanziImagePasteInput]::GetForegroundWindow(); [uint32]$fgpid=0;
 [void][YanziImagePasteInput]::GetWindowThreadProcessId($win,[ref]$fgpid)
 Write-Output "BEFORE_CTRL_C_FOREGROUND_PID=$fgpid"
 $hostPid=(Get-Process -Name Yanzi -ErrorAction Stop | Sort-Object StartTime -Descending | Select-Object -First 1).Id
 if ($fgpid -ne $hostPid){ throw "CAPTURE_NOT_FOREGROUND: $fgpid != $hostPid" }
 CtrlKey 0x43 # C
 Pump 1150
 $newLog=ReadCaptureLog
 $added=$newLog.Substring([Math]::Min($baseline,$newLog.Length))
 $events=($added -split "\r?\n") | Where-Object {$_ -match 'event=(clipboard.image|workspace.closed|capture.flow)' }
 $events | Select-Object -Last 7 | ForEach-Object { Write-Output "EVENT=$_"}
 if ($added -notmatch 'event=clipboard.image.shortcut.deferred') {throw 'NEW_DLL_SHORTCUT_NOT_EXECUTED'}
 if ($added -notmatch 'event=clipboard.image.copied') {throw 'CTRL_C_NOT_COPIED'}
 if ($added -match 'event=clipboard.image.failed') {throw 'CTRL_C_COPIED_BUT_LOGGED_FAILURE'}
 $available=[Windows.Forms.Clipboard]::ContainsImage()
 $img=[Windows.Forms.Clipboard]::GetImage()
 if(!$available -or !$img -or $img.Width -le 20 -or $img.Height -le 20) {throw "IMAGE_CLIPBOARD_READ_FAILED: available=$available"}
 Write-Output "CTRL_C_CLIPBOARD_IMAGE=True SIZE=$($img.Width)x$($img.Height)"
 $seqBeforePaste=[YanziImagePasteInput]::GetClipboardSequenceNumber()
 $pasteForm=New-Object Windows.Forms.Form
 $pasteForm.Text='YANZI_CLIPBOARD_E2E_PASTE_TARGET'
 $pasteForm.StartPosition='Manual'
 $pasteForm.Location=New-Object Drawing.Point(1000,180)
 $pasteForm.Size=New-Object Drawing.Size(680,540)
 $pasteForm.TopMost=$true
 $r=New-Object Windows.Forms.RichTextBox
 $r.Dock='Fill'
 $pasteForm.Controls.Add($r)
 $pasteForm.Show();$pasteForm.Activate();$r.Focus();Pump 500
 $pasteForm.TopMost=$false
 CtrlKey 0x56 # V
 Pump 1000
 $got=$r.Rtf -match '\\pict'
 $seqAfterPaste=[YanziImagePasteInput]::GetClipboardSequenceNumber()
 Write-Output "REAL_CTRL_V_RTF_IMAGE=$got"
 Write-Output "CLIPBOARD_SEQUENCE_BEFORE_AFTER_PASTE=$seqBeforePaste,$seqAfterPaste"
 if(!$got) { throw 'REAL_CTRL_V_DID_NOT_INSERT_IMAGE' }
 Write-Output 'E2E_PASS=True'
}
finally {
 if ($pasteForm){$pasteForm.Close();$pasteForm.Dispose()}
 if ($testForm){$testForm.Close();$testForm.Dispose()}
}

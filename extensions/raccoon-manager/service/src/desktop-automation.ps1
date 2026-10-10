param(
  [Parameter(Mandatory=$true)][string]$StateFile
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

public static class RaccoonDesktopInput {
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    const uint MOUSEEVENTF_LEFTDOWN=0x0002, MOUSEEVENTF_LEFTUP=0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN=0x0008, MOUSEEVENTF_RIGHTUP=0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN=0x0020, MOUSEEVENTF_MIDDLEUP=0x0040;
    const uint MOUSEEVENTF_WHEEL=0x0800;
    const uint KEYEVENTF_KEYUP=0x0002, KEYEVENTF_UNICODE=0x0004;
    const int INPUT_KEYBOARD=1;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public int type;
        public InputUnion U;
    }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    static readonly Dictionary<string, byte> Keys = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase) {
        {"BACKSPACE",0x08},{"TAB",0x09},{"ENTER",0x0D},{"RETURN",0x0D},{"SHIFT",0x10},{"CTRL",0x11},{"CONTROL",0x11},
        {"ALT",0x12},{"ESC",0x1B},{"ESCAPE",0x1B},{"SPACE",0x20},{"PAGEUP",0x21},{"PAGEDOWN",0x22},{"END",0x23},
        {"HOME",0x24},{"LEFT",0x25},{"UP",0x26},{"RIGHT",0x27},{"DOWN",0x28},{"INSERT",0x2D},{"DELETE",0x2E},
        {"WIN",0x5B},{"LWIN",0x5B},{"RWIN",0x5C}
    };

    static byte KeyCode(string name) {
        byte code; if (Keys.TryGetValue(name, out code)) return code;
        if (name.Length == 1) {
            char c = Char.ToUpperInvariant(name[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return (byte)c;
        }
        int f; if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(name.Substring(1), out f) && f >= 1 && f <= 24) {
            return (byte)(0x70 + f - 1);
        }
        throw new ArgumentException("Unsupported key: " + name);
    }

    public static void Move(int x, int y, int durationMs) {
        if (durationMs <= 0) { SetCursorPos(x,y); return; }
        POINT start; GetCursorPos(out start);
        int steps = Math.Max(1, Math.Min(120, durationMs / 12));
        for (int i=1; i<=steps; i++) {
            double t = (double)i / steps;
            SetCursorPos((int)Math.Round(start.X + (x-start.X)*t), (int)Math.Round(start.Y + (y-start.Y)*t));
            Thread.Sleep(Math.Max(1, durationMs / steps));
        }
    }

    public static void Click(string button, int count, int intervalMs) {
        uint down, up;
        if (button.Equals("right", StringComparison.OrdinalIgnoreCase)) { down=MOUSEEVENTF_RIGHTDOWN; up=MOUSEEVENTF_RIGHTUP; }
        else if (button.Equals("middle", StringComparison.OrdinalIgnoreCase)) { down=MOUSEEVENTF_MIDDLEDOWN; up=MOUSEEVENTF_MIDDLEUP; }
        else { down=MOUSEEVENTF_LEFTDOWN; up=MOUSEEVENTF_LEFTUP; }
        for (int i=0;i<count;i++) {
            mouse_event(down,0,0,0,UIntPtr.Zero); mouse_event(up,0,0,0,UIntPtr.Zero);
            if (i+1<count && intervalMs>0) Thread.Sleep(intervalMs);
        }
    }

    public static void Scroll(int delta) { mouse_event(MOUSEEVENTF_WHEEL,0,0,delta,UIntPtr.Zero); }

    public static void KeyDown(string key) { keybd_event(KeyCode(key),0,0,UIntPtr.Zero); }
    public static void KeyUp(string key) { keybd_event(KeyCode(key),0,KEYEVENTF_KEYUP,UIntPtr.Zero); }
    public static void KeyTap(string key) { KeyDown(key); Thread.Sleep(20); KeyUp(key); }

    public static void Hotkey(string[] keys) {
        foreach (var key in keys) KeyDown(key);
        Thread.Sleep(25);
        for (int i=keys.Length-1;i>=0;i--) KeyUp(keys[i]);
    }

    public static void TypeText(string text, int intervalMs) {
        foreach (char ch in text) {
            var down = new INPUT { type=INPUT_KEYBOARD, U=new InputUnion { ki=new KEYBDINPUT { wVk=0, wScan=ch, dwFlags=KEYEVENTF_UNICODE } } };
            var up = new INPUT { type=INPUT_KEYBOARD, U=new InputUnion { ki=new KEYBDINPUT { wVk=0, wScan=ch, dwFlags=KEYEVENTF_UNICODE|KEYEVENTF_KEYUP } } };
            var arr = new INPUT[] { down, up };
            if (SendInput(2, arr, Marshal.SizeOf(typeof(INPUT))) != 2) throw new InvalidOperationException("SendInput failed.");
            if (intervalMs > 0) Thread.Sleep(intervalMs);
        }
    }

    public static string ActivateWindow(string titleContains) {
        IntPtr found = IntPtr.Zero;
        string foundTitle = null;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            int len = GetWindowTextLength(h); if (len <= 0) return true;
            var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString();
            if (title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0) {
                found = h; foundTitle = title; return false;
            }
            return true;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) throw new InvalidOperationException("Window not found: " + titleContains);
        ShowWindow(found, 9);
        if (!SetForegroundWindow(found)) throw new InvalidOperationException("Unable to activate window: " + foundTitle);
        return foundTitle;
    }

    public static string CursorJson() {
        POINT p; GetCursorPos(out p);
        return "{\"x\":" + p.X + ",\"y\":" + p.Y + "}";
    }
}
'@

function Read-State {
  if (-not (Test-Path -LiteralPath $StateFile)) { throw 'Foreground control state is missing.' }
  Get-Content -LiteralPath $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Wait-For-Control([string]$LeaseId) {
  while ($true) {
    $state = Read-State
    if (-not $state.active -or [string]$state.leaseId -ne $LeaseId) {
      throw 'Foreground control was released or replaced.'
    }
    if (-not $state.paused) { return }
    Start-Sleep -Milliseconds 100
  }
}

$payloadText = [Console]::In.ReadToEnd()
if (-not $payloadText) { throw 'Desktop action payload is empty.' }
$payload = $payloadText | ConvertFrom-Json
$leaseId = [string]$payload.leaseId
$completed = 0
$activatedWindow = $null

foreach ($action in @($payload.actions)) {
  Wait-For-Control $leaseId
  switch ([string]$action.type) {
    'move' {
      [RaccoonDesktopInput]::Move([int]$action.x, [int]$action.y, [int]$action.durationMs)
    }
    'click' {
      if ($null -ne $action.x -and $null -ne $action.y) {
        [RaccoonDesktopInput]::Move([int]$action.x, [int]$action.y, 0)
      }
      [RaccoonDesktopInput]::Click([string]$action.button, [int]$action.count, [int]$action.intervalMs)
    }
    'scroll' {
      if ($null -ne $action.x -and $null -ne $action.y) {
        [RaccoonDesktopInput]::Move([int]$action.x, [int]$action.y, 0)
      }
      [RaccoonDesktopInput]::Scroll([int]$action.delta)
    }
    'type' {
      [RaccoonDesktopInput]::TypeText([string]$action.text, [int]$action.intervalMs)
    }
    'key' {
      for ($i=0; $i -lt [int]$action.count; $i++) {
        Wait-For-Control $leaseId
        [RaccoonDesktopInput]::KeyTap([string]$action.key)
        if ($i + 1 -lt [int]$action.count -and [int]$action.intervalMs -gt 0) {
          Start-Sleep -Milliseconds ([int]$action.intervalMs)
        }
      }
    }
    'hotkey' {
      [RaccoonDesktopInput]::Hotkey([string[]]$action.keys)
    }
    'activateWindow' {
      $activatedWindow = [RaccoonDesktopInput]::ActivateWindow([string]$action.titleContains)
    }
    'wait' {
      $remaining = [int]$action.ms
      while ($remaining -gt 0) {
        Wait-For-Control $leaseId
        $slice = [Math]::Min(100, $remaining)
        Start-Sleep -Milliseconds $slice
        $remaining -= $slice
      }
    }
    default { throw "Unsupported desktop action type: $($action.type)" }
  }
  $completed++
}

Wait-For-Control $leaseId
$cursor = [RaccoonDesktopInput]::CursorJson() | ConvertFrom-Json
[pscustomobject]@{
  ok = $true
  actionsCompleted = $completed
  activatedWindow = $activatedWindow
  cursor = $cursor
} | ConvertTo-Json -Compress -Depth 4

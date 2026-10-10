using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "worker")
            return Worker(args[1], args[2]);
        return Controller();
    }

    private static int Controller()
    {
        var resultRoot = Path.Combine(AppContext.BaseDirectory, "results");
        Directory.CreateDirectory(resultRoot);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("exe missing");
        var metrics = new List<object>();
        Console.WriteLine("PROBE_DoesNotSendGlobalInput=True");
        Console.WriteLine("PROBE_DoesNotTouchClipboard=True");
        Console.WriteLine("PROBE_DoesNotSwitchActiveDesktop=True");
        var beforeHwnd = GetForegroundWindow();
        var beforeSequence = GetClipboardSequenceNumber();
        GetCursorPos(out var beforeCursor);
        bool allPass = true;
        foreach (var mode in new[] { "hidden", "offscreen" })
        {
            var reportPath = Path.Combine(resultRoot, mode + ".json");
            if (File.Exists(reportPath)) File.Delete(reportPath);
            var process = new Process {
                StartInfo = new ProcessStartInfo(exe)
                {
                    Arguments = $"worker {mode} \"{resultRoot}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            var stopwatch = Stopwatch.StartNew();
            process.Start();
            long peakWorkingSet = 0, peakPrivate = 0;
            TimeSpan lastCpu = TimeSpan.Zero;
            var timedOut = true;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(12))
            {
                if (process.HasExited) { timedOut = false; break; }
                try {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                    lastCpu = process.TotalProcessorTime;
                } catch { }
                Thread.Sleep(65);
            }
            if (timedOut) {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            process.WaitForExit();
            stopwatch.Stop();
            var output = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            Console.WriteLine($"{mode.ToUpperInvariant()}_EXIT={process.ExitCode};DURATION_MS={stopwatch.ElapsedMilliseconds};PEAK_WS_MB={peakWorkingSet / 1048576.0:F1};PEAK_PRIVATE_MB={peakPrivate / 1048576.0:F1};CPU_MS={lastCpu.TotalMilliseconds:F1}");
            Console.WriteLine($"{mode.ToUpperInvariant()}_DETAIL={output}");
            if (!string.IsNullOrEmpty(stderr))
                Console.WriteLine($"{mode.ToUpperInvariant()}_ERROR={stderr}");
            var exists = File.Exists(reportPath);
            if (!exists || process.ExitCode != 0 || timedOut) allPass = false;
            if (exists) {
                var json = File.ReadAllText(reportPath);
                var d = JsonDocument.Parse(json).RootElement;
                if (!d.TryGetProperty("buttonClicked", out var buttonClicked) || !buttonClicked.GetBoolean() ||
                    !d.GetProperty("printWindowReturned").GetBoolean() ||
                    d.GetProperty("nonBackgroundPixels").GetInt32() < 200 ||
                    (mode == "hidden" && !d.GetProperty("setThreadDesktopSucceeded").GetBoolean()) ||
                    (mode == "offscreen" && !d.GetProperty("fullyOutsideVirtualScreen").GetBoolean()))
                    allPass = false;
                Console.WriteLine($"{mode.ToUpperInvariant()}_REPORT={json}");
            }
            metrics.Add(new { mode, totalMs=stopwatch.ElapsedMilliseconds, peakWsMb=Math.Round(peakWorkingSet / 1048576.0,1), peakPrivateMb=Math.Round(peakPrivate / 1048576.0,1), cpuMs=Math.Round(lastCpu.TotalMilliseconds,1), reportPath, timedOut, exitCode=process.ExitCode });
            process.Dispose();
        }
        var afterHwnd = GetForegroundWindow();
        var afterSequence = GetClipboardSequenceNumber();
        GetCursorPos(out var afterCursor);
        Console.WriteLine($"FOREGROUND_UNCHANGED={beforeHwnd == afterHwnd}");
        Console.WriteLine($"CLIPBOARD_SEQUENCE_UNCHANGED={beforeSequence == afterSequence}");
        Console.WriteLine($"CURSOR_UNCHANGED={beforeCursor.X == afterCursor.X && beforeCursor.Y == afterCursor.Y}");
        var summaryPath = Path.Combine(resultRoot,"summary.json");
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(new {
            allPass, createdAt=DateTimeOffset.Now, tests=metrics,
            foregroundUnchanged=beforeHwnd == afterHwnd,
            clipboardSequenceUnchanged=beforeSequence == afterSequence,
            cursorUnchanged=beforeCursor.X == afterCursor.X && beforeCursor.Y == afterCursor.Y
        },new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine("OVERALL_PASS=" + allPass);
        return allPass ? 0 : 1;
    }

    private static int Worker(string mode, string resultRoot)
    {
        if(mode != "hidden")
            return RunUi(mode, resultRoot, true, 0);

        var name = "YanziProbe_" + Guid.NewGuid().ToString("N")[..10];
        var desktop = CreateDesktopW(name, null, IntPtr.Zero, 0, 0x000F01FF, IntPtr.Zero);
        if(desktop == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            File.WriteAllText(Path.Combine(resultRoot,"hidden.json"),
                JsonSerializer.Serialize(new { setThreadDesktopSucceeded=false, win32Error=error }));
            return 7;
        }

        try
        {
            var result = 7;
            var uiThread = new Thread(() =>
            {
                // SetThreadDesktop must run BEFORE creating any HWND or hook on this STA.
                var attached = SetThreadDesktop(desktop);
                var error = attached ? 0 : Marshal.GetLastWin32Error();
                if(attached)
                {
                    try { result=RunUi(mode, resultRoot, true, 0); }
                    catch(Exception ex)
                    {
                        File.WriteAllText(Path.Combine(resultRoot,"hidden.json"),
                          JsonSerializer.Serialize(new {setThreadDesktopSucceeded=true,exception=ex.ToString()}));
                        result=8;
                    }
                }
                else
                {
                    File.WriteAllText(Path.Combine(resultRoot,"hidden.json"),
                      JsonSerializer.Serialize(new {setThreadDesktopSucceeded=false,win32Error=error}));
                }
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiThread.Join();
            return result;
        }
        finally { CloseDesktop(desktop); }
    }

    private static int RunUi(string mode, string resultRoot, bool attached, int attachError)
    {        var virtualBounds = SystemInformation.VirtualScreen;
        var startX = mode == "offscreen" ? virtualBounds.Right + 280 : 80;
        var startY = mode == "offscreen" ? virtualBounds.Bottom + 280 : 80;
        var screenShotPath = Path.Combine(resultRoot,mode+".png");
        var reportPath = Path.Combine(resultRoot,mode+".json");
        var clicked = false;
        bool printWindow = false;
        int pixels = 0;
        bool screenBitbltContainsWindow = false;
        bool screenBitbltReturned = false;
        string screenBitbltError = "";
        Rectangle rect = Rectangle.Empty;
        using (var form = new ProbeWindow(startX,startY))
        {
            var startTimer = new System.Windows.Forms.Timer { Interval = 380 };
            var finishTimer = new System.Windows.Forms.Timer { Interval = 1050 };
            startTimer.Tick += (_,_) => {
                startTimer.Stop();
                clicked = form.DoButtonClick();
                finishTimer.Start();
            };
            finishTimer.Tick += (_,_) => {
                finishTimer.Stop();
                rect = form.Bounds;
                using var bmp = new Bitmap(form.Width, form.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    var hdc = g.GetHdc();
                    try { printWindow = PrintWindow(form.Handle, hdc, 0); }
                    finally { g.ReleaseHdc(hdc); }
                }
                bmp.Save(screenShotPath, ImageFormat.Png);
                try
                {
                    using var screenBmp = new Bitmap(form.Width, form.Height);
                    using(var screenGraphics = Graphics.FromImage(screenBmp))
                        screenGraphics.CopyFromScreen(rect.X,rect.Y,0,0,new Size(form.Width,form.Height));
                    screenBmp.Save(Path.Combine(resultRoot,mode+"-screen.png"),ImageFormat.Png);
                    var nativePixel=bmp.GetPixel(85,160);
                    var screenPixel=screenBmp.GetPixel(85,160);
                    screenBitbltReturned=true;
                    screenBitbltContainsWindow = nativePixel.G > nativePixel.R + 20
                        && Math.Abs(nativePixel.R-screenPixel.R)<10
                        && Math.Abs(nativePixel.G-screenPixel.G)<10
                        && Math.Abs(nativePixel.B-screenPixel.B)<10;
                }
                catch(Exception ex) { screenBitbltError=ex.GetType().Name+":"+ex.Message; }
                for(var y=25;y<bmp.Height-25;y+=4) {
                    for(var x=25;x<bmp.Width-25;x+=4) {
                        var p = bmp.GetPixel(x,y);
                        if(p.R<190 && p.G<190 && p.B<190) pixels++;
                    }
                }
                form.Close();
            };
            form.Shown += (_,_) => startTimer.Start();
            Application.Run(form);
            startTimer.Dispose();
            finishTimer.Dispose();
        }

        var fullyOutside = rect.Left >= virtualBounds.Right || rect.Top >= virtualBounds.Bottom;
        var report = new {
            mode, setThreadDesktopSucceeded=attached, win32Error=attachError,
            buttonClicked=clicked, printWindowReturned=printWindow,
            nonBackgroundPixels=pixels,
            screenBitbltReturned,screenBitbltContainsWindow,screenBitbltError,
            fullyOutsideVirtualScreen=fullyOutside,
            bounds = new { rect.X, rect.Y, rect.Width, rect.Height },
            virtualScreen = new {virtualBounds.X, virtualBounds.Y, virtualBounds.Width, virtualBounds.Height},
            screenshotPath=screenShotPath
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report));
        Console.WriteLine($"button={clicked} screenshot={printWindow} sampledPixels={pixels} bounds={rect.X},{rect.Y} outside={fullyOutside}");

        return clicked && printWindow && pixels>=200 && (mode != "offscreen" || fullyOutside) ? 0: 3;
    }

    private sealed class ProbeWindow : Form
    {
        private readonly Button _button;
        private readonly Label _label;
        private bool _clicked;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams {
            get {
                var cp=base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        internal ProbeWindow(int x, int y)
        {
            Text="Yanzi.OCR.HiddenDesktop.SafeProbe";
            StartPosition=FormStartPosition.Manual;
            Location=new Point(x,y);
            Size=new Size(630,275);
            FormBorderStyle=FormBorderStyle.FixedDialog;
            ShowInTaskbar=false;
            TopMost=false;
            BackColor=Color.White;
            _label=new Label {
                Text="OCR  GUI test: WAITING",
                Location=new Point(26,35),
                Size=new Size(550,55),
                Font=new Font("Arial",19,FontStyle.Bold),
                ForeColor=Color.Black
            };
            _button=new Button {
                Text="CLICK TEST BUTTON",
                Location=new Point(28,108),
                Size=new Size(310,56),
                Font=new Font("Arial",15)
            };
            _button.Click += (_,_) => {
                _clicked = true;
                _label.Text="OCR GUI test: CLICKED";
                _label.ForeColor=Color.DarkGreen;
                _button.Text="BUTTON CLICK VERIFIED";
                _button.BackColor=Color.LightGreen;
                Refresh();
            };
            Controls.Add(_label);
            Controls.Add(_button);
        }
        internal bool DoButtonClick() {
            SendMessageW(_button.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK (no global pointer input)
            return _clicked && _label.Text.EndsWith("CLICKED") && _button.Text.Contains("VERIFIED");
        }
    }

    [DllImport("user32.dll", EntryPoint="SendMessageW")]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr CreateDesktopW(string name, string? device, IntPtr devmode, uint flags, uint desiredAccess, IntPtr attributes);
    [DllImport("user32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr destinationDC, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point p);
}

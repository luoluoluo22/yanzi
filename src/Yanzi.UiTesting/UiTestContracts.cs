using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;

namespace Yanzi.UiTesting;

/// <summary>
/// One trusted test scenario; executed inside a fresh STA thread bound to a hidden desktop.
/// Scenarios must not send global input, change the user's clipboard, or switch desktops.
/// </summary>
public interface IUiTestScenario
{
    string Name { get; }
    Task RunAsync(UiTestContext context, CancellationToken cancellationToken);
}

public sealed record UiCheck(string Name, bool Passed, string? Detail);
public sealed record UiCapture(string Name, string? WindowPng, string VisualTreePng, bool PrintWindowReturned, int WindowColorVariety);
public sealed record UiWorkerReport(
    string Scenario,
    string Mode,
    bool Passed,
    IReadOnlyList<UiCheck> Checks,
    IReadOnlyList<UiCapture> Captures,
    string? Error,
    DateTimeOffset FinishedAtUtc);

/// <summary>
/// Test context bound to the dedicated WPF dispatcher. All methods must be called
/// from its STA thread. Captures are stored only in the allocated run directory.
/// </summary>
public sealed class UiTestContext
{
    private readonly List<UiCheck> _checks = [];
    private readonly List<UiCapture> _captures = [];
    private readonly string _outputDirectory;
    public string Mode { get; }

    public UiTestContext(string outputDirectory, string mode)
    {
        Mode = mode;
        _outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(_outputDirectory);
    }

    public IReadOnlyList<UiCheck> Checks => _checks;
    public IReadOnlyList<UiCapture> Captures => _captures;
    public bool Passed => _checks.All(check => check.Passed) && _checks.Count > 0;

    public void Check(bool passed, string name, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _checks.Add(new UiCheck(name, passed, detail));
    }

    public void Require(bool passed, string name, string? detail = null)
    {
        Check(passed, name, detail);
        if (!passed) throw new InvalidOperationException($"UI check failed: {name} ({detail})");
    }

    /// <summary>
    /// Dispatch a WPF control's routed Click event. This is not a physical mouse event.
    /// </summary>
    public void ShowWindow(Window window)
    {
        VerifyDispatcher(window);
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (Mode == "offscreen")
        {
            window.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 350;
            window.Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 350;
        }
        else
        {
            window.Left = 70;
            window.Top = 70;
        }
        window.Show();
    }

    public void Click(Button button)
    {
        VerifyDispatcher(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    public UiCapture CaptureWindow(Window window, string name)
    {
        VerifyDispatcher(window);
        if (!window.IsVisible) throw new InvalidOperationException("Cannot capture a hidden/collapsed WPF window.");
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
        if (safe.Length == 0 || safe.Length > 80) throw new ArgumentException("Capture name must be 1-80 safe characters.", nameof(name));
        window.UpdateLayout();

        var w = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var h = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var visualPng = Path.Combine(_outputDirectory, safe + "-visual.png");
        var windowPng = Path.Combine(_outputDirectory, safe + "-window.png");

        var visual = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        visual.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(visual));
        using (var stream = File.Create(visualPng)) encoder.Save(stream);

        var ok = false;
        var variety = 0;
        try
        {
            using var bitmap = new SD.Bitmap(w, h, SD.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = SD.Graphics.FromImage(bitmap))
            {
                graphics.Clear(SD.Color.White);
                var hdc = graphics.GetHdc();
                try
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    ok = PrintWindow(hwnd, hdc, 0);
                }
                finally { graphics.ReleaseHdc(hdc); }
            }
            bitmap.Save(windowPng, SD.Imaging.ImageFormat.Png);
            var palette = new HashSet<int>();
            for (var y = 8; y < h - 8; y += 6)
            for (var x = 8; x < w - 8; x += 6)
            {
                var p = bitmap.GetPixel(x,y);
                palette.Add(((p.R >> 5) << 6) | ((p.G >> 5) << 3) | (p.B >> 5));
                if (palette.Count >= 100) break;
            }
            variety = palette.Count;
        }
        catch (System.ComponentModel.Win32Exception) { ok = false; }
        catch (ExternalException) { ok = false; }

        // PrintWindow can return true yet produce an empty/black bitmap for
        // layered WPF windows. Only advertise usable window captures.
        var capture = new UiCapture(name, ok && variety >= 3 ? windowPng : null, visualPng, ok, variety);
        _captures.Add(capture);
        return capture;
    }

    private static void VerifyDispatcher(DispatcherObject target)
    {
        if (!target.CheckAccess()) throw new InvalidOperationException("UI testing methods must run on their owning STA dispatcher.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
}
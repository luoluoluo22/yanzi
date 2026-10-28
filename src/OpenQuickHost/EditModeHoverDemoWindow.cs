using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WColor = System.Windows.Media.Color;
using WBrushes = System.Windows.Media.Brushes;
using WOrientation = System.Windows.Controls.Orientation;
using WCursors = System.Windows.Input.Cursors;
using WHorizontalAlignment = System.Windows.HorizontalAlignment;
using WVerticalAlignment = System.Windows.VerticalAlignment;

namespace OpenQuickHost;

public static class EditModeGuideCache
{
    private static readonly List<GdiGifFrame> CachedFrames = [];
    private static readonly object LockObj = new();
    private static bool _isLoaded;
    private static Task? _loadingTask;

    public static Task EnsureLoadedAsync()
    {
        lock (LockObj)
        {
            if (_isLoaded) return Task.CompletedTask;
            _loadingTask ??= Task.Run(() => PerformCacheLoad());
            return _loadingTask;
        }
    }

    public static List<GdiGifFrame> GetFrames()
    {
        lock (LockObj)
        {
            return new List<GdiGifFrame>(CachedFrames);
        }
    }

    private static void PerformCacheLoad()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var localAssetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "edit-guide.gif");
        var desktopFallbackPath = @"F:\Desktop\PixPin_2026-08-13_11-59-22.gif";

        var targetPath = File.Exists(localAssetPath)
            ? localAssetPath
            : (File.Exists(desktopFallbackPath) ? desktopFallbackPath : null);

        if (string.IsNullOrEmpty(targetPath)) return;

        try
        {
            using var sysImg = System.Drawing.Image.FromFile(targetPath);
            var dimension = new System.Drawing.Imaging.FrameDimension(sysImg.FrameDimensionsList[0]);
            int totalFrames = sysImg.GetFrameCount(dimension);

            if (totalFrames == 0) return;

            int width = sysImg.Width;
            int height = sysImg.Height;

            byte[]? delayBytes = null;
            try
            {
                var item = sysImg.GetPropertyItem(20736); // PropertyTagFrameDelay
                delayBytes = item?.Value;
            }
            catch { }

            using var canvas = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(canvas);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            int step = totalFrames > 150 ? 2 : 1;

            lock (LockObj)
            {
                CachedFrames.Clear();
                for (int i = 0; i < totalFrames; i += step)
                {
                    sysImg.SelectActiveFrame(dimension, i);
                    g.DrawImage(sysImg, 0, 0, width, height);

                    int delayMs = 33 * step;
                    if (delayBytes != null && (i * 4 + 3) < delayBytes.Length)
                    {
                        int rawDelay = BitConverter.ToInt32(delayBytes, i * 4);
                        if (rawDelay > 0) delayMs = rawDelay * 10 * step;
                    }
                    if (delayMs < 10) delayMs = 33;

                    IntPtr hBitmap = canvas.GetHbitmap();
                    try
                    {
                        var wpfSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        if (wpfSource.CanFreeze) wpfSource.Freeze();
                        CachedFrames.Add(new GdiGifFrame { Source = wpfSource, DelayMs = delayMs });
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
                _isLoaded = true;
            }

            sw.Stop();
            HostAssets.AppendLog($"[GifLog] Pre-cached {totalFrames} frames into {CachedFrames.Count} frames in {sw.ElapsedMilliseconds}ms ONCE globally.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[GifLog] PerformCacheLoad exception: {ex}");
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

public class EditModeHoverDemoWindow : Window
{
    private readonly RobustGifPlayerControl _gifControl;

    public EditModeHoverDemoWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WBrushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Focusable = true;

        var outerBorder = new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(0xF8, 0x12, 0x16, 0x22)),
            BorderBrush = new SolidColorBrush(WColor.FromRgb(0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.65
            }
        };

        var mainStack = new StackPanel();

        // Header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = WOrientation.Horizontal, VerticalAlignment = WVerticalAlignment.Center };
        var titleIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z"),
            Fill = new SolidColorBrush(WColor.FromRgb(0x60, 0xA5, 0xFA)),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var titleText = new TextBlock
        {
            Text = "编辑功能操作演示（点击图片查看大图）",
            Foreground = WBrushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = WVerticalAlignment.Center
        };
        titleStack.Children.Add(titleIcon);
        titleStack.Children.Add(titleText);
        Grid.SetColumn(titleStack, 0);

        var closeBtn = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = WBrushes.Transparent,
            Cursor = WCursors.Hand,
            HorizontalAlignment = WHorizontalAlignment.Right,
            VerticalAlignment = WVerticalAlignment.Center
        };
        var closeText = new TextBlock
        {
            Text = "✕",
            Foreground = new SolidColorBrush(WColor.FromRgb(0x9C, 0xA3, 0xAF)),
            FontSize = 11,
            HorizontalAlignment = WHorizontalAlignment.Center,
            VerticalAlignment = WVerticalAlignment.Center
        };
        closeBtn.Child = closeText;
        closeBtn.MouseEnter += (s, e) => closeBtn.Background = new SolidColorBrush(WColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        closeBtn.MouseLeave += (s, e) => closeBtn.Background = WBrushes.Transparent;
        closeBtn.MouseLeftButtonUp += (s, e) => Close();
        Grid.SetColumn(closeBtn, 1);

        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(closeBtn);
        mainStack.Children.Add(headerGrid);

        // Image Container - Interactive click opens native GIF file in system viewer
        var imageBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = new SolidColorBrush(WColor.FromRgb(0x0B, 0x0F, 0x17)),
            BorderBrush = new SolidColorBrush(WColor.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = WCursors.Hand,
            ToolTip = "点击在系统查看器中打开高清 GIF 原图大图"
        };

        imageBorder.MouseEnter += (s, e) => imageBorder.BorderBrush = new SolidColorBrush(WColor.FromRgb(0x3B, 0x82, 0xF6));
        imageBorder.MouseLeave += (s, e) => imageBorder.BorderBrush = new SolidColorBrush(WColor.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

        _gifControl = new RobustGifPlayerControl();
        imageBorder.Child = _gifControl;
        mainStack.Children.Add(imageBorder);

        Action handleOpenImage = () =>
        {
            HostAssets.AppendLog("[GifLog] Click detected on demo window image card.");
            OpenGifFileInSystemViewer();
            Close();
        };

        imageBorder.MouseLeftButtonDown += (s, e) => { handleOpenImage(); e.Handled = true; };
        imageBorder.MouseLeftButtonUp += (s, e) => { handleOpenImage(); e.Handled = true; };
        imageBorder.PreviewMouseLeftButtonDown += (s, e) => { handleOpenImage(); e.Handled = true; };
        imageBorder.PreviewMouseLeftButtonUp += (s, e) => { handleOpenImage(); e.Handled = true; };

        // Footer Hint
        var hintText = new TextBlock
        {
            Text = "💡 点击预览图片可在系统查看器中打开高清大图。开启编辑模式后可自由拖拽与管理槽位。",
            Foreground = new SolidColorBrush(WColor.FromRgb(0x9C, 0xA3, 0xAF)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = WHorizontalAlignment.Center
        };
        mainStack.Children.Add(hintText);

        outerBorder.Child = mainStack;
        Content = outerBorder;

        MouseEnter += (s, e) => IsMouseOverWindow = true;
        MouseLeave += (s, e) =>
        {
            IsMouseOverWindow = false;
            MouseLeftDemoWindow?.Invoke(this, EventArgs.Empty);
        };

        _ = LoadAnimationAsync();
        Closed += (s, e) => _gifControl.StopAnimation();
    }

    public bool IsMouseOverWindow { get; private set; }
    public event EventHandler? MouseLeftDemoWindow;

    private async Task LoadAnimationAsync()
    {
        await EditModeGuideCache.EnsureLoadedAsync();
        _gifControl.StartCachedAnimation();
    }

    public static void OpenGifFileInSystemViewer()
    {
        HostAssets.AppendLog("[GifLog] OpenGifFileInSystemViewer invoked by user click.");

        var binAssetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "edit-guide.gif");
        var appDataPath = Path.Combine(HostAssets.DataRootPath, "edit-guide.gif");
        var desktopFallbackPath = @"F:\Desktop\PixPin_2026-08-13_11-59-22.gif";

        HostAssets.AppendLog($"[GifLog] Paths check: binAssetPath='{binAssetPath}' (exists={File.Exists(binAssetPath)}), appDataPath='{appDataPath}' (exists={File.Exists(appDataPath)}), desktopFallbackPath='{desktopFallbackPath}' (exists={File.Exists(desktopFallbackPath)}).");

        string? targetPath = null;
        if (File.Exists(binAssetPath)) targetPath = binAssetPath;
        else if (File.Exists(appDataPath)) targetPath = appDataPath;
        else if (File.Exists(desktopFallbackPath)) targetPath = desktopFallbackPath;

        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
        {
            HostAssets.AppendLog("[GifLog] Error: No valid GIF file path found on disk!");
            return;
        }

        try
        {
            Directory.CreateDirectory(HostAssets.DataRootPath);
            if (!File.Exists(appDataPath) || new FileInfo(appDataPath).Length != new FileInfo(targetPath).Length)
            {
                File.Copy(targetPath, appDataPath, true);
            }
            if (File.Exists(appDataPath))
            {
                targetPath = appDataPath;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[GifLog] AppData copy failed: {ex.Message}");
        }

        HostAssets.AppendLog($"[GifLog] Attempting to open target GIF: '{targetPath}'");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true,
                Verb = "open"
            };
            Process.Start(psi);
            HostAssets.AppendLog("[GifLog] Process.Start with UseShellExecute succeeded.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[GifLog] Process.Start failed: {ex.Message}. Trying explorer.exe fallback...");
            try
            {
                Process.Start("explorer.exe", $"\"{targetPath}\"");
                HostAssets.AppendLog("[GifLog] Process.Start explorer.exe succeeded.");
            }
            catch (Exception ex2)
            {
                HostAssets.AppendLog($"[GifLog] Explorer fallback failed: {ex2.Message}");
            }
        }
    }

    public void PositionToRightOf(Window parentWindow)
    {
        var workArea = SystemParameters.WorkArea;
        var left = parentWindow.Left + parentWindow.Width + 10;
        var top = parentWindow.Top;

        if (left + Width > workArea.Right)
        {
            left = parentWindow.Left - Width - 10;
        }

        Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - Height));
    }
}

public class GdiGifFrame
{
    public ImageSource Source { get; set; } = null!;
    public int DelayMs { get; set; } = 33;
}

public class RobustGifPlayerControl : Grid
{
    private readonly System.Windows.Controls.Image _imageControl;
    private List<GdiGifFrame> _frames = [];
    private int _frameIndex = 0;
    private DispatcherTimer? _timer;

    public RobustGifPlayerControl()
    {
        Background = WBrushes.Transparent;
        _imageControl = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Uniform
        };
        Children.Add(_imageControl);
    }

    public void StartCachedAnimation()
    {
        StopAnimation();
        _frames = EditModeGuideCache.GetFrames();
        _frameIndex = 0;

        if (_frames.Count > 0)
        {
            _imageControl.Source = _frames[0].Source;

            if (_frames.Count > 1)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render);
                _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DelayMs);
                _timer.Tick += (s, e) =>
                {
                    if (_frames.Count == 0) return;
                    _frameIndex = (_frameIndex + 1) % _frames.Count;
                    _imageControl.Source = _frames[_frameIndex].Source;
                    _timer.Interval = TimeSpan.FromMilliseconds(_frames[_frameIndex].DelayMs);
                };
                _timer.Start();
            }
        }
    }

    public void StopAnimation()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer = null;
        }
        _frames.Clear();
    }
}

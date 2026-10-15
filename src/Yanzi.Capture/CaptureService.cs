using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

public static class ScreenCaptureService
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(
        IntPtr dest, int xDest, int yDest, int width, int height,
        IntPtr src, int xSrc, int ySrc, int rop);

    public static int PrimaryWidth => GetSystemMetrics(SmCxScreen);
    public static int PrimaryHeight => GetSystemMetrics(SmCyScreen);

    public static BitmapSource CapturePrimary() => Capture(0, 0, PrimaryWidth, PrimaryHeight);

    public static BitmapSource CaptureCenterRegion()
    {
        var width = Math.Min(1180, PrimaryWidth);
        var height = Math.Min(760, PrimaryHeight);
        var x = Math.Max(0, (PrimaryWidth - width) / 2);
        var y = Math.Max(0, (PrimaryHeight - height) / 2);
        return Capture(x, y, width, height);
    }

    public static BitmapSource Capture(int x, int y, int width, int height)
    {
        var totalStart = System.Diagnostics.Stopwatch.GetTimestamp();
        CaptureDiagnostics.Mark("capture.gdi.begin",
            ("x", x), ("y", y), ("width", width), ("height", height));

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var old = SelectObject(memoryDc, bitmap);
        try
        {
            var bltStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt))
                throw new InvalidOperationException("无法捕获屏幕。");
            CaptureDiagnostics.Mark("capture.gdi.bitblt.end",
                ("durationMs", System.Diagnostics.Stopwatch.GetElapsedTime(bltStart).TotalMilliseconds.ToString("F2")));

            var convertStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            CaptureDiagnostics.Mark("capture.gdi.bitmap.ready",
                ("durationMs", System.Diagnostics.Stopwatch.GetElapsedTime(convertStart).TotalMilliseconds.ToString("F2")),
                ("totalMs", System.Diagnostics.Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds.ToString("F2")));
            return source;
        }
        finally
        {
            SelectObject(memoryDc, old);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static BitmapSource Crop(BitmapSource source, Int32Rect rect)
    {
        rect.X = Math.Clamp(rect.X, 0, source.PixelWidth - 1);
        rect.Y = Math.Clamp(rect.Y, 0, source.PixelHeight - 1);
        rect.Width = Math.Clamp(rect.Width, 1, source.PixelWidth - rect.X);
        rect.Height = Math.Clamp(rect.Height, 1, source.PixelHeight - rect.Y);

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var crop = new CroppedBitmap(source, rect);

        // Materialize the selected pixels into an independent bitmap.
        // CroppedBitmap keeps the full-screen source alive, which made even
        // tiny captures retain the entire 2560x1440 backing image.
        var stride = (crop.PixelWidth * crop.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * crop.PixelHeight];
        crop.CopyPixels(pixels, stride, 0);

        var materialized = BitmapSource.Create(
            crop.PixelWidth,
            crop.PixelHeight,
            crop.DpiX,
            crop.DpiY,
            crop.Format,
            crop.Palette,
            pixels,
            stride);
        materialized.Freeze();

        CaptureDiagnostics.Mark("capture.crop.materialized",
            ("width", materialized.PixelWidth),
            ("height", materialized.PixelHeight),
            ("bytes", pixels.Length),
            ("durationMs", System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds.ToString("F2")));

        return materialized;
    }
}

public static class CaptureFactory
{
    public static CaptureDocument CreateDemoDocument()
    {
        const int width = 1280;
        const int height = 760;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244, 246, 249)), null, new Rect(0, 0, width, height));
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(70, 64, 1140, 632), 24, 24);
            dc.DrawText(Text("燕子截图 · Capture Studio", 38, FontWeights.SemiBold, Color.FromRgb(28, 32, 40)), new Point(120, 112));
            dc.DrawText(Text("区域截图  ·  OCR  ·  标注  ·  图片叠加  ·  未来支持录屏", 20, FontWeights.Normal, Color.FromRgb(93, 102, 116)), new Point(122, 175));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(238, 246, 255)), null, new Rect(120, 245, 1040, 180), 18, 18);
            dc.DrawText(Text("OCR 测试文字", 28, FontWeights.SemiBold, Color.FromRgb(36, 92, 165)), new Point(160, 286));
            dc.DrawText(Text("识别屏幕上的文字，然后复制、翻译或交给燕语继续处理。", 22, FontWeights.Normal, Color.FromRgb(51, 59, 70)), new Point(160, 338));
            dc.DrawText(Text("Yanzi turns the screen into an input surface.", 21, FontWeights.Normal, Color.FromRgb(51, 59, 70)), new Point(160, 376));
            dc.DrawText(Text("这是用于自动化迭代的安全演示画面，不会截取隐私内容。", 18, FontWeights.Normal, Color.FromRgb(110, 117, 128)), new Point(124, 520));
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return new CaptureDocument(target);
    }

    private static FormattedText Text(string text, double size, FontWeight weight, Color color) =>
        new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, new SolidColorBrush(color), 1.0);
}

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Yanzi.Capture;

public static class WindowsOcrService
{
    public static async Task<string> RecognizeAsync(BitmapSource bitmap)
    {
        var totalStart = System.Diagnostics.Stopwatch.GetTimestamp();
        CaptureDiagnostics.Mark(
            "ocr.begin",
            ("width", bitmap.PixelWidth),
            ("height", bitmap.PixelHeight));

        var temp = Path.Combine(
            Path.GetTempPath(),
            "yanzi-ocr-" + Guid.NewGuid().ToString("N") + ".png");
        EditorWindow.SavePng(bitmap, temp);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(temp);
            using IRandomAccessStream stream =
                await file.OpenAsync(FileAccessMode.Read);
            var decoder =
                await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap =
                await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);

            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new InvalidOperationException(
                    "Windows OCR 不可用，请在 Windows 语言设置中安装 OCR 语言包。");

            var recognizeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = await engine.RecognizeAsync(softwareBitmap);
            var text = string.Join(
                Environment.NewLine,
                result.Lines.Select(line => NormalizeLine(line.Text)));

            CaptureDiagnostics.Mark(
                "ocr.end",
                ("recognizeMs", System.Diagnostics.Stopwatch.GetElapsedTime(recognizeStart).TotalMilliseconds.ToString("F2")),
                ("totalMs", System.Diagnostics.Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds.ToString("F2")),
                ("chars", text.Length));

            return text;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static string NormalizeLine(string? line)
    {
        var text = (line ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        text = Regex.Replace(
            text,
            @"(?<=[㐀-鿿])s+(?=[㐀-鿿])",
            string.Empty);
        text = Regex.Replace(
            text,
            @"s+([，。！？；：、）》】])",
            "$1");
        text = Regex.Replace(
            text,
            @"([（《【])s+",
            "$1");

        return text;
    }
}

public sealed class OcrResultWindow : Window
{
    public OcrResultWindow(string text)
    {
        Title = "文字识别";
        Width = 700;
        Height = 450;
        MinWidth = 540;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Brush("#171A20");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var shell = new Grid();
        shell.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(52) });
        shell.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = shell;

        var header = new Grid
        {
            Background = Brush("#15181E")
        };
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            try { DragMove(); } catch { }
        };

        header.Children.Add(new TextBlock
        {
            Text = "文字识别",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0)
        });

        var closeHeader = MakeButton("×", "capture.ocr.header-close", false);
        closeHeader.Width = 42;
        closeHeader.MinWidth = 42;
        closeHeader.Height = 36;
        closeHeader.Margin = new Thickness(0, 8, 8, 8);
        closeHeader.FontSize = 18;
        closeHeader.Click += (_, _) => Close();
        Grid.SetColumn(closeHeader, 1);
        header.Children.Add(closeHeader);
        shell.Children.Add(header);

        var body = new Grid
        {
            Margin = new Thickness(20, 16, 20, 18)
        };
        body.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(10) });
        body.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(14) });
        body.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        body.Children.Add(new TextBlock
        {
            Text = "识别到的文字",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });

        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush("#20242B"),
            Foreground = Brush("#E8ECF2"),
            BorderBrush = Brush("#353B46"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            FontSize = 14,
            SelectionBrush = Brush("#3A8DFF")
        };
        AutomationProperties.SetAutomationId(box, "capture.ocr.text");
        Grid.SetRow(box, 2);
        body.Children.Add(box);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var copy = MakeButton(
            "复制文字",
            "capture.ocr.copy",
            false);
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(box.Text ?? string.Empty);
            copy.Content = "已复制";
        };

        var done = MakeButton(
            "完成",
            "capture.ocr.close",
            true);
        done.Margin = new Thickness(8, 0, 0, 0);
        done.Click += (_, _) => Close();

        actions.Children.Add(copy);
        actions.Children.Add(done);
        Grid.SetRow(actions, 4);
        body.Children.Add(actions);

        Grid.SetRow(body, 1);
        shell.Children.Add(body);

        Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }

    private static Button MakeButton(
        string label,
        string id,
        bool primary)
    {
        var button = new Button
        {
            Content = label,
            Height = 36,
            MinWidth = 86,
            Padding = new Thickness(14, 0, 14, 0),
            Background = primary ? Brush("#3A8DFF") : Brush("#292E36"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace Yanzi.Capture;

public sealed class EditorWindow : Window
{
    private readonly CaptureDocument _document;
    private readonly EditorSurface _surface;
    private readonly TextBlock _status = new();
    private readonly Dictionary<EditorTool, Button> _toolButtons = [];

    private readonly SolidColorBrush _surfaceBrush = Brush("#20242B");
    private readonly SolidColorBrush _hoverBrush = Brush("#2A3039");
    private readonly SolidColorBrush _accentBrush = Brush("#2D71D2");
    private readonly SolidColorBrush _mutedBrush = Brush("#9CA6B5");

    private Button? _historyUndo;
    private Button? _historyRedo;

    public EditorWindow(CaptureDocument document)
    {
        _document = document;
        _surface = new EditorSurface(document);
        _surface.StatusChanged = SetStatus;
        _surface.TextFactory = CreateTextAnnotation;

        AutomationProperties.SetAutomationId(
            _surface,
            "capture.surface");
        AutomationProperties.SetName(
            _surface,
            "截图编辑画布");

        Title = "燕子截图";
        Width = 1180;
        Height = 800;
        MinWidth = 980;
        MinHeight = 640;
        Background = Brush("#111318");
        Foreground = Brushes.White;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

        var root = new Grid();
        root.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(54) });
        root.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        root.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(76) });

        Content = root;

        BuildTitleBar(root);

        _surface.Margin = new Thickness(14, 12, 14, 6);
        Grid.SetRow(_surface, 1);
        root.Children.Add(_surface);

        BuildContextBar(root);
        BuildBottomToolbar(root);

        _document.History.Changed += RefreshHistoryState;

        Loaded += (_, _) =>
        {
            SelectTool(EditorTool.Select);
            RefreshHistoryState();
            _surface.Focus();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        };
    }

    private void BuildTitleBar(Grid root)
    {
        var bar = new Grid
        {
            Background = Brush("#171A20"),
            Margin = new Thickness(0, 0, 0, 1)
        };

        bar.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(54) });
        bar.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        bar.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button)
                return;

            if (e.ClickCount == 2)
            {
                WindowState =
                    WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        };

        var brandHost = new Grid
        {
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        brandHost.Children.Add(
            new ShapePath
            {
                Data = Geometry.Parse(
                    "M2,14 C7,12 10,7 14,3 " +
                    "C13,8 16,10 24,8 " +
                    "C19,12 15,15 12,24 " +
                    "C10,18 7,16 2,14 Z"),
                Fill = Brush("#72AEFF"),
                Stretch = Stretch.Uniform,
                Width = 25,
                Height = 25
            });

        bar.Children.Add(brandHost);

        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        titleStack.Children.Add(
            new TextBlock
            {
                Text = "燕子截图",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

        titleStack.Children.Add(
            new TextBlock
            {
                Text =
                    $"  {_document.BaseImage.PixelWidth} × " +
                    $"{_document.BaseImage.PixelHeight}",
                FontSize = 11,
                Foreground = _mutedBrush,
                VerticalAlignment = VerticalAlignment.Center
            });

        Grid.SetColumn(titleStack, 1);
        bar.Children.Add(titleStack);

        var windowActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        windowActions.Children.Add(
            WindowButton(
                "—",
                "capture.minimize",
                () => WindowState = WindowState.Minimized));

        windowActions.Children.Add(
            WindowButton(
                "□",
                "capture.maximize",
                () =>
                    WindowState =
                        WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized));

        windowActions.Children.Add(
            WindowButton(
                "×",
                "capture.close",
                Close,
                close: true));

        Grid.SetColumn(windowActions, 2);
        bar.Children.Add(windowActions);

        Grid.SetRow(bar, 0);
        root.Children.Add(bar);
    }

    private void BuildContextBar(Grid root)
    {
        var bar = new Grid
        {
            Background = Brush("#14171C"),
            Margin = new Thickness(0, 0, 0, 1)
        };

        bar.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        bar.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        _status.Text = "选择工具后拖动标注；Esc 退出";
        _status.Foreground = _mutedBrush;
        _status.FontSize = 11;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(18, 0, 12, 0);
        bar.Children.Add(_status);

        var style = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };

        style.Children.Add(
            ContextLabel("颜色"));

        AddColorSwatch(
            style,
            "#FF4D4F",
            "capture.style.red");
        AddColorSwatch(
            style,
            "#3A8DFF",
            "capture.style.blue");
        AddColorSwatch(
            style,
            "#F4C542",
            "capture.style.yellow");
        AddColorSwatch(
            style,
            "#FFFFFF",
            "capture.style.white");
        AddColorSwatch(
            style,
            "#181818",
            "capture.style.black");

        style.Children.Add(
            new Border
            {
                Width = 1,
                Height = 18,
                Background = Brush("#303640"),
                Margin = new Thickness(10, 0, 10, 0)
            });

        style.Children.Add(
            ContextLabel("粗细"));

        AddThicknessButton(
            style,
            "细",
            2,
            "capture.style.thin");
        AddThicknessButton(
            style,
            "中",
            5,
            "capture.style.medium");
        AddThicknessButton(
            style,
            "粗",
            9,
            "capture.style.thick");

        Grid.SetColumn(style, 1);
        bar.Children.Add(style);

        Grid.SetRow(bar, 2);
        root.Children.Add(bar);
    }

    private void BuildBottomToolbar(Grid root)
    {
        var shell = new Border
        {
            Background = Brush("#15181E"),
            BorderBrush = Brush("#2A2F38"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var dock = new DockPanel
        {
            LastChildFill = true
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        actions.Children.Add(
            BottomActionButton(
                "copy",
                "复制",
                "capture.copy",
                CopyImage));

        actions.Children.Add(
            BottomActionButton(
                "done",
                "完成",
                "capture.done",
                Close));

        var save = BottomActionButton(
            "save",
            "保存",
            "capture.save",
            SaveImage,
            primary: true);

        save.Margin = new Thickness(8, 0, 0, 0);
        save.MinWidth = 92;
        actions.Children.Add(save);

        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        AddTool(
            tools,
            EditorTool.Select,
            "select",
            "选择",
            "capture.tool.select");
        AddTool(
            tools,
            EditorTool.Rectangle,
            "rectangle",
            "矩形",
            "capture.tool.rectangle");
        AddTool(
            tools,
            EditorTool.Arrow,
            "arrow",
            "箭头",
            "capture.tool.arrow");
        AddTool(
            tools,
            EditorTool.Pen,
            "pen",
            "画笔",
            "capture.tool.pen");
        AddTool(
            tools,
            EditorTool.Text,
            "text",
            "文字",
            "capture.tool.text");
        AddTool(
            tools,
            EditorTool.Mosaic,
            "mosaic",
            "马赛克",
            "capture.tool.mosaic");

        tools.Children.Add(
            VerticalSeparator());

        var addImage = ToolButton(
            "image",
            "图片",
            "capture.add-image",
            AddImage);
        addImage.ToolTip = "把另一张图片叠加到截图中";
        tools.Children.Add(addImage);

        tools.Children.Add(
            ToolButton(
                "ocr",
                "OCR",
                "capture.ocr",
                async () => await RunOcrAsync()));

        tools.Children.Add(
            ToolButton(
                "pin",
                "贴图",
                "capture.pin",
                PinImage));

        tools.Children.Add(
            VerticalSeparator());

        _historyUndo = ToolButton(
            "undo",
            "撤销",
            "capture.undo",
            () => _document.History.Undo());

        _historyRedo = ToolButton(
            "redo",
            "重做",
            "capture.redo",
            () => _document.History.Redo());

        tools.Children.Add(_historyUndo);
        tools.Children.Add(_historyRedo);

        dock.Children.Add(tools);
        shell.Child = dock;

        Grid.SetRow(shell, 3);
        root.Children.Add(shell);
    }

    private void AddTool(
        Panel parent,
        EditorTool tool,
        string icon,
        string label,
        string id)
    {
        var button = ToolButton(
            icon,
            label,
            id,
            () => SelectTool(tool));

        _toolButtons[tool] = button;
        parent.Children.Add(button);
    }

    private void SelectTool(EditorTool tool)
    {
        _surface.Tool = tool;
        _surface.SelectAnnotation(null);

        foreach (var pair in _toolButtons)
        {
            pair.Value.Background =
                pair.Key == tool
                    ? _accentBrush
                    : Brushes.Transparent;
        }

        _surface.Cursor =
            tool == EditorTool.Select
                ? Cursors.Arrow
                : Cursors.Cross;

        SetStatus(
            tool switch
            {
                EditorTool.Select =>
                    "选择：点击标注后拖动，Delete 删除",
                EditorTool.Rectangle =>
                    "矩形：拖动框出重点区域",
                EditorTool.Arrow =>
                    "箭头：从起点拖向目标",
                EditorTool.Pen =>
                    "画笔：按住鼠标自由绘制",
                EditorTool.Text =>
                    "文字：点击画面选择文字位置",
                EditorTool.Mosaic =>
                    "马赛克：拖动覆盖需要隐藏的区域",
                _ => "准备就绪"
            });

        _surface.Focus();
    }

    private Button ToolButton(
        string icon,
        string label,
        string id,
        Action action)
    {
        var button = ButtonBase(string.Empty, id);
        button.Width = 58;
        button.Height = 58;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Background = Brushes.Transparent;

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(
            CreateIcon(icon, Brushes.White));

        stack.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });

        button.Content = stack;

        button.MouseEnter += (_, _) =>
        {
            var selected =
                _toolButtons.Any(
                    x =>
                        x.Value == button &&
                        x.Key == _surface.Tool);

            if (!selected)
                button.Background = _hoverBrush;
        };

        button.MouseLeave += (_, _) =>
        {
            var selected =
                _toolButtons.Any(
                    x =>
                        x.Value == button &&
                        x.Key == _surface.Tool);

            button.Background =
                selected
                    ? _accentBrush
                    : Brushes.Transparent;
        };

        button.Click += (_, _) => action();

        return button;
    }

    private Button BottomActionButton(
        string icon,
        string label,
        string id,
        Action action,
        bool primary = false)
    {
        var button = ButtonBase(string.Empty, id);
        button.Height = 46;
        button.MinWidth = 76;
        button.Margin = new Thickness(6, 0, 0, 0);
        button.Padding = new Thickness(12, 0, 12, 0);
        button.Background =
            primary
                ? Brush("#1976F3")
                : Brush("#252A32");

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(
            CreateIcon(icon, Brushes.White, 18));

        stack.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight =
                    primary
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

        button.Content = stack;

        button.MouseEnter += (_, _) =>
        {
            button.Background =
                primary
                    ? Brush("#3185F6")
                    : Brush("#303640");
        };

        button.MouseLeave += (_, _) =>
        {
            button.Background =
                primary
                    ? Brush("#1976F3")
                    : Brush("#252A32");
        };

        button.Click += (_, _) => action();

        return button;
    }

    private static FrameworkElement CreateIcon(
        string icon,
        Brush foreground,
        double size = 20)
    {
        if (icon == "text" || icon == "ocr")
        {
            return new TextBlock
            {
                Text = icon == "text" ? "T" : "OCR",
                Foreground = foreground,
                FontSize = icon == "text" ? 18 : 10,
                FontWeight = FontWeights.SemiBold,
                Width = size + 4,
                Height = size,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        var data = icon switch
        {
            "select" =>
                "M3,2 L3,20 L8,15 L12,23 L15,21 L11,14 L20,14 Z",
            "rectangle" =>
                "M3,4 L21,4 L21,20 L3,20 Z",
            "arrow" =>
                "M4,20 L20,4 M11,4 L20,4 L20,13",
            "pen" =>
                "M4,20 L8,19 L20,7 L17,4 L5,16 Z",
            "mosaic" =>
                "M3,3 H9 V9 H3 Z M13,3 H21 V11 H13 Z " +
                "M3,13 H11 V21 H3 Z M15,15 H21 V21 H15 Z",
            "image" =>
                "M3,4 H21 V20 H3 Z " +
                "M5,17 L10,11 L14,15 L17,12 L20,17",
            "pin" =>
                "M8,3 H16 L15,9 L19,13 H13 L12,21 L11,13 H5 L9,9 Z",
            "undo" =>
                "M9,7 L4,12 L9,17 M5,12 H14 C19,12 21,15 21,19",
            "redo" =>
                "M15,7 L20,12 L15,17 M19,12 H10 C5,12 3,15 3,19",
            "copy" =>
                "M8,7 H20 V20 H8 Z M4,3 H16 V7 M4,3 V16 H8",
            "done" =>
                "M4,12 L9,17 L20,6",
            "save" =>
                "M4,3 H20 V21 H4 Z M8,3 V9 H16 V3 M8,16 H16 V21",
            _ =>
                "M4,4 H20 V20 H4 Z"
        };

        return new ShapePath
        {
            Data = Geometry.Parse(data),
            Stroke = foreground,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill =
                icon is "select" or "pen" or "mosaic" or "pin"
                    ? foreground
                    : Brushes.Transparent,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private Button WindowButton(
        string label,
        string id,
        Action action,
        bool close = false)
    {
        var button = ButtonBase(label, id);
        button.Width = 38;
        button.Height = 34;
        button.Margin = new Thickness(2, 0, 0, 0);
        button.Background = Brushes.Transparent;
        button.FontSize = label == "×" ? 20 : 14;

        button.MouseEnter += (_, _) =>
            button.Background =
                close
                    ? Brush("#C6424A")
                    : _hoverBrush;

        button.MouseLeave += (_, _) =>
            button.Background = Brushes.Transparent;

        button.Click += (_, _) => action();
        return button;
    }

    private Button ButtonBase(string label, string id)
    {
        var button = new Button
        {
            Content = label,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        AutomationProperties.SetAutomationId(button, id);

        var border = new FrameworkElementFactory(
            typeof(Border));
        border.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(8));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });

        var presenter = new FrameworkElementFactory(
            typeof(ContentPresenter));
        presenter.SetValue(
            HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            VerticalAlignmentProperty,
            VerticalAlignment.Center);

        border.AppendChild(presenter);

        button.Template =
            new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };

        return button;
    }

    private static FrameworkElement ContextLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush("#8993A2"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
    }

    private void AddColorSwatch(
        Panel parent,
        string hex,
        string id)
    {
        var colorBrush = Brush(hex);
        var button = ButtonBase(string.Empty, id);

        button.Width = 22;
        button.Height = 22;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Background = colorBrush;
        button.ToolTip = "标注颜色";

        button.Click += (_, _) =>
        {
            _surface.StrokeBrush = colorBrush;
            _surface.Focus();
        };

        parent.Children.Add(button);
    }

    private void AddThicknessButton(
        Panel parent,
        string label,
        double thickness,
        string id)
    {
        var button = ButtonBase(label, id);

        button.Width = 34;
        button.Height = 26;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Background = _surfaceBrush;
        button.FontSize = 10;

        button.Click += (_, _) =>
        {
            _surface.StrokeThickness = thickness;
            _surface.TextSize =
                thickness switch
                {
                    <= 2 => 20,
                    >= 9 => 38,
                    _ => 28
                };

            _surface.Focus();
        };

        parent.Children.Add(button);
    }

    private static FrameworkElement VerticalSeparator()
    {
        return new Border
        {
            Width = 1,
            Height = 36,
            Background = Brush("#303640"),
            Margin = new Thickness(7, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void RefreshHistoryState()
    {
        if (_historyUndo is not null)
            _historyUndo.IsEnabled = _document.History.CanUndo;

        if (_historyRedo is not null)
            _historyRedo.IsEnabled = _document.History.CanRedo;
    }

    private TextAnnotation? CreateTextAnnotation(Point origin)
    {
        var dialog = new TextInputWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.EnteredText))
        {
            return null;
        }

        return new TextAnnotation(
            origin,
            dialog.EnteredText,
            _surface.StrokeBrush,
            _surface.TextSize);
    }

    private void PinImage()
    {
        try
        {
            var bitmap =
                DocumentRenderer.Render(_document);

            var pin = new PinWindow(bitmap);
            pin.Show();

            SetStatus(
                "已贴到屏幕，可拖动或双击关闭");
        }
        catch (Exception ex)
        {
            SetStatus(
                "贴图失败：" + ex.Message);
        }
    }

    private void AddImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加图片",
            Filter =
                "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(dialog.FileName);
        image.EndInit();
        image.Freeze();

        var maxW =
            Math.Min(
                420d,
                _document.BaseImage.PixelWidth * 0.45);

        var maxH =
            Math.Min(
                320d,
                _document.BaseImage.PixelHeight * 0.45);

        var scale =
            Math.Min(
                1,
                Math.Min(
                    maxW / image.PixelWidth,
                    maxH / image.PixelHeight));

        var width =
            Math.Max(
                40,
                image.PixelWidth * scale);

        var height =
            Math.Max(
                40,
                image.PixelHeight * scale);

        var rect = new Rect(
            (_document.BaseImage.PixelWidth - width) / 2,
            (_document.BaseImage.PixelHeight - height) / 2,
            width,
            height);

        var annotation =
            new ImageAnnotation(
                image,
                rect);

        _document.History.Execute(
            new AddAnnotationCommand(
                _document,
                annotation));

        SelectTool(EditorTool.Select);
        _surface.SelectAnnotation(annotation);

        SetStatus(
            "已添加图片，可拖动调整位置");
    }

    private void CopyImage()
    {
        try
        {
            Clipboard.SetImage(
                DocumentRenderer.Render(_document));

            SetStatus(
                "已复制图片到剪贴板");
        }
        catch (Exception ex)
        {
            SetStatus(
                "复制失败：" + ex.Message);
        }
    }

    private void SaveImage()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            FileName =
                "Yanzi-" +
                DateTime.Now.ToString(
                    "yyyyMMdd-HHmmss") +
                ".png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SavePng(
                DocumentRenderer.Render(_document),
                dialog.FileName);

            SetStatus(
                "已保存：" + dialog.FileName);
        }
        catch (Exception ex)
        {
            SetStatus(
                "保存失败：" + ex.Message);
        }
    }

    private async Task RunOcrAsync()
    {
        SetStatus(
            "正在识别文字…");

        try
        {
            var text =
                await WindowsOcrService.RecognizeAsync(
                    DocumentRenderer.Render(_document));

            var result =
                new OcrResultWindow(text)
                {
                    Owner = this
                };

            result.ShowDialog();

            SetStatus(
                string.IsNullOrWhiteSpace(text)
                    ? "没有识别到文字"
                    : "OCR 完成");
        }
        catch (Exception ex)
        {
            SetStatus(
                "OCR 失败：" + ex.Message);
        }
    }

    internal static void SavePng(
        BitmapSource bitmap,
        string path)
    {
        var encoder =
            new PngBitmapEncoder();

        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        var directory =
            IOPath.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream =
            File.Create(path);

        encoder.Save(stream);
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    private static SolidColorBrush Brush(string hex)
    {
        return
            (SolidColorBrush)
            new BrushConverter()
                .ConvertFromString(hex)!;
    }
}

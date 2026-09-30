using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Yanzi.Capture;

internal enum SelectionHandle
{
    None,
    Create,
    Move,
    North,
    South,
    East,
    West,
    NorthWest,
    NorthEast,
    SouthWest,
    SouthEast
}

public sealed class CaptureWorkspaceWindow : Window
{
    private readonly BitmapSource _screen;
    private readonly string _extensionDataDirectory;

    private readonly Canvas _canvas = new();
    private readonly Image _screenImage = new();
    private readonly Rectangle _selectionPreview = new();
    private readonly Rectangle _selectionBorder = new();
    private readonly TextBlock _hint = new();
    private readonly TextBlock _sizeBadge = new();
    private readonly Border _sizeBadgeShell = new();
    private readonly Border _toolbar = new();
    private readonly Border _ratioControl = new();
    private readonly Border _ratioFlyout = new();
    private readonly TextBox _ratioWidthBox = new();
    private readonly TextBox _ratioHeightBox = new();
    private readonly StackPanel _toolPanel = new();
    private readonly StackPanel _editingPanel = new();
    private readonly Dictionary<SelectionHandle, Border> _handles = [];
    private readonly Dictionary<EditorTool, Button> _toolButtons = [];

    private ImageBrush? _previewBrush;
    private CaptureSession? _session;
    private EditorSurface? _editor;
    private SaveLocationHistory? _saveLocations;

    private Point _pointerStart;
    private Rect _startRectDip;
    private Rect _workingRectDip;
    private Int32Rect _adjustBeforePixels;
    private SelectionHandle _activeHandle;
    private bool _selectionDragging;
    private double _scaleX = 1;
    private double _scaleY = 1;

    private Button? _saveMainButton;
    private Button? _ratioButton;
    private TextBlock? _ratioLabel;
    private Ellipse? _colorIndicator;
    private double? _lockedAspectRatio;
    private string _aspectRatioLabel = "自由";
    private bool _customAspectMode;
    private Brush _strokeBrush = Brushes.OrangeRed;
    private readonly Brush[] _strokePalette =
    [
        Brushes.OrangeRed,
        Brushes.DeepSkyBlue,
        Brushes.Gold,
        Brushes.White,
        Brushes.Black
    ];
    private int _strokeIndex;
    private readonly double[] _thicknesses = [2, 5, 9];
    private int _thicknessIndex = 1;

    public CaptureWorkspaceWindow(
        BitmapSource screen,
        string extensionDataDirectory)
    {
        _screen = screen;
        _extensionDataDirectory = extensionDataDirectory;

        Title = "燕子截图";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Left = 0;
        Top = 0;
        SizeToContent = SizeToContent.Manual;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Focusable = true;

        BuildVisualTree();

        Loaded += (_, _) =>
        {
            UpdateScale();
            _saveLocations = new SaveLocationHistory(
                _extensionDataDirectory);
            UpdateSaveButtonLabel();
            Activate();
            Focus();
        };

        SizeChanged += (_, _) => UpdateScale();

        PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseUp;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void BuildVisualTree()
    {
        var root = new Grid();

        _screenImage.Source = _screen;
        _screenImage.Stretch = Stretch.Fill;
        root.Children.Add(_screenImage);

        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(
                Color.FromArgb(128, 0, 0, 0)),
            IsHitTestVisible = false
        });

        _previewBrush = new ImageBrush(_screen)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute
        };
        _selectionPreview.Fill = _previewBrush;
        _selectionPreview.Visibility = Visibility.Collapsed;
        _selectionPreview.IsHitTestVisible = false;
        Panel.SetZIndex(_selectionPreview, 12);
        _canvas.Children.Add(_selectionPreview);

        _selectionBorder.Fill = Brushes.Transparent;
        _selectionBorder.Stroke = Brush("#2F89FF");
        _selectionBorder.StrokeThickness = 2;
        _selectionBorder.Visibility = Visibility.Collapsed;
        _selectionBorder.IsHitTestVisible = false;
        Panel.SetZIndex(_selectionBorder, 20);
        _canvas.Children.Add(_selectionBorder);

        foreach (var handle in new[]
        {
            SelectionHandle.NorthWest,
            SelectionHandle.North,
            SelectionHandle.NorthEast,
            SelectionHandle.West,
            SelectionHandle.East,
            SelectionHandle.SouthWest,
            SelectionHandle.South,
            SelectionHandle.SouthEast
        })
        {
            var visual = new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.White,
                BorderBrush = Brush("#2F89FF"),
                BorderThickness = new Thickness(2),
                Tag = handle,
                Visibility = Visibility.Collapsed,
                Cursor = CursorForHandle(handle)
            };
            AutomationProperties.SetAutomationId(
                visual,
                "capture.workspace.handle." +
                handle.ToString().ToLowerInvariant());
            Panel.SetZIndex(visual, 30);
            _handles[handle] = visual;
            _canvas.Children.Add(visual);
        }

        _sizeBadge.Foreground = Brush("#F2FFFFFF");
        _sizeBadge.FontFamily = new FontFamily("Segoe UI");
        _sizeBadge.FontSize = 10.5;
        _sizeBadge.FontWeight = FontWeights.Medium;
        _sizeBadge.VerticalAlignment = VerticalAlignment.Center;

        _sizeBadgeShell.Background = Brush("#E8161A20");
        _sizeBadgeShell.BorderBrush = Brush("#363D48");
        _sizeBadgeShell.BorderThickness = new Thickness(1);
        _sizeBadgeShell.CornerRadius = new CornerRadius(6);
        _sizeBadgeShell.Padding = new Thickness(8, 4, 8, 4);
        _sizeBadgeShell.Child = _sizeBadge;
        _sizeBadgeShell.Visibility = Visibility.Collapsed;
        _sizeBadgeShell.IsHitTestVisible = false;
        _sizeBadgeShell.Effect = new DropShadowEffect
        {
            BlurRadius = 8,
            ShadowDepth = 1,
            Opacity = 0.28,
            Color = Colors.Black
        };
        Panel.SetZIndex(_sizeBadgeShell, 35);
        _canvas.Children.Add(_sizeBadgeShell);

        _hint.Text =
            "拖动选择截图区域  ·  松开后仍可调整  ·  Esc 退出";
        _hint.Foreground = Brushes.White;
        _hint.Background = Brush("#D914161B");
        _hint.Padding = new Thickness(14, 8, 14, 8);
        _hint.FontSize = 13;
        _hint.IsHitTestVisible = false;
        Canvas.SetLeft(_hint, 24);
        Canvas.SetTop(_hint, 24);
        Panel.SetZIndex(_hint, 50);
        _canvas.Children.Add(_hint);

        BuildToolbar();
        Panel.SetZIndex(_toolbar, 45);
        _canvas.Children.Add(_toolbar);

        BuildRatioControl();
        Panel.SetZIndex(_ratioControl, 46);
        _canvas.Children.Add(_ratioControl);

        BuildRatioFlyout();
        Panel.SetZIndex(_ratioFlyout, 47);
        _canvas.Children.Add(_ratioFlyout);

        root.Children.Add(_canvas);
        Content = root;
    }

    private void BuildToolbar()
    {
        _toolbar.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(
            _toolbar,
            "capture.workspace.toolbar");
        _toolPanel.Orientation = Orientation.Horizontal;
        _toolPanel.VerticalAlignment = VerticalAlignment.Center;
        _toolbar.Background = Brush("#F212151A");
        _toolbar.BorderBrush = Brush("#343A44");
        _toolbar.BorderThickness = new Thickness(1);
        _toolbar.CornerRadius = new CornerRadius(8);
        _toolbar.Padding = new Thickness(5, 3, 5, 3);
        _toolbar.Effect = new DropShadowEffect
        {
            BlurRadius = 12,
            ShadowDepth = 2,
            Opacity = 0.32,
            Color = Colors.Black
        };

        var dock = new DockPanel
        {
            LastChildFill = true
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(actions, Dock.Right);

        actions.Children.Add(
            ActionButton(
                "ocr",
                "文字识别",
                "capture.workspace.ocr",
                async () => await RunOcrAsync()));

        actions.Children.Add(
            ActionButton(
                "pin",
                "贴图",
                "capture.workspace.pin",
                PinImage));

        actions.Children.Add(
            ActionButton(
                "copy",
                "复制 (Ctrl+C)",
                "capture.workspace.copy",
                CopyImage));

        actions.Children.Add(ToolbarSpacer(10));

        var closeButton =
            ActionButton(
                "close",
                "退出 (Esc)",
                "capture.workspace.close",
                Close);
        actions.Children.Add(closeButton);

        actions.Children.Add(ToolbarSpacer(12));

        var saveSplit = CreateSaveSplitButton();
        actions.Children.Add(saveSplit);

        dock.Children.Add(actions);

        AddTool(
            EditorTool.Select,
            "select",
            "选择 / 调整截图区域",
            "capture.workspace.select",
            _toolPanel);

        _editingPanel.Orientation = Orientation.Horizontal;
        _editingPanel.VerticalAlignment = VerticalAlignment.Center;
        _editingPanel.Visibility = Visibility.Visible;
        _editingPanel.Margin = new Thickness(2, 0, 0, 0);

        AddTool(
            EditorTool.Rectangle,
            "rectangle",
            "矩形",
            "capture.workspace.rectangle",
            _editingPanel);
        AddTool(
            EditorTool.Arrow,
            "arrow",
            "箭头",
            "capture.workspace.arrow",
            _editingPanel);
        AddTool(
            EditorTool.Pen,
            "pen",
            "画笔",
            "capture.workspace.pen",
            _editingPanel);
        AddTool(
            EditorTool.Text,
            "text",
            "文字",
            "capture.workspace.text",
            _editingPanel);
        AddTool(
            EditorTool.Mosaic,
            "mosaic",
            "马赛克",
            "capture.workspace.mosaic",
            _editingPanel);

        _editingPanel.Children.Add(Separator());

        _editingPanel.Children.Add(
            ActionButton(
                "image",
                "添加图片",
                "capture.workspace.image",
                AddImage));

        _editingPanel.Children.Add(
            ActionButton(
                "undo",
                "撤销 (Ctrl+Z)",
                "capture.workspace.undo",
                () => _session?.Document.History.Undo()));

        _editingPanel.Children.Add(
            ActionButton(
                "redo",
                "重做 (Ctrl+Y)",
                "capture.workspace.redo",
                () => _session?.Document.History.Redo()));

        var colorButton = ActionButton(
            "color",
            "切换标注颜色",
            "capture.workspace.color",
            CycleColor);
        colorButton.Tag = "color";
        _editingPanel.Children.Add(colorButton);

        _editingPanel.Children.Add(
            ActionButton(
                "thickness",
                "切换粗细",
                "capture.workspace.thickness",
                CycleThickness));

        _toolPanel.Children.Add(_editingPanel);

        dock.Children.Add(_toolPanel);
        _toolbar.Child = dock;
    }

    private void BuildRatioControl()
    {
        _ratioControl.Visibility = Visibility.Collapsed;
        _ratioControl.Background = Brush("#F212151A");
        _ratioControl.BorderBrush = Brush("#343A44");
        _ratioControl.BorderThickness = new Thickness(1);
        _ratioControl.CornerRadius = new CornerRadius(7);
        _ratioControl.Padding = new Thickness(2);
        _ratioControl.Effect = new DropShadowEffect
        {
            BlurRadius = 10,
            ShadowDepth = 2,
            Opacity = 0.28,
            Color = Colors.Black
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        content.Children.Add(
            CreateVectorIcon(
                "ratio",
                Brush("#E6FFFFFF"),
                14));

        _ratioLabel = new TextBlock
        {
            Text = _aspectRatioLabel,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(_ratioLabel);

        content.Children.Add(
            CreateVectorIcon(
                "chevron-down",
                Brush("#C8FFFFFF"),
                9));

        _ratioButton = FlatButton(
            content,
            "capture.workspace.ratio",
            primary: false);
        _ratioButton.Height = 28;
        _ratioButton.Padding = new Thickness(8, 0, 8, 0);
        _ratioButton.ToolTip = "截图比例";
        _ratioButton.Click += (_, _) =>
            ToggleRatioFlyout();

        _ratioControl.PreviewMouseLeftButtonDown += (_, e) =>
        {
            ToggleRatioFlyout();
            e.Handled = true;
        };

        _ratioControl.Child = _ratioButton;
    }

    private void BuildRatioFlyout()
    {
        _ratioFlyout.Visibility = Visibility.Collapsed;
        _ratioFlyout.Background = Brush("#F5171B21");
        _ratioFlyout.BorderBrush = Brush("#343A44");
        _ratioFlyout.BorderThickness = new Thickness(1);
        _ratioFlyout.CornerRadius = new CornerRadius(8);
        _ratioFlyout.Padding = new Thickness(8);
        _ratioFlyout.Effect = new DropShadowEffect
        {
            BlurRadius = 12,
            ShadowDepth = 2,
            Opacity = 0.34,
            Color = Colors.Black
        };

        var root = new StackPanel
        {
            Width = 176
        };

        root.Children.Add(
            new TextBlock
            {
                Text = "截图比例",
                Foreground = Brush("#AFFFFFFF"),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 10.5,
                Margin = new Thickness(4, 2, 4, 6)
            });

        root.Children.Add(
            CreateRatioChoiceButton(
                "自由",
                null,
                "自由",
                "capture.workspace.ratio.free"));
        root.Children.Add(
            CreateRatioChoiceButton(
                "1 : 1",
                1d,
                "1:1",
                "capture.workspace.ratio.1x1"));
        root.Children.Add(
            CreateRatioChoiceButton(
                "3 : 2",
                3d / 2d,
                "3:2",
                "capture.workspace.ratio.3x2"));
        root.Children.Add(
            CreateRatioChoiceButton(
                "16 : 9",
                16d / 9d,
                "16:9",
                "capture.workspace.ratio.16x9"));
        root.Children.Add(
            CreateRatioChoiceButton(
                "9 : 16",
                9d / 16d,
                "9:16",
                "capture.workspace.ratio.9x16"));

        root.Children.Add(
            new Border
            {
                Height = 1,
                Background = Brush("#303640"),
                Margin = new Thickness(4, 7, 4, 8)
            });

        root.Children.Add(
            new TextBlock
            {
                Text = "自定义尺寸",
                Foreground = Brush("#DFFFFFFF"),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 10.5,
                Margin = new Thickness(4, 0, 4, 6)
            });

        var customRow = new Grid
        {
            Margin = new Thickness(4, 0, 4, 4)
        };
        customRow.ColumnDefinitions.Add(
            new ColumnDefinition());
        customRow.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(20) });
        customRow.ColumnDefinitions.Add(
            new ColumnDefinition());

        ConfigureInlinePixelBox(
            _ratioWidthBox,
            "capture.workspace.ratio.width");
        ConfigureInlinePixelBox(
            _ratioHeightBox,
            "capture.workspace.ratio.height");

        customRow.Children.Add(_ratioWidthBox);

        var times = new TextBlock
        {
            Text = "×",
            Foreground = Brush("#BFFFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        };
        Grid.SetColumn(times, 1);
        customRow.Children.Add(times);

        Grid.SetColumn(_ratioHeightBox, 2);
        customRow.Children.Add(_ratioHeightBox);
        root.Children.Add(customRow);

        var hint = new TextBlock
        {
            Text = "px",
            Foreground = Brush("#7FFFFFFF"),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9.5,
            Margin = new Thickness(4, 0, 4, 5)
        };
        root.Children.Add(hint);

        var apply = new Button
        {
            Content = "应用",
            Height = 28,
            Margin = new Thickness(4, 0, 4, 2),
            Foreground = Brushes.White,
            Background = Brush("#1976F3"),
            BorderBrush = Brush("#398AF5"),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 10.5,
            Cursor = Cursors.Hand,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(
            apply,
            "capture.workspace.ratio.apply");
        apply.Click += (_, _) =>
            ApplyInlineCustomPixelSize();

        root.Children.Add(apply);
        _ratioFlyout.Child = root;
    }

    private Button CreateRatioChoiceButton(
        string text,
        double? ratio,
        string label,
        string automationId)
    {
        var button = new Button
        {
            Content = text,
            Height = 30,
            Margin = new Thickness(2, 1, 2, 1),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 0, 8, 0),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Cursor = Cursors.Hand,
            Focusable = false
        };

        AutomationProperties.SetAutomationId(
            button,
            automationId);

        button.MouseEnter += (_, _) =>
            button.Background = Brush("#2A3038");
        button.MouseLeave += (_, _) =>
            button.Background = Brushes.Transparent;

        button.Click += (_, _) =>
        {
            ApplyAspectRatio(ratio, label);
            _ratioFlyout.Visibility = Visibility.Collapsed;
        };

        return button;
    }

    private static void ConfigureInlinePixelBox(
        TextBox box,
        string automationId)
    {
        box.Height = 30;
        box.Foreground = Brushes.White;
        box.Background = Brush("#20252C");
        box.BorderBrush = Brush("#3B4552");
        box.BorderThickness = new Thickness(1);
        box.Padding = new Thickness(7, 4, 7, 4);
        box.FontFamily = new FontFamily("Segoe UI");
        box.FontSize = 10.5;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(
            box,
            automationId);
    }

    private void ToggleRatioFlyout()
    {
        var opening =
            _ratioFlyout.Visibility != Visibility.Visible;

        if (!opening)
        {
            _ratioFlyout.Visibility = Visibility.Collapsed;
            return;
        }

        if (_session is not null)
        {
            _ratioWidthBox.Text =
                _session.SelectionPixels.Width.ToString();
            _ratioHeightBox.Text =
                _session.SelectionPixels.Height.ToString();
        }

        _ratioFlyout.Visibility = Visibility.Visible;

        if (_session is not null)
            PositionRatioFlyout(CurrentSelectionDipRect());
    }

    private void ApplyAspectRatio(
        double? ratio,
        string label)
    {
        _lockedAspectRatio = ratio;
        _aspectRatioLabel = label;
        _customAspectMode = false;
        UpdateRatioLabel();

        if (_session is null ||
            ratio is null)
        {
            return;
        }

        var before = _session.SelectionPixels;
        var next = CreateRatioAdjustedPixels(
            before,
            ratio.Value);

        _session.SetSelection(next);

        if (!SameRect(before, next))
        {
            _session.Document.History.RecordExecuted(
                new ChangeSelectionCommand(
                    _session,
                    before,
                    next));
        }

        UpdateSessionLayout();
    }

    private void ApplyInlineCustomPixelSize()
    {
        if (_session is null)
            return;

        if (!int.TryParse(
                _ratioWidthBox.Text,
                out var requestedWidth) ||
            !int.TryParse(
                _ratioHeightBox.Text,
                out var requestedHeight) ||
            requestedWidth < 1 ||
            requestedHeight < 1)
        {
            _ratioWidthBox.BorderBrush =
                Brush("#C85A67");
            _ratioHeightBox.BorderBrush =
                Brush("#C85A67");
            return;
        }

        var current = _session.SelectionPixels;

        var width = Math.Clamp(
            requestedWidth,
            1,
            _screen.PixelWidth);
        var height = Math.Clamp(
            requestedHeight,
            1,
            _screen.PixelHeight);

        _ratioWidthBox.BorderBrush =
            Brush("#3B4552");
        _ratioHeightBox.BorderBrush =
            Brush("#3B4552");

        var x = Math.Clamp(
            current.X,
            0,
            Math.Max(
                0,
                _screen.PixelWidth - width));
        var y = Math.Clamp(
            current.Y,
            0,
            Math.Max(
                0,
                _screen.PixelHeight - height));

        var next = new Int32Rect(
            x,
            y,
            width,
            height);

        _lockedAspectRatio =
            width / (double)Math.Max(1, height);
        _aspectRatioLabel =
            $"{width}×{height}";
        _customAspectMode = true;
        UpdateRatioLabel();

        var before = _session.SelectionPixels;
        _session.SetSelection(next);

        if (!SameRect(before, next))
        {
            _session.Document.History.RecordExecuted(
                new ChangeSelectionCommand(
                    _session,
                    before,
                    next));
        }

        _ratioFlyout.Visibility =
            Visibility.Collapsed;
        UpdateSessionLayout();
    }

    private void UpdateRatioLabel()
    {
        if (_ratioLabel is not null)
            _ratioLabel.Text = _aspectRatioLabel;
    }

    private Int32Rect CreateRatioAdjustedPixels(
        Int32Rect current,
        double ratio)
    {
        var width = current.Width;
        var height = Math.Max(
            1,
            (int)Math.Round(width / ratio));

        var availableWidth =
            _screen.PixelWidth - current.X;
        var availableHeight =
            _screen.PixelHeight - current.Y;

        if (height > availableHeight)
        {
            height = Math.Max(1, availableHeight);
            width = Math.Max(
                1,
                (int)Math.Round(height * ratio));
        }

        if (width > availableWidth)
        {
            width = Math.Max(1, availableWidth);
            height = Math.Max(
                1,
                (int)Math.Round(width / ratio));
        }

        return new Int32Rect(
            current.X,
            current.Y,
            width,
            height);
    }

    private Border CreateSaveSplitButton()
    {
        var shell = new Border
        {
            Background = Brush("#1976F3"),
            CornerRadius = new CornerRadius(7),
            Height = 32
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(36) });

        var arrow = FlatButton(
            CreateVectorIcon(
                "chevron-down",
                Brushes.White,
                10),
            "capture.workspace.save-menu",
            primary: true);
        arrow.ToolTip = "保存位置";
        arrow.BorderThickness = new Thickness(0, 0, 1, 0);
        arrow.BorderBrush = Brush("#55FFFFFF");
        arrow.Click += (_, _) =>
        {
            arrow.ContextMenu = BuildSaveMenu();
            arrow.ContextMenu.PlacementTarget = arrow;
            arrow.ContextMenu.IsOpen = true;
        };
        grid.Children.Add(arrow);

        _saveMainButton = FlatButton(
            CreateVectorIcon("save", Brushes.White, 16),
            "capture.workspace.save",
            primary: true);
        _saveMainButton.ToolTip = "保存 (Ctrl+S)";
        _saveMainButton.Click += (_, _) => SaveToCurrent();
        Grid.SetColumn(_saveMainButton, 1);
        grid.Children.Add(_saveMainButton);

        shell.Child = grid;
        return shell;
    }

    private ContextMenu BuildSaveMenu()
    {
        var menu = new ContextMenu
        {
            Background = Brush("#171B21"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#343A44"),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            Padding = new Thickness(4),
            HasDropShadow = true
        };

        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(
            new Setter(
                Control.ForegroundProperty,
                Brush("#F0FFFFFF")));
        itemStyle.Setters.Add(
            new Setter(
                Control.BackgroundProperty,
                Brushes.Transparent));
        itemStyle.Setters.Add(
            new Setter(
                Control.PaddingProperty,
                new Thickness(10, 7, 14, 7)));
        itemStyle.Setters.Add(
            new Setter(
                Control.BorderThicknessProperty,
                new Thickness(0)));
        itemStyle.Setters.Add(
            new Setter(
                FrameworkElement.MinHeightProperty,
                30d));

        var highlighted = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        highlighted.Setters.Add(
            new Setter(
                Control.BackgroundProperty,
                Brush("#2A3038")));
        itemStyle.Triggers.Add(highlighted);

        menu.Resources[typeof(MenuItem)] = itemStyle;

        var saveAs = new MenuItem
        {
            Header = "另存为… (Ctrl+Shift+S)"
        };
        saveAs.Click += (_, _) => SaveAs();
        menu.Items.Add(saveAs);

        if (_saveLocations is not null &&
            _saveLocations.RecentDirectories.Count > 0)
        {
            menu.Items.Add(new Separator());

            var header = new MenuItem
            {
                Header = "最近保存位置",
                IsEnabled = false
            };
            menu.Items.Add(header);

            foreach (var directory in
                     _saveLocations.RecentDirectories)
            {
                var capturedDirectory = directory;
                var item = new MenuItem
                {
                    Header = DisplayDirectory(directory),
                    ToolTip = directory,
                    IsCheckable = true,
                    IsChecked = string.Equals(
                        directory,
                        _saveLocations.CurrentDirectory,
                        StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (_, _) =>
                    SaveToDirectory(capturedDirectory);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());

        var open = new MenuItem
        {
            Header = "打开当前保存目录"
        };
        open.Click += (_, _) => OpenCurrentDirectory();
        menu.Items.Add(open);

        return menu;
    }

    private void AddTool(
        EditorTool tool,
        string icon,
        string tooltip,
        string automationId,
        Panel? targetPanel = null)
    {
        var button = ActionButton(
            icon,
            tooltip,
            automationId,
            () => SelectTool(tool));

        _toolButtons[tool] = button;
        (targetPanel ?? _toolPanel).Children.Add(button);
    }

    private Button ActionButton(
        string icon,
        string tooltip,
        string automationId,
        Action action)
    {
        var iconElement =
            CreateVectorIcon(
                icon,
                Brushes.White,
                17);

        var button = FlatButton(
            iconElement,
            automationId,
            primary: false);

        button.Width = 32;
        button.Height = 32;
        button.Margin = new Thickness(1, 0, 1, 0);
        button.ToolTip = tooltip;
        button.Click += (_, _) => action();

        if (icon == "color" &&
            iconElement is Ellipse ellipse)
        {
            _colorIndicator = ellipse;
        }

        return button;
    }

    private Button FlatButton(
        object content,
        string automationId,
        bool primary)
    {
        var button = new Button
        {
            Content = content,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            Focusable = false,
            Padding = new Thickness(0)
        };

        AutomationProperties.SetAutomationId(
            button,
            automationId);

        var border =
            new FrameworkElementFactory(
                typeof(Border));
        border.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(6));
        border.SetBinding(
            Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });
        border.SetBinding(
            Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });

        var presenter =
            new FrameworkElementFactory(
                typeof(ContentPresenter));
        presenter.SetBinding(
            ContentPresenter.HorizontalAlignmentProperty,
            new System.Windows.Data.Binding("HorizontalContentAlignment")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });
        presenter.SetBinding(
            ContentPresenter.VerticalAlignmentProperty,
            new System.Windows.Data.Binding("VerticalContentAlignment")
            {
                RelativeSource =
                    System.Windows.Data.RelativeSource.TemplatedParent
            });
        presenter.SetValue(
            ContentPresenter.RecognizesAccessKeyProperty,
            true);
        border.AppendChild(presenter);

        button.Template =
            new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };

        button.MouseEnter += (_, _) =>
        {
            if (primary)
            {
                button.Background = Brush("#2C83F5");
                return;
            }

            if (!IsSelectedToolButton(button))
                button.Background = Brush("#2A3038");
        };

        button.MouseLeave += (_, _) =>
        {
            if (primary)
            {
                button.Background = Brushes.Transparent;
                return;
            }

            ApplyToolButtonVisual(button);
        };

        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            button.Background =
                primary
                    ? Brush("#1165D5")
                    : Brush("#38424F");
        };

        button.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (primary)
            {
                button.Background = Brush("#2C83F5");
            }
            else
            {
                ApplyToolButtonVisual(button);
            }
        };

        return button;
    }

    private bool IsSelectedToolButton(Button button)
    {
        if (_editor is null)
            return false;

        foreach (var pair in _toolButtons)
        {
            if (ReferenceEquals(pair.Value, button))
                return pair.Key == _editor.Tool;
        }

        return false;
    }

    private void ApplyToolButtonVisual(Button button)
    {
        if (IsSelectedToolButton(button))
        {
            button.Background = Brush("#246FCB");
            button.BorderBrush = Brush("#4A95F5");
            button.BorderThickness = new Thickness(1);
        }
        else
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(1);
        }
    }

    private FrameworkElement CreateVectorIcon(
        string icon,
        Brush foreground,
        double size)
    {
        if (icon == "color")
        {
            return new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = _strokeBrush,
                Stroke = Brush("#E6FFFFFF"),
                StrokeThickness = 1.2
            };
        }

        if (icon == "thickness")
        {
            var canvas = new Canvas
            {
                Width = size,
                Height = size
            };

            var lines = new[]
            {
                (Y: 4d, Thickness: 1d, Width: 11d),
                (Y: 8.5d, Thickness: 2d, Width: 14d),
                (Y: 14d, Thickness: 3.2d, Width: 17d)
            };

            foreach (var item in lines)
            {
                var line = new Line
                {
                    X1 = 0,
                    X2 = item.Width,
                    Y1 = item.Y,
                    Y2 = item.Y,
                    Stroke = foreground,
                    StrokeThickness = item.Thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                canvas.Children.Add(line);
            }

            return canvas;
        }

        var data = icon switch
        {
            "select" =>
                "M3,2 L3,20 L8,15 L12,23 L15,21 L11,14 L20,14 Z",
            "ratio" =>
                "M4,6 H20 V18 H4 Z M9,6 V18 M4,12 H20",
            "rectangle" =>
                "M4,5 H20 V19 H4 Z",
            "arrow" =>
                "M4,20 L20,4 M11,4 H20 V13",
            "pen" =>
                "M4,19 L6,14 L16,4 L20,8 L10,18 Z M14,6 L18,10",
            "text" =>
                "M5,5 H19 M12,5 V20 M8,20 H16",
            "mosaic" =>
                "M3,3 H9 V9 H3 Z M13,3 H21 V11 H13 Z " +
                "M3,13 H11 V21 H3 Z M15,15 H21 V21 H15 Z",
            "image" =>
                "M3,4 H21 V20 H3 Z M5,17 L10,11 L14,15 L17,12 L20,17 M8,8 H8.2",
            "ocr" =>
                "M4,8 V4 H8 M16,4 H20 V8 M20,16 V20 H16 M8,20 H4 V16 " +
                "M8,9 H16 M8,12 H14 M8,15 H16",
            "pin" =>
                "M8,4 H16 L15,9 L19,13 H13 L12,21 L11,13 H5 L9,9 Z",
            "undo" =>
                "M9,7 L4,12 L9,17 M5,12 H14 C19,12 21,15 21,19",
            "redo" =>
                "M15,7 L20,12 L15,17 M19,12 H10 C5,12 3,15 3,19",
            "copy" =>
                "M8,7 H20 V20 H8 Z M4,3 H16 V7 M4,3 V16 H8",
            "close" =>
                "M5,5 L19,19 M19,5 L5,19",
            "save" =>
                "M12,3 V15 M7,10 L12,15 L17,10 M5,20 H19",
            "chevron-down" =>
                "M5,8 L12,15 L19,8",
            _ =>
                "M4,4 H20 V20 H4 Z"
        };

        var filled =
            icon is "select" or "mosaic";

        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = foreground,
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill =
                filled
                    ? foreground
                    : Brushes.Transparent,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 20,
        Background = Brush("#303640"),
        Margin = new Thickness(4, 6, 4, 6)
    };

    private static FrameworkElement ToolbarSpacer(double width) =>
        new Border
        {
            Width = width,
            Background = Brushes.Transparent
        };

    private void OnPreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var original =
            e.OriginalSource as DependencyObject;

        if (IsDescendantOf(_toolbar, original) ||
            IsDescendantOf(_ratioControl, original) ||
            IsDescendantOf(_ratioFlyout, original))
        {
            return;
        }

        if (_ratioFlyout.Visibility == Visibility.Visible)
            _ratioFlyout.Visibility = Visibility.Collapsed;

        var point = e.GetPosition(_canvas);

        if (e.ClickCount == 2 && _session is not null)
        {
            var selection = CurrentSelectionDipRect();
            if (selection.Contains(point))
            {
                CopyImage();
                e.Handled = true;
                return;
            }
        }

        if (_session is null)
        {
            BeginSelection(
                SelectionHandle.Create,
                point,
                new Rect(point, point));
            e.Handled = true;
            return;
        }

        var taggedHandle =
            FindTaggedHandle(original);

        if (taggedHandle != SelectionHandle.None)
        {
            BeginSelection(
                taggedHandle,
                point,
                CurrentSelectionDipRect());
            e.Handled = true;
            return;
        }

        if (_editor is not null &&
            _editor.Tool == EditorTool.Select)
        {
            var selection = CurrentSelectionDipRect();

            if (selection.Contains(point))
            {
                var local = e.GetPosition(_editor);

                if (!_editor.HasAnnotationAtViewPoint(local))
                {
                    BeginSelection(
                        SelectionHandle.Move,
                        point,
                        selection);
                    e.Handled = true;
                    return;
                }
            }
            else
            {
                BeginSelection(
                    SelectionHandle.Create,
                    point,
                    new Rect(point, point));
                e.Handled = true;
            }
        }
    }

    private void OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_selectionDragging)
            return;

        var point = ClampDip(
            e.GetPosition(_canvas));

        _workingRectDip =
            CalculateAdjustedRect(
                _activeHandle,
                _startRectDip,
                _pointerStart,
                point);

        ShowSelectionPreview(_workingRectDip);
        e.Handled = true;
    }

    private void OnPreviewMouseUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_selectionDragging ||
            e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _selectionDragging = false;
        Mouse.Capture(null);

        var finalDip = ClampRect(
            Normalize(_workingRectDip));

        if (finalDip.Width < 8 ||
            finalDip.Height < 8)
        {
            if (_session is null)
            {
                _selectionPreview.Visibility =
                    Visibility.Collapsed;
                _selectionBorder.Visibility =
                    Visibility.Collapsed;
                _sizeBadgeShell.Visibility =
                    Visibility.Collapsed;
            }
            else
            {
                RestoreSessionVisuals();
            }

            return;
        }

        var pixels = DipToPixels(finalDip);

        if (_session is null)
        {
            CreateSession(pixels);
        }
        else
        {
            var before = _adjustBeforePixels;

            _session.SetSelection(pixels);

            if (!SameRect(before, pixels))
            {
                _session.Document.History.RecordExecuted(
                    new ChangeSelectionCommand(
                        _session,
                        before,
                        pixels));
            }

            RestoreSessionVisuals();
        }

        e.Handled = true;
    }

    private void BeginSelection(
        SelectionHandle handle,
        Point pointer,
        Rect startingRect)
    {
        _activeHandle = handle;
        _pointerStart = pointer;
        _startRectDip = startingRect;
        _workingRectDip = startingRect;
        _selectionDragging = true;

        if (_session is not null)
        {
            _adjustBeforePixels =
                _session.SelectionPixels;

            if (_editor is not null)
                _editor.Visibility =
                    Visibility.Hidden;
        }

        _toolbar.Visibility =
            Visibility.Collapsed;
        _ratioControl.Visibility =
            Visibility.Collapsed;
        _ratioFlyout.Visibility =
            Visibility.Collapsed;

        _selectionPreview.Visibility =
            Visibility.Visible;
        _selectionBorder.Visibility =
            Visibility.Visible;

        Mouse.Capture(this);

        if (handle == SelectionHandle.Create)
        {
            _workingRectDip =
                new Rect(pointer, pointer);
        }

        ShowSelectionPreview(_workingRectDip);
    }

    private void CreateSession(Int32Rect pixels)
    {
        _session = new CaptureSession(
            _screen,
            pixels);

        _editor = new EditorSurface(
            _session.Document)
        {
            WorkspaceMode = true,
            ClipToBounds = true,
            Tool = EditorTool.Select,
            Cursor = Cursors.Arrow,
            Focusable = true
        };

        _editor.TextFactory =
            CreateTextAnnotation;
        _editor.StatusChanged =
            _ => { };

        AutomationProperties.SetAutomationId(
            _editor,
            "capture.workspace.surface");

        Panel.SetZIndex(_editor, 10);
        _canvas.Children.Add(_editor);

        _session.SelectionChanged +=
            UpdateSessionLayout;

        _hint.Visibility =
            Visibility.Collapsed;

        _selectionPreview.Visibility =
            Visibility.Collapsed;

        SelectTool(EditorTool.Select);
        UpdateSessionLayout();

        _editor.Focus();
    }

    private void RestoreSessionVisuals()
    {
        _selectionPreview.Visibility =
            Visibility.Collapsed;

        if (_editor is not null)
            _editor.Visibility =
                Visibility.Visible;

        UpdateSessionLayout();
    }

    private void UpdateSessionLayout()
    {
        if (_session is null ||
            _editor is null)
        {
            return;
        }

        if (_customAspectMode)
        {
            _aspectRatioLabel =
                $"{_session.SelectionPixels.Width}×{_session.SelectionPixels.Height}";
            UpdateRatioLabel();
        }

        var rect =
            PixelsToDip(
                _session.SelectionPixels);

        Canvas.SetLeft(_editor, rect.X);
        Canvas.SetTop(_editor, rect.Y);
        _editor.Width = rect.Width;
        _editor.Height = rect.Height;
        _editor.Visibility = Visibility.Visible;
        _editor.RefreshSurface();

        SetElementRect(
            _selectionBorder,
            rect);
        _selectionBorder.Visibility =
            Visibility.Visible;

        PositionHandles(rect);
        UpdateSizeBadge(
            rect,
            _session.SelectionPixels);

        _toolbar.Visibility =
            Visibility.Visible;
        PositionToolbar(rect);

        _ratioControl.Visibility =
            Visibility.Visible;
        PositionRatioControl(rect);
    }

    private void ShowSelectionPreview(Rect rect)
    {
        rect = ClampRect(
            Normalize(rect));

        SetElementRect(
            _selectionPreview,
            rect);
        SetElementRect(
            _selectionBorder,
            rect);

        if (_previewBrush is not null)
        {
            _previewBrush.Viewbox =
                new Rect(
                    rect.X * _scaleX,
                    rect.Y * _scaleY,
                    Math.Max(
                        1,
                        rect.Width * _scaleX),
                    Math.Max(
                        1,
                        rect.Height * _scaleY));
        }

        PositionHandles(rect);

        UpdateSizeBadge(
            rect,
            DipToPixels(rect));
    }

    private void PositionHandles(Rect rect)
    {
        if (rect.Width < 1 ||
            rect.Height < 1)
        {
            foreach (var handle in _handles.Values)
                handle.Visibility =
                    Visibility.Collapsed;
            return;
        }

        PositionHandle(
            SelectionHandle.NorthWest,
            rect.Left,
            rect.Top);
        PositionHandle(
            SelectionHandle.North,
            rect.Left + rect.Width / 2,
            rect.Top);
        PositionHandle(
            SelectionHandle.NorthEast,
            rect.Right,
            rect.Top);
        PositionHandle(
            SelectionHandle.West,
            rect.Left,
            rect.Top + rect.Height / 2);
        PositionHandle(
            SelectionHandle.East,
            rect.Right,
            rect.Top + rect.Height / 2);
        PositionHandle(
            SelectionHandle.SouthWest,
            rect.Left,
            rect.Bottom);
        PositionHandle(
            SelectionHandle.South,
            rect.Left + rect.Width / 2,
            rect.Bottom);
        PositionHandle(
            SelectionHandle.SouthEast,
            rect.Right,
            rect.Bottom);
    }

    private void PositionHandle(
        SelectionHandle handle,
        double x,
        double y)
    {
        var element = _handles[handle];
        element.Visibility =
            Visibility.Visible;
        Canvas.SetLeft(
            element,
            x - element.Width / 2);
        Canvas.SetTop(
            element,
            y - element.Height / 2);
    }

    private void UpdateSizeBadge(
        Rect dipRect,
        Int32Rect pixels)
    {
        _sizeBadge.Text =
            $"{pixels.X},{pixels.Y}   " +
            $"{pixels.Width} × {pixels.Height}";
        _sizeBadgeShell.Visibility =
            Visibility.Visible;
        _sizeBadgeShell.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        var top =
            dipRect.Top -
            _sizeBadgeShell.DesiredSize.Height -
            7;

        if (top < 8)
            top = dipRect.Top + 7;

        Canvas.SetLeft(
            _sizeBadgeShell,
            Math.Clamp(
                dipRect.Left,
                8,
                Math.Max(
                    8,
                    ActualWidth -
                    _sizeBadgeShell.DesiredSize.Width -
                    8)));
        Canvas.SetTop(_sizeBadgeShell, top);
    }

    private void PositionToolbar(Rect selection)
    {
        _toolbar.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        var size = _toolbar.DesiredSize;

        var x = Math.Clamp(
            selection.Right - size.Width,
            8,
            Math.Max(
                8,
                ActualWidth - size.Width - 8));

        var below =
            selection.Bottom + 8;

        var y =
            below + size.Height <=
            ActualHeight - 8
                ? below
                : selection.Top -
                  size.Height -
                  8;

        y = Math.Clamp(
            y,
            8,
            Math.Max(
                8,
                ActualHeight - size.Height - 8));

        Canvas.SetLeft(_toolbar, x);
        Canvas.SetTop(_toolbar, y);
    }

    private void PositionRatioControl(Rect selection)
    {
        _ratioControl.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        var size = _ratioControl.DesiredSize;

        var outsideX =
            selection.Right + 8;
        var canPlaceOutside =
            outsideX + size.Width <=
            ActualWidth - 8;

        var x = canPlaceOutside
            ? outsideX
            : Math.Max(
                8,
                selection.Right -
                size.Width -
                7);

        var y = Math.Clamp(
            selection.Bottom -
            size.Height,
            8,
            Math.Max(
                8,
                ActualHeight -
                size.Height -
                8));

        Canvas.SetLeft(_ratioControl, x);
        Canvas.SetTop(_ratioControl, y);

        if (_ratioFlyout.Visibility == Visibility.Visible)
            PositionRatioFlyout(selection);
    }

    private void PositionRatioFlyout(Rect selection)
    {
        _ratioFlyout.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        var size = _ratioFlyout.DesiredSize;
        var controlX = Canvas.GetLeft(_ratioControl);
        var controlY = Canvas.GetTop(_ratioControl);

        var x = Math.Clamp(
            controlX + _ratioControl.ActualWidth - size.Width,
            8,
            Math.Max(
                8,
                ActualWidth - size.Width - 8));

        var preferredAbove =
            controlY - size.Height - 8;

        var y = preferredAbove >= 8
            ? preferredAbove
            : Math.Min(
                ActualHeight - size.Height - 8,
                controlY + _ratioControl.ActualHeight + 8);

        Canvas.SetLeft(_ratioFlyout, x);
        Canvas.SetTop(_ratioFlyout, Math.Max(8, y));
    }

    private void SelectTool(EditorTool tool)
    {
        if (_editor is null)
            return;

        _editor.Tool = tool;
        _editor.SelectAnnotation(null);
        _editor.Cursor =
            tool == EditorTool.Select
                ? Cursors.Arrow
                : Cursors.Cross;

        foreach (var pair in _toolButtons)
            ApplyToolButtonVisual(pair.Value);

        _editor.Focus();
    }

    private TextAnnotation? CreateTextAnnotation(
        Point origin)
    {
        var dialog =
            new TextInputWindow
            {
                Owner = this
            };

        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(
                dialog.EnteredText))
        {
            return null;
        }

        return new TextAnnotation(
            origin,
            dialog.EnteredText,
            _strokeBrush,
            CurrentTextSize());
    }

    private void AddImage()
    {
        if (_session is null)
            return;

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
        image.CacheOption =
            BitmapCacheOption.OnLoad;
        image.UriSource =
            new Uri(dialog.FileName);
        image.EndInit();
        image.Freeze();

        var baseImage =
            _session.Document.BaseImage;

        var maxW =
            Math.Min(
                420d,
                baseImage.PixelWidth * 0.45);
        var maxH =
            Math.Min(
                320d,
                baseImage.PixelHeight * 0.45);

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

        var annotation =
            new ImageAnnotation(
                image,
                new Rect(
                    (baseImage.PixelWidth - width) / 2,
                    (baseImage.PixelHeight - height) / 2,
                    width,
                    height));

        _session.Document.History.Execute(
            new AddAnnotationCommand(
                _session.Document,
                annotation));

        SelectTool(EditorTool.Select);
        _editor?.SelectAnnotation(annotation);
    }

    private async Task RunOcrAsync()
    {
        if (_session is null)
            return;

        try
        {
            var bitmap =
                DocumentRenderer.Render(
                    _session.Document);

            var text =
                await WindowsOcrService
                    .RecognizeAsync(bitmap);

            new OcrResultWindow(text)
            {
                Owner = this
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "文字识别",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PinImage()
    {
        if (_session is null)
            return;

        var bitmap =
            DocumentRenderer.Render(
                _session.Document);

        new PinWindow(bitmap).Show();
    }

    private void CopyImage()
    {
        if (_session is null)
            return;

        try
        {
            CaptureClipboard.SetImage(
                DocumentRenderer.Render(
                    _session.Document));

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "复制截图",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveToCurrent()
    {
        if (_saveLocations is null)
            return;

        SaveToPath(
            _saveLocations
                .BuildAutomaticFilePath());
    }

    private void SaveToDirectory(
        string directory)
    {
        if (_saveLocations is null)
            return;

        _saveLocations.Remember(directory);
        UpdateSaveButtonLabel();

        SaveToPath(
            _saveLocations
                .BuildAutomaticFilePath());
    }

    private void SaveAs()
    {
        if (_session is null ||
            _saveLocations is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "另存为",
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            InitialDirectory =
                _saveLocations.CurrentDirectory,
            FileName =
                "Yanzi-" +
                DateTime.Now.ToString(
                    "yyyyMMdd-HHmmss") +
                ".png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var directory =
            IOPath.GetDirectoryName(
                dialog.FileName);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            _saveLocations.Remember(directory);
            UpdateSaveButtonLabel();
        }

        SaveToPath(dialog.FileName);
    }

    private void SaveToPath(string path)
    {
        if (_session is null)
            return;

        try
        {
            EditorWindow.SavePng(
                DocumentRenderer.Render(
                    _session.Document),
                path);

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "保存截图",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenCurrentDirectory()
    {
        if (_saveLocations is null)
            return;

        Directory.CreateDirectory(
            _saveLocations.CurrentDirectory);

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    _saveLocations
                        .CurrentDirectory,
                UseShellExecute = true
            });
    }

    private void UpdateSaveButtonLabel()
    {
        if (_saveMainButton is null ||
            _saveLocations is null)
        {
            return;
        }

        _saveMainButton.ToolTip =
            $"保存到：{_saveLocations.CurrentDirectory} (Ctrl+S)";
    }

    private void CycleColor()
    {
        _strokeIndex =
            (_strokeIndex + 1) %
            _strokePalette.Length;

        _strokeBrush =
            _strokePalette[_strokeIndex];

        if (_editor is not null)
            _editor.StrokeBrush =
                _strokeBrush;

        if (_colorIndicator is not null)
            _colorIndicator.Fill = _strokeBrush;
    }

    private void CycleThickness()
    {
        _thicknessIndex =
            (_thicknessIndex + 1) %
            _thicknesses.Length;

        if (_editor is not null)
        {
            _editor.StrokeThickness =
                _thicknesses[_thicknessIndex];
            _editor.TextSize =
                CurrentTextSize();
        }

        foreach (var child in
                 _toolPanel.Children
                     .OfType<Button>())
        {
            if (AutomationProperties
                    .GetAutomationId(child) ==
                "capture.workspace.thickness")
            {
                child.ToolTip =
                    $"切换粗细（当前 {_thicknesses[_thicknessIndex]:0}）";
                break;
            }
        }
    }

    private double CurrentTextSize() =>
        _thicknesses[_thicknessIndex] switch
        {
            <= 2 => 20,
            >= 9 => 38,
            _ => 28
        };

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (key == Key.S)
            {
                if (_session is not null)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        SaveAs();
                    }
                    else
                    {
                        SaveToCurrent();
                    }
                    e.Handled = true;
                    return;
                }
            }
            else if (key == Key.C)
            {
                if (_session is not null)
                {
                    CopyImage();
                    e.Handled = true;
                    return;
                }
            }
            else if (key == Key.Z)
            {
                if (_session is not null)
                {
                    _session.Document.History.Undo();
                    e.Handled = true;
                    return;
                }
            }
            else if (key == Key.Y)
            {
                if (_session is not null)
                {
                    _session.Document.History.Redo();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (key != Key.Escape)
            return;

        if (_editor?.IsInteracting == true)
        {
            _editor.CancelCurrentInteraction();
            e.Handled = true;
            return;
        }

        e.Handled = true;
        Close();
    }

    private void UpdateScale()
    {
        _scaleX =
            _screen.PixelWidth /
            Math.Max(1, ActualWidth);
        _scaleY =
            _screen.PixelHeight /
            Math.Max(1, ActualHeight);

        if (_session is not null)
            UpdateSessionLayout();
    }

    private Rect CurrentSelectionDipRect() =>
        _session is null
            ? Rect.Empty
            : PixelsToDip(
                _session.SelectionPixels);

    private Int32Rect DipToPixels(Rect rect)
    {
        rect = ClampRect(
            Normalize(rect));

        var x =
            (int)Math.Round(
                rect.X * _scaleX);
        var y =
            (int)Math.Round(
                rect.Y * _scaleY);
        var width =
            Math.Max(
                1,
                (int)Math.Round(
                    rect.Width * _scaleX));
        var height =
            Math.Max(
                1,
                (int)Math.Round(
                    rect.Height * _scaleY));

        x = Math.Clamp(
            x,
            0,
            _screen.PixelWidth - 1);
        y = Math.Clamp(
            y,
            0,
            _screen.PixelHeight - 1);
        width = Math.Clamp(
            width,
            1,
            _screen.PixelWidth - x);
        height = Math.Clamp(
            height,
            1,
            _screen.PixelHeight - y);

        return new Int32Rect(
            x,
            y,
            width,
            height);
    }

    private Rect PixelsToDip(Int32Rect rect) =>
        new(
            rect.X / _scaleX,
            rect.Y / _scaleY,
            rect.Width / _scaleX,
            rect.Height / _scaleY);

    private Rect CalculateAdjustedRect(
        SelectionHandle handle,
        Rect start,
        Point pointerStart,
        Point current)
    {
        if (handle == SelectionHandle.Move)
        {
            var delta =
                current - pointerStart;

            var moved =
                new Rect(
                    start.Location + delta,
                    start.Size);

            if (moved.Left < 0)
                moved.X = 0;
            if (moved.Top < 0)
                moved.Y = 0;
            if (moved.Right > ActualWidth)
                moved.X =
                    ActualWidth -
                    moved.Width;
            if (moved.Bottom > ActualHeight)
                moved.Y =
                    ActualHeight -
                    moved.Height;

            return moved;
        }

        if (_lockedAspectRatio is double ratio)
        {
            return ConstrainRectToAspect(
                handle,
                start,
                pointerStart,
                current,
                ratio);
        }

        if (handle == SelectionHandle.Create)
            return new Rect(pointerStart, current);

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        if (handle is
            SelectionHandle.West or
            SelectionHandle.NorthWest or
            SelectionHandle.SouthWest)
        {
            left = current.X;
        }

        if (handle is
            SelectionHandle.East or
            SelectionHandle.NorthEast or
            SelectionHandle.SouthEast)
        {
            right = current.X;
        }

        if (handle is
            SelectionHandle.North or
            SelectionHandle.NorthWest or
            SelectionHandle.NorthEast)
        {
            top = current.Y;
        }

        if (handle is
            SelectionHandle.South or
            SelectionHandle.SouthWest or
            SelectionHandle.SouthEast)
        {
            bottom = current.Y;
        }

        return new Rect(
            new Point(left, top),
            new Point(right, bottom));
    }

    private Rect ConstrainRectToAspect(
        SelectionHandle handle,
        Rect start,
        Point pointerStart,
        Point current,
        double ratio)
    {
        ratio = Math.Max(0.01, ratio);

        if (handle == SelectionHandle.Create)
        {
            return CreateAspectRectFromAnchor(
                pointerStart,
                current,
                ratio);
        }

        if (handle is
            SelectionHandle.NorthWest or
            SelectionHandle.NorthEast or
            SelectionHandle.SouthWest or
            SelectionHandle.SouthEast)
        {
            var anchor = handle switch
            {
                SelectionHandle.NorthWest =>
                    new Point(start.Right, start.Bottom),
                SelectionHandle.NorthEast =>
                    new Point(start.Left, start.Bottom),
                SelectionHandle.SouthWest =>
                    new Point(start.Right, start.Top),
                _ =>
                    new Point(start.Left, start.Top)
            };

            return CreateAspectRectFromAnchor(
                anchor,
                current,
                ratio);
        }

        if (handle is
            SelectionHandle.East or
            SelectionHandle.West)
        {
            var anchorX =
                handle == SelectionHandle.East
                    ? start.Left
                    : start.Right;
            var width =
                Math.Max(
                    1,
                    Math.Abs(current.X - anchorX));
            var height =
                Math.Max(1, width / ratio);
            var centerY =
                start.Top + start.Height / 2;

            var x =
                handle == SelectionHandle.East
                    ? anchorX
                    : anchorX - width;

            return FitAspectRectToScreen(
                new Rect(
                    x,
                    centerY - height / 2,
                    width,
                    height),
                ratio);
        }

        if (handle is
            SelectionHandle.North or
            SelectionHandle.South)
        {
            var anchorY =
                handle == SelectionHandle.South
                    ? start.Top
                    : start.Bottom;
            var height =
                Math.Max(
                    1,
                    Math.Abs(current.Y - anchorY));
            var width =
                Math.Max(1, height * ratio);
            var centerX =
                start.Left + start.Width / 2;

            var y =
                handle == SelectionHandle.South
                    ? anchorY
                    : anchorY - height;

            return FitAspectRectToScreen(
                new Rect(
                    centerX - width / 2,
                    y,
                    width,
                    height),
                ratio);
        }

        return start;
    }

    private Rect CreateAspectRectFromAnchor(
        Point anchor,
        Point current,
        double ratio)
    {
        var dx = current.X - anchor.X;
        var dy = current.Y - anchor.Y;

        var signX = dx < 0 ? -1d : 1d;
        var signY = dy < 0 ? -1d : 1d;

        var width = Math.Max(1, Math.Abs(dx));
        var height = Math.Max(1, Math.Abs(dy));

        if (width / height > ratio)
            height = width / ratio;
        else
            width = height * ratio;

        var end = new Point(
            anchor.X + signX * width,
            anchor.Y + signY * height);

        return FitAspectRectToScreen(
            new Rect(anchor, end),
            ratio);
    }

    private Rect FitAspectRectToScreen(
        Rect rect,
        double ratio)
    {
        rect = Normalize(rect);

        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);

        var scale = Math.Min(
            1,
            Math.Min(
                ActualWidth / width,
                ActualHeight / height));

        width *= scale;
        height *= scale;

        if (Math.Abs(width / height - ratio) > 0.001)
            height = width / ratio;

        if (height > ActualHeight)
        {
            height = ActualHeight;
            width = height * ratio;
        }

        var x = Math.Clamp(
            rect.X,
            0,
            Math.Max(0, ActualWidth - width));
        var y = Math.Clamp(
            rect.Y,
            0,
            Math.Max(0, ActualHeight - height));

        return new Rect(
            x,
            y,
            width,
            height);
    }

    private Rect ClampRect(Rect rect)
    {
        rect = Normalize(rect);

        var left =
            Math.Clamp(
                rect.Left,
                0,
                ActualWidth);
        var top =
            Math.Clamp(
                rect.Top,
                0,
                ActualHeight);
        var right =
            Math.Clamp(
                rect.Right,
                0,
                ActualWidth);
        var bottom =
            Math.Clamp(
                rect.Bottom,
                0,
                ActualHeight);

        return new Rect(
            new Point(left, top),
            new Point(right, bottom));
    }

    private Point ClampDip(Point point) =>
        new(
            Math.Clamp(
                point.X,
                0,
                ActualWidth),
            Math.Clamp(
                point.Y,
                0,
                ActualHeight));

    private static Rect Normalize(Rect rect) =>
        new(
            Math.Min(
                rect.Left,
                rect.Right),
            Math.Min(
                rect.Top,
                rect.Bottom),
            Math.Abs(rect.Width),
            Math.Abs(rect.Height));

    private static void SetElementRect(
        FrameworkElement element,
        Rect rect)
    {
        Canvas.SetLeft(
            element,
            rect.X);
        Canvas.SetTop(
            element,
            rect.Y);
        element.Width =
            Math.Max(0, rect.Width);
        element.Height =
            Math.Max(0, rect.Height);
    }

    private static bool SameRect(
        Int32Rect a,
        Int32Rect b) =>
        a.X == b.X &&
        a.Y == b.Y &&
        a.Width == b.Width &&
        a.Height == b.Height;

    private static Cursor CursorForHandle(
        SelectionHandle handle) =>
        handle switch
        {
            SelectionHandle.North or
            SelectionHandle.South =>
                Cursors.SizeNS,

            SelectionHandle.East or
            SelectionHandle.West =>
                Cursors.SizeWE,

            SelectionHandle.NorthWest or
            SelectionHandle.SouthEast =>
                Cursors.SizeNWSE,

            _ => Cursors.SizeNESW
        };

    private static SelectionHandle FindTaggedHandle(
        DependencyObject? source)
    {
        var current = source;

        while (current is not null)
        {
            if (current is FrameworkElement fe &&
                fe.Tag is SelectionHandle handle)
            {
                return handle;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return SelectionHandle.None;
    }

    private static bool IsDescendantOf(
        DependencyObject ancestor,
        DependencyObject? child)
    {
        var current = child;

        while (current is not null)
        {
            if (ReferenceEquals(
                    current,
                    ancestor))
            {
                return true;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return false;
    }

    private static string DisplayDirectory(
        string directory)
    {
        try
        {
            var info =
                new DirectoryInfo(directory);

            return string.IsNullOrWhiteSpace(
                info.Name)
                ? directory
                : info.Name;
        }
        catch
        {
            return directory;
        }
    }

    private static SolidColorBrush Brush(
        string hex) =>
        (SolidColorBrush)
        new BrushConverter()
            .ConvertFromString(hex)!;
}

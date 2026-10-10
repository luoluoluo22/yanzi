using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UiTesting;
using Yanzi.UI.Wpf;

namespace Yanzi.UiTestSamples;

/// <summary>
/// Real Yanzi.UI.Wpf theme and button tested in an isolated desktop.
/// This scenario does not send global mouse/keyboard events.
/// </summary>
public sealed class PublicControlsScenario : IUiTestScenario
{
    public string Name => "Yanzi shared WPF UI controls";
    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var window = new Window
        {
            Title="燕子公共 UI：隐藏桌面验收",
            Width=580,Height=260,
            Background=Brushes.Black
        };
        YanziUi.ApplyTo(window,YanziTheme.Dark);
        var message=new TextBlock
        {
            Text="UI TEST: WAITING",
            FontSize=22,
            Foreground=Brushes.White,
            Margin=new Thickness(8,5,0,15)
        };
        var button=YanziUi.WithStyle(new Button
        {
            Content="TEST SHARED BUTTON",
            Width=260,
            Height=45,
            HorizontalAlignment=HorizontalAlignment.Left
        },YanziUi.Styles.PrimaryButton);
        button.Click += (_,_) =>
        {
            message.Text="UI TEST: CLICKED";
            message.Foreground=Brushes.LightGreen;
            button.Content="BUTTON VERIFIED";
        };
        var stack=new StackPanel { Margin=new Thickness(25) };
        stack.Children.Add(message);
        stack.Children.Add(button);
        window.Content=stack;
        try
        {
            context.ShowWindow(window);
            context.Check(window.IsVisible,"window displayed on isolated desktop");
            context.Check(window.Resources.MergedDictionaries.Count>=2,"shared theme loaded");
            context.Check(button.Style is not null,"shared primary button style resolved");
            context.Click(button);
            await Task.Delay(120,cancellationToken);
            context.Check(message.Text=="UI TEST: CLICKED","WPF routed click updated text");
            context.Check((string?)button.Content=="BUTTON VERIFIED","WPF routed click updated button");
            var image=context.CaptureWindow(window,"shared-controls-after-click");
            context.Check(File.Exists(image.VisualTreePng)
                          && new FileInfo(image.VisualTreePng).Length > 2000,
                          "WPF visual tree captured");
            context.Check(context.Mode=="offscreen" ||
                          (image.PrintWindowReturned && image.WindowColorVariety>=8),
                          "hidden desktop PrintWindow captured actual WPF UI",
                          $"printed={image.PrintWindowReturned}, colors={image.WindowColorVariety}, mode={context.Mode}");
        }
        finally { window.Close(); }
    }
}

/// <summary>Warehouse menu width is measured from its widest rendered row.</summary>
public sealed class IntrinsicDropdownWidthScenario : IUiTestScenario
{
    public string Name => "Shared dropdown content-adaptive width";

    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var trigger = new Button { Content = "···", Width = 42, Height = 30 };
        var host = new Window
        {
            Title = "仓库菜单自适应宽度验收",
            Width = 460,
            Height = 270,
            Content = new StackPanel { Children = { trigger } }
        };
        YanziUi.ApplyTo(host, YanziTheme.Dark);
        var menu = new YanziDropdownMenu { PreferAbove = false, AlignStart = true };
        menu.UseStandaloneTheme(YanziTheme.Dark);
        menu.UseContentWidth(minimumWidth: 176);
        menu.AddAction("导出 Skill", () => { },
            icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
        menu.AddAction("同步燕子云", () => { },
            icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
        menu.AddSeparator();
        menu.AddAction("帮助文档", () => { },
            icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
        menu.AddAction("赞助维护与卡密", () => { },
            icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
        menu.AddAction("关于燕子", () => { },
            icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });

        try
        {
            context.ShowWindow(host);
            menu.Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var normalWidth = menu.Surface.DesiredSize.Width;
            context.Check(double.IsNaN(menu.Surface.Width) && menu.Surface.MinWidth == 176,
                "Warehouse uses Auto width with an explicit minimum");
            context.Check(normalWidth >= 176 && normalWidth < 264,
                "Warehouse's actual five item labels are narrower than old fixed 264-DIP menu",
                $"measured={normalWidth:F1}");
            menu.Attach(trigger);
            menu.IsOpen = true;
            await Task.Delay(130, cancellationToken);
            context.Require(menu.IsOpen, "Auto-width menu opens from footer trigger");
            var renderedWidth = menu.Surface.ActualWidth;
            context.Check(Math.Abs(renderedWidth - normalWidth) <= 2,
                "Rendered menu uses intrinsic measured width",
                $"measured={normalWidth:F1}, rendered={renderedWidth:F1}");
            var rootPoint = menu.Surface.PointToScreen(new Point(0, 0));
            var triggerLeft = trigger.PointToScreen(new Point(0, 0));
            context.Check(Math.Abs(rootPoint.X - triggerLeft.X) <= 3,
                "Warehouse dropdown aligns its LEFT edge to trigger when right side fits");
            menu.IsOpen = false;

            const string longerLabel = "一个明显超过所有其他菜单项的超长测试操作文字";
            var button = menu.AddAction(longerLabel, () => { },
                icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
            menu.Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var longWidth = menu.Surface.DesiredSize.Width;
            context.Check(longWidth > normalWidth + 60,
                "Adding the longest item expands the entire dropdown, not just its row",
                $"before={normalWidth:F1}, after={longWidth:F1}");
            var title = ((Grid)button.Content).Children.OfType<TextBlock>().First();
            context.Check(longWidth >= title.DesiredSize.Width + 16 + 8 + 16,
                "Wide text and icon/padding fit inside the menu without clipping");
            menu.IsOpen = true;
            await Task.Delay(90, cancellationToken);
            context.Check(Math.Abs(menu.Surface.ActualWidth - longWidth) <= 2,
                "Opening again recalculates the new content width");
            context.Check(!double.IsNaN(menu.Surface.ActualWidth) && menu.Surface.ActualWidth > 0,
                "Custom popup placement never receives NaN width");
            menu.IsOpen = false;

            // Reposition the trigger very close to the active monitor's right
            // boundary. The same menu must flip, keeping both parts visible.
            host.Left = SystemParameters.WorkArea.Right - host.Width - 8;
            host.Top = SystemParameters.WorkArea.Top + 30;
            trigger.HorizontalAlignment = HorizontalAlignment.Right;
            host.UpdateLayout();
            var triggerRight = trigger.PointToScreen(new Point(trigger.ActualWidth, 0));
            menu.IsOpen = true;
            await Task.Delay(100, cancellationToken);
            rootPoint = menu.Surface.PointToScreen(new Point(0, 0));
            context.Check(Math.Abs(rootPoint.X + menu.Surface.ActualWidth - triggerRight.X) <= 4,
                "Warehouse flips to RIGHT-edge alignment near monitor boundary");
        }
        finally
        {
            menu.IsOpen = false;
            host.Close();
        }
    }
}

/// <summary>Verifies a real ownerless tray-style menu with left-opening nested panels.</summary>
public sealed class LeftOpeningTrayMenuScenario : IUiTestScenario
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    private static NativeRect PopupBounds(FrameworkElement menuRoot)
    {
        var source = PresentationSource.FromVisual(menuRoot) as HwndSource;
        if (source is null || !GetWindowRect(source.Handle, out var rect))
            throw new InvalidOperationException("Cannot inspect Popup HWND bounds.");
        return rect;
    }
    private static bool SameBounds(NativeRect a, NativeRect b) =>
        a.Left == b.Left && a.Top == b.Top &&
        a.Right == b.Right && a.Bottom == b.Bottom;
    public string Name => "Shared left-opening tray submenu";
    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var host = new Window { Title = "菜单隔离验收", Width = 440, Height = 180 };
        YanziUi.ApplyTo(host, YanziTheme.Dark);
        host.Content = new TextBlock { Text = "Tray menu UI verification", Margin = new Thickness(20) };
        var clicked = false;
        var menu = new YanziDropdownMenu { OpenChildrenToLeft = true, SubmenuWidth = 200 };
        menu.Surface.Width = 220;
        menu.UseStandaloneTheme(YanziTheme.Dark);
        var blacklist = menu.AddSubmenu("应用黑名单", child =>
        {
            child.AddAction("添加当前应用", () => clicked = true);
            child.AddAction("管理黑名单", () => clicked = true);
        }, icon: new Border { Width = 16, Height = 16, Background = Brushes.Gray });
        var tools = menu.AddSubmenu("工具与排错", child =>
        {
            child.AddSubmenu("高级操作", level =>
            {
                level.AddAction("检查输入状态", () => clicked = true);
            });
        });
        var normalAction = menu.AddAction("常规操作", () => clicked = true);
        var checkedAction = menu.AddCheck("显示状态", true, _ => { });
        var radioActions = menu.AddRadioGroup("主题", new[] { "深色", "浅色" }, "深色", _ => { });
        menu.AddAction("退出", () => clicked = true, destructive: true);

        try
        {
            context.ShowWindow(host);
            var bounds = SystemParameters.WorkArea;
            var cursor = new Point(bounds.Right - 15, bounds.Bottom - 15);
            menu.ShowAtScreenPoint(cursor, bounds);
            await Task.Delay(120, cancellationToken);
            context.Require(menu.IsOpen, "ownerless shared popup opened");
            context.Check(menu.Surface is Border { Background: not null }, "standalone theme resolved");
            var itemRow = blacklist.Content as Grid;
            var leadingIcon = itemRow?.Children.OfType<Border>().FirstOrDefault();
            context.Check(leadingIcon is { Margin.Right: 8 } && leadingIcon.Width == 16,
                "menu uses official 16-DIP icon plus explicit 8-DIP icon-to-text gap");
            context.Check(normalAction.Padding.Left == 8 && normalAction.Padding.Top == 6,
                "menu item uses 8-DIP horizontal and 6-DIP vertical padding");
            var checkIndicator = (checkedAction.Content as Grid)?.Children
                .Cast<UIElement>().FirstOrDefault(x => Grid.GetColumn(x) == 0) as FrameworkElement;
            var radioIndicator = (radioActions[0].Content as Grid)?.Children
                .Cast<UIElement>().FirstOrDefault(x => Grid.GetColumn(x) == 0) as FrameworkElement;
            context.Check(checkIndicator?.Margin.Right == 8 && radioIndicator?.Margin.Right == 8,
                "checkbox and radio indicators use the same 8-DIP gap");
            var rootBefore = menu.Surface.PointToScreen(new Point(0, 0));
            var windowBefore = PopupBounds(menu.Surface);
            context.Check(rootBefore.X >= bounds.Left && rootBefore.X < bounds.Right &&
                          rootBefore.Y >= bounds.Top && rootBefore.Y < bounds.Bottom,
                          "root menu positioned within the active screen");
            context.Require(menu.OpenSubmenu(blacklist), "first submenu opens");
            await Task.Delay(90, cancellationToken);
            var rootAfter = menu.Surface.PointToScreen(new Point(0, 0));
            context.Check(Math.Abs(rootAfter.X - rootBefore.X) <= 2 &&
                          Math.Abs(rootAfter.Y - rootBefore.Y) <= 2,
                          "left submenu does not shift its root anchor");
            context.Check(menu.OpenDepth == 1, "submenu depth one");
            var windowAfter = PopupBounds(menu.Surface);
            context.Check(SameBounds(windowBefore, windowAfter),
                "native Popup HWND rectangle remains unchanged when submenu opens");
            context.Check(Equals(blacklist.Background, blacklist.TryFindResource("Yanzi.Color.Accent")),
                "expanded submenu trigger keeps its active highlight");
            var container = VisualTreeHelper.GetParent(menu.Surface) as Canvas;
            context.Require(container is not null && container.Children.Count == 2 &&
                            Canvas.GetLeft(container.Children.OfType<Border>().First(x => x != menu.Surface)) <
                            Canvas.GetLeft(menu.Surface),
                            "submenu is on the LEFT of root menu");

            // Re-entrance into an already open trigger must not tear down the visible panel.
            var originalChild = container!.Children[1];
            for (int n = 0; n < 25; n++)
            {
                blacklist.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseEnterEvent
                });
            }
            context.Check(menu.OpenDepth == 1 && ReferenceEquals(container.Children[1], originalChild),
                "repeated hover on active trigger leaves original submenu mounted");
            context.Require(menu.OpenSubmenu(tools), "second independent submenu opens");
            context.Check(Equals(tools.Background, tools.TryFindResource("Yanzi.Color.Accent")) &&
                          !Equals(blacklist.Background, blacklist.TryFindResource("Yanzi.Color.Accent")),
                "sibling switch transfers highlight without retaining stale open state");
            await Task.Delay(30, cancellationToken);
            var nextMenuPoint = menu.Surface.PointToScreen(new Point(0, 0));
            context.Check(SameBounds(windowBefore, PopupBounds(menu.Surface)),
                "native Popup HWND rectangle remains fixed when switching submenu siblings");
            context.Check(Math.Abs(nextMenuPoint.X - rootBefore.X) <= 2 &&
                          Math.Abs(nextMenuPoint.Y - rootBefore.Y) <= 2,
                          "sibling submenu switch does not reposition the popup");
            for (int n = 0; n < 20; n++)
            {
                var sibling = n % 2 == 0 ? blacklist : tools;
                sibling.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = Mouse.MouseEnterEvent
                });
            }
            context.Check(menu.IsOpen && menu.OpenDepth == 1 &&
                          container.Children.Count == 2 &&
                          ReferenceEquals(container.Children[0], menu.Surface) &&
                          SameBounds(windowBefore, PopupBounds(menu.Surface)),
                          "rapid pointer-enter transitions between sibling submenus remain stable");
            context.Require(menu.OpenSubmenu(tools), "restore second submenu after repeated hover");
            var subpanel = container!.Children[1] as Border;
            var children = subpanel?.Child as StackPanel;
            var nestedTrigger = children?.Children.OfType<Button>().FirstOrDefault();
            context.Require(nestedTrigger is not null && menu.OpenSubmenu(nestedTrigger),
                            "third-level submenu opens to the left");
            context.Check(menu.OpenDepth == 2 && container.Children.Count == 3 &&
                          ReferenceEquals(container.Children[0], menu.Surface) &&
                          SameBounds(windowBefore, PopupBounds(menu.Surface)),
                          "nested levels fit inside fixed HWND without moving or resizing");
            await Task.Delay(90, cancellationToken);
            rootAfter = menu.Surface.PointToScreen(new Point(0, 0));
            context.Check(Math.Abs(rootAfter.X - rootBefore.X) <= 2 &&
                          Math.Abs(rootAfter.Y - rootBefore.Y) <= 2,
                          "third level preserves the original root position");

            var lastPanel = container.Children[2] as Border;
            var lastRows = lastPanel?.Child as StackPanel;
            var leaf = lastRows?.Children.OfType<Button>().FirstOrDefault();
            context.Require(leaf is not null && leaf.Style is not null,
                "leaf action uses common dropdown action style");
            context.Click(leaf!);
            context.Check(clicked && !menu.IsOpen && menu.OpenDepth == 0,
                "leaf click executes callback and closes the full menu");

            menu.ShowAtScreenPoint(new Point(bounds.Left + 20, bounds.Bottom - 15), bounds);
            context.Check(!menu.OpenChildrenToLeft, "left screen edge selects RIGHT expansion");
            var rightWindowBefore = PopupBounds(menu.Surface);
            context.Require(menu.OpenSubmenu(blacklist), "fallback submenu opens near left monitor boundary");
            container = VisualTreeHelper.GetParent(menu.Surface) as Canvas;
            context.Check(container is not null && Canvas.GetLeft(container.Children.OfType<Border>().First(x => x != menu.Surface)) >
                          Canvas.GetLeft(menu.Surface),
                "edge placement flips children to the RIGHT when no left space");
            context.Check(SameBounds(rightWindowBefore, PopupBounds(menu.Surface)),
                "right-side submenu keeps native popup bounds fixed");

            // Reproduce the screenshot: 962-DIP working area, root begins around
            // x=320, and the free space to its RIGHT fits a full child menu.
            // Old code picked LEFT merely because some left space was available.
            var narrowWidth = Math.Min(962, bounds.Width - 8);
            context.Require(narrowWidth >= 680, "test screen wide enough for screenshot regression");
            var screenshotArea = new Rect(bounds.Left + 4, bounds.Top,
                narrowWidth, bounds.Height);
            var screenshotCursor = new Point(screenshotArea.Left + 324,
                screenshotArea.Bottom - 15);
            menu.ShowAtScreenPoint(screenshotCursor, screenshotArea);
            await Task.Delay(100, cancellationToken);
            context.Check(!menu.OpenChildrenToLeft,
                "screenshot regression: enough room at right selects RIGHT, not LEFT");
            var screenshotRoot = menu.Surface.PointToScreen(new Point(0, 0));
            var toDip = (PresentationSource.FromVisual(menu.Surface) as HwndSource)?
                .CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var screenshotRootDip = toDip.Transform(screenshotRoot);
            context.Check(Math.Abs(screenshotRootDip.X - screenshotCursor.X) <= 3,
                "tray middle-screen click anchors main menu LEFT edge at the click",
                $"actualPhysical={screenshotRoot.X:F1} actualDip={screenshotRootDip.X:F1} targetDip={screenshotCursor.X:F1}");
            var screenshotWindow = PopupBounds(menu.Surface);
            context.Require(menu.OpenSubmenu(blacklist), "screenshot regression submenu opens");
            container = VisualTreeHelper.GetParent(menu.Surface) as Canvas;
            context.Check(container is not null &&
                          Canvas.GetLeft(container.Children.OfType<Border>().First(x => x != menu.Surface)) >
                          Canvas.GetLeft(menu.Surface),
                "screenshot regression: submenu actually renders to RIGHT");
            context.Check(SameBounds(screenshotWindow, PopupBounds(menu.Surface)),
                "screenshot regression: opening to RIGHT does not move/resize popup");
            context.Require(menu.OpenSubmenu(tools), "screenshot regression sibling switches");
            context.Check(SameBounds(screenshotWindow, PopupBounds(menu.Surface)),
                "screenshot regression: sibling switch does not move/resize popup");

            // A right-edge popup must still prefer LEFT when the right side
            // genuinely cannot fit a child; arrow orientation must also flip.
            var rightEdgeClick = new Point(screenshotArea.Right - 15,
                screenshotArea.Bottom - 15);
            menu.ShowAtScreenPoint(rightEdgeClick, screenshotArea);
            await Task.Delay(100, cancellationToken);
            context.Check(menu.OpenChildrenToLeft,
                "right screen edge selects LEFT expansion");
            var rightEdgeRoot = toDip.Transform(menu.Surface.PointToScreen(new Point(0, 0)));
            context.Check(rightEdgeRoot.X + menu.Surface.ActualWidth <= rightEdgeClick.X + 5,
                "tray flips ROOT to left of click only near right monitor edge",
                $"rootDip={rightEdgeRoot.X:F1}, widthDip={menu.Surface.ActualWidth:F1}, clickDip={rightEdgeClick.X:F1}");
            context.Require(menu.OpenSubmenu(blacklist), "right-edge fallback submenu opens");
            container = VisualTreeHelper.GetParent(menu.Surface) as Canvas;
            context.Check(container is not null &&
                          Canvas.GetLeft(container.Children.OfType<Border>().First(x => x != menu.Surface)) <
                          Canvas.GetLeft(menu.Surface),
                "right edge submenu actually renders to LEFT");
        }
        finally
        {
            menu.IsOpen = false;
            host.Close();
        }
    }
}

public sealed class ExpectedFailureScenario : IUiTestScenario
{
    public string Name => "Intentional failure checks";
    public Task RunAsync(UiTestContext context,CancellationToken cancellationToken)
    {
        context.Check(false,"intentional failing check, must produce exit 1");
        return Task.CompletedTask;
    }
}

public sealed class TimeoutScenario : IUiTestScenario
{
    public string Name => "Timeout kill regression";
    public async Task RunAsync(UiTestContext context,CancellationToken cancellationToken)
    {
        context.Check(true,"worker started");
        await Task.Delay(TimeSpan.FromSeconds(60),cancellationToken);
    }
}

/// <summary>
/// Exercises the actual capture extension's WPF OCR progress toast, without
/// recognizing external content or writing the user's system clipboard.
/// </summary>
public sealed class CaptureOcrToastScenario : IUiTestScenario
{
    public string Name => "Yanzi screenshot OCR feedback";
    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        Yanzi.Capture.OcrFeedback.BeginRecognizing();
        var field = typeof(Yanzi.Capture.OcrFeedback).GetField(
            "_currentToast", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        var toast = field?.GetValue(null) as Window;
        context.Require(toast is { IsVisible: true }, "OCR progress toast visible in isolated desktop");
        context.Check(toast!.Title=="OCR 正在识别","progress window title");
        var loading = context.CaptureWindow(toast,"capture-ocr-loading");
        context.Check(File.Exists(loading.VisualTreePng) &&
                      new FileInfo(loading.VisualTreePng).Length > 1500,
                      "progress visual captured with actual content");
        await Task.Delay(3150,cancellationToken);
        context.Check(toast.IsVisible,"progress remains visible after 3 seconds");
        // Empty text deliberately avoids writing anything to the real clipboard.
        await Yanzi.Capture.OcrFeedback.PublishAsync(
            new Yanzi.Capture.CaptureOcrResult("", "燕子 PaddleOCR", false, TimeSpan.Zero));
        context.Check(toast.Title=="未识别到文字","completion transitions to empty result state");
        var completed=context.CaptureWindow(toast,"capture-ocr-completed");
        context.Check(File.Exists(completed.VisualTreePng) &&
                      new FileInfo(completed.VisualTreePng).Length > 1500,
                      "completed visual captured with actual content");
        await Task.Delay(3250,cancellationToken);
        context.Check(!toast.IsVisible,"completed toast automatically closes after 3 seconds");
    }
}

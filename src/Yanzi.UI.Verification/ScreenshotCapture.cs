using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Yanzi.UI.Wpf;

internal static class ScreenshotCapture
{
    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(9, 9, 11)); // #09090b
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(18, 18, 21)); // #121215
    private static readonly Brush PopoverBg = new SolidColorBrush(Color.FromRgb(24, 24, 27)); // #18181b
    private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)); // #27272a
    private static readonly Brush Fg = new SolidColorBrush(Color.FromRgb(250, 250, 250)); // #fafafa
    private static readonly Brush MutedFg = new SolidColorBrush(Color.FromRgb(161, 161, 170)); // #a1a1aa
    private static readonly Brush AccentBg = new SolidColorBrush(Color.FromRgb(39, 39, 42)); // #27272a
    private static readonly Brush InputBg = new SolidColorBrush(Color.FromRgb(15, 15, 18));
    private static readonly Brush RingBrush = new SolidColorBrush(Color.FromRgb(212, 212, 216)); // #d4d4d8
    private static readonly Brush DestructiveBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #ef4444
    private static readonly FontFamily Geist = new FontFamily("Microsoft YaHei UI, Segoe UI");

    public static int Run(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        Console.WriteLine("Generating Phase 1 UI showcase images in: " + outputDir);

        try
        {
            // 1. Navigation Menu
            var navCard = BuildNavigationMenuCard();
            SaveVisual(navCard, System.IO.Path.Combine(outputDir, "01_navigation_menu.png"), 760, 420);
            Console.WriteLine("Saved: 01_navigation_menu.png");

            // 2. Combobox
            var comboCard = BuildComboboxCard();
            SaveVisual(comboCard, System.IO.Path.Combine(outputDir, "02_combobox.png"), 760, 420);
            Console.WriteLine("Saved: 02_combobox.png");

            // 3. Toast Stacking
            var toastCard = BuildToastCard();
            SaveVisual(toastCard, System.IO.Path.Combine(outputDir, "03_toast_stacking.png"), 760, 480);
            Console.WriteLine("Saved: 03_toast_stacking.png");

            // 4. Skeleton & Shimmer
            var skeletonCard = BuildSkeletonCard();
            SaveVisual(skeletonCard, System.IO.Path.Combine(outputDir, "04_skeleton_shimmer.png"), 760, 440);
            Console.WriteLine("Saved: 04_skeleton_shimmer.png");

            // 5. Input Groups
            var inputCard = BuildInputGroupCard();
            SaveVisual(inputCard, System.IO.Path.Combine(outputDir, "05_input_groups.png"), 760, 450);
            Console.WriteLine("Saved: 05_input_groups.png");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Screenshot capture error: " + ex);
            return 1;
        }
    }

    private static void SaveVisual(FrameworkElement visual, string path, int width, int height)
    {
        visual.Width = width;
        visual.Height = height;
        visual.Measure(new Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        visual.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static Border BuildCard(string title, string badge, string desc, FrameworkElement content, int width = 760, int height = 420)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            Background = Bg,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24)
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        var t = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Fg,
            FontFamily = Geist,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(t);

        var badgeElem = new Border
        {
            Background = AccentBg,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var badgeText = new TextBlock
        {
            Text = badge,
            FontSize = 11,
            Foreground = MutedFg,
            FontFamily = Geist
        };
        badgeElem.Child = badgeText;
        titleStack.Children.Add(badgeElem);
        header.Children.Add(titleStack);

        var d = new TextBlock
        {
            Text = desc,
            FontSize = 12,
            Foreground = MutedFg,
            FontFamily = Geist,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var headerStack = new StackPanel();
        headerStack.Children.Add(header);
        headerStack.Children.Add(d);
        Grid.SetRow(headerStack, 0);
        root.Children.Add(headerStack);

        Grid.SetRow(content, 1);
        root.Children.Add(content);
        border.Child = root;
        return border;
    }

    // 1. Navigation Menu Card
    private static Border BuildNavigationMenuCard()
    {
        var panel = new StackPanel();

        // 顶部水平导航行
        var navBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        navBar.Children.Add(CreateNavItem("快速开始", active: false));

        // 激活展开项
        var activeItem = new Border
        {
            Background = AccentBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var activeStack = new StackPanel { Orientation = Orientation.Horizontal };
        activeStack.Children.Add(new TextBlock { Text = "核心组件", Foreground = Fg, FontSize = 13, FontWeight = FontWeights.Medium, FontFamily = Geist, VerticalAlignment = VerticalAlignment.Center });
        var chev = CreatePathIcon("M6,15 L12,9 L18,15", 12, Fg);
        chev.RenderTransformOrigin = new Point(0.5, 0.5);
        chev.RenderTransform = new RotateTransform(180); // 展开时旋转180度
        chev.Margin = new Thickness(5, 0, 0, 0);
        activeStack.Children.Add(chev);
        activeItem.Child = activeStack;
        navBar.Children.Add(activeItem);

        navBar.Children.Add(CreateNavItem("文档与规范", active: false));
        navBar.Children.Add(CreateNavItem("设计令牌", active: false));
        panel.Children.Add(navBar);

        // 下方浮动卡片视口 Viewport
        var viewport = new Border
        {
            Width = 680,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = PopoverBg,
            BorderBrush = BorderBrush,
            Padding = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var col1 = new StackPanel();
        col1.Children.Add(CreateNavLink("Navigation Menu", "官方无边框菜单栏与浮动卡片视口，带旋转指示器", active: true));
        col1.Children.Add(CreateNavLink("Combobox", "带即时过滤搜索、Checkmark 选项槽位与双箭头触发器", active: false));
        col1.Children.Add(CreateNavLink("Dropdown Menu", "轻量弹出式动作行与快捷键提示列", active: false));
        Grid.SetColumn(col1, 0);
        grid.Children.Add(col1);

        var col2 = new StackPanel();
        col2.Children.Add(CreateNavLink("Toast (Sonner)", "自适应垂直堆叠、微动效与 Lucide 矢量状态图标", active: false));
        col2.Children.Add(CreateNavLink("Skeleton / Shimmer", "正弦呼吸脉冲与 45° 渐变流光扫光高级骨架屏", active: false));
        col2.Children.Add(CreateNavLink("Input Group", "内嵌放大镜搜索、快捷键提示与内嵌操作按钮", active: false));
        Grid.SetColumn(col2, 1);
        grid.Children.Add(col2);

        viewport.Child = grid;
        panel.Children.Add(viewport);

        return BuildCard("Navigation Menu", "官方标准无边框水平行 + Viewport 视口浮动卡片",
            "彻底剔除原厚重封闭外框与手绘三角形；触发项展开时 Chevron 矢量箭头顺畅旋转 180°，下拉浮层拥有官方一致的 16px 投影与双栏布局。",
            panel, 760, 420);
    }

    private static Border CreateNavItem(string text, bool active)
    {
        return new Border
        {
            Background = active ? AccentBg : Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = text,
                Foreground = active ? Fg : MutedFg,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                FontFamily = Geist,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Border CreateNavLink(string title, string desc, bool active)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(4, 2, 4, 2),
            Background = active ? AccentBg : Brushes.Transparent
        };
        var sp = new StackPanel();
        var t = new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist
        };
        var d = new TextBlock
        {
            Text = desc, FontSize = 12,
            Foreground = MutedFg, FontFamily = Geist,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        sp.Children.Add(t);
        sp.Children.Add(d);
        b.Child = sp;
        return b;
    }

    // 2. Combobox Card
    private static Border BuildComboboxCard()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // 左列：已选中并打开下拉列表
        var col1 = new StackPanel { Width = 320, Margin = new Thickness(0, 0, 40, 0) };
        col1.Children.Add(new TextBlock
        {
            Text = "已选状态与选项列表 (Next.js 选中)",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // 触发器 (带 Ring 焦点环)
        var trig1 = new Border
        {
            Width = 290, Height = 38, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = InputBg, BorderBrush = RingBrush
        };
        var tg1 = new Grid();
        tg1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tg1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        tg1.Children.Add(new TextBlock
        {
            Text = "Next.js", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0), FontSize = 13,
            Foreground = Fg, FontFamily = Geist
        });
        var arr1 = CreatePathIcon("M7,15 L12,20 L17,15 M7,9 L12,4 L17,9", 14, MutedFg);
        arr1.Opacity = 0.6;
        arr1.VerticalAlignment = VerticalAlignment.Center;
        arr1.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(arr1, 1);
        tg1.Children.Add(arr1);
        trig1.Child = tg1;
        col1.Children.Add(trig1);

        // 下拉列表
        var dropCard = new Border
        {
            Width = 290, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = PopoverBg, BorderBrush = BorderBrush,
            Padding = new Thickness(4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.25 }
        };
        var optStack = new StackPanel();
        optStack.Children.Add(CreateComboOption("Next.js", isSelected: true, isHover: false));
        optStack.Children.Add(CreateComboOption("SvelteKit", isSelected: false, isHover: false));
        optStack.Children.Add(CreateComboOption("Nuxt.js", isSelected: false, isHover: true)); // 悬停高亮
        optStack.Children.Add(CreateComboOption("Remix", isSelected: false, isHover: false));
        optStack.Children.Add(CreateComboOption("Astro", isSelected: false, isHover: false));
        dropCard.Child = optStack;
        col1.Children.Add(dropCard);
        panel.Children.Add(col1);

        // 右列：空状态与搜索过滤
        var col2 = new StackPanel { Width = 320 };
        col2.Children.Add(new TextBlock
        {
            Text = "即时过滤与无匹配 Empty 状态",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var trig2 = new Border
        {
            Width = 290, Height = 38, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Background = InputBg, BorderBrush = BorderBrush
        };
        var tg2 = new Grid();
        tg2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tg2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        tg2.Children.Add(new TextBlock
        {
            Text = "flutter", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0), FontSize = 13,
            Foreground = Fg, FontFamily = Geist
        });
        var arr2 = CreatePathIcon("M7,15 L12,20 L17,15 M7,9 L12,4 L17,9", 14, MutedFg);
        arr2.Opacity = 0.6;
        arr2.VerticalAlignment = VerticalAlignment.Center;
        arr2.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(arr2, 1);
        tg2.Children.Add(arr2);
        trig2.Child = tg2;
        col2.Children.Add(trig2);

        var emptyCard = new Border
        {
            Width = 290, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = PopoverBg, BorderBrush = BorderBrush,
            Padding = new Thickness(4)
        };
        emptyCard.Child = new TextBlock
        {
            Text = "No framework found.", TextAlignment = TextAlignment.Center,
            Padding = new Thickness(18), FontSize = 12,
            Foreground = MutedFg, FontFamily = Geist
        };
        col2.Children.Add(emptyCard);
        panel.Children.Add(col2);

        return BuildCard("Combobox", "带即时过滤、Checkmark 选项槽位与 ChevronsUpDown 双箭头",
            "触发框右侧应用官方标准 Lucide ChevronsUpDown 矢量双箭头；选项左侧 14px Checkmark 槽位未选中时占位保证基线对齐，选中项自动点亮矢量对勾并高亮 Accent 背景。",
            panel, 760, 420);
    }

    private static Border CreateComboOption(string label, bool isSelected, bool isHover)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(2, 1, 2, 1),
            Background = (isSelected || isHover) ? AccentBg : Brushes.Transparent
        };
        var dp = new DockPanel();
        var check = CreatePathIcon("M20,6 L9,17 L4,12", 14, Fg);
        check.Margin = new Thickness(0, 0, 8, 0);
        check.Visibility = isSelected ? Visibility.Visible : Visibility.Hidden; // 隐藏占位
        DockPanel.SetDock(check, Dock.Left);
        dp.Children.Add(check);

        var t = new TextBlock
        {
            Text = label, FontSize = 13,
            Foreground = Fg, FontFamily = Geist,
            VerticalAlignment = VerticalAlignment.Center
        };
        dp.Children.Add(t);
        b.Child = dp;
        return b;
    }

    // 3. Toast Card
    private static Border BuildToastCard()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        panel.Children.Add(CreateToastBubble("success", "事件已创建", "2026年10月11日 晚上 8:00", null));
        panel.Children.Add(CreateToastBubble("info", "项目已移至回收站", "可在 30 天内恢复", "撤销"));
        panel.Children.Add(CreateToastBubble("error", "请求未通过", "服务器响应 500 内部错误，请稍后重试。", null));
        panel.Children.Add(CreateToastBubble("loading", "正在上传文件...", "已完成 65% · 预估剩余 4 秒", null));

        return BuildCard("Toast (Sonner 风格)", "自适应垂直堆叠 (Stacking) + Lucide 矢量状态图标",
            "全面废除纯文本字符（✓ ! ×），升级为 Lucide Check、AlertCircle、Info、X 与平滑旋转加载圆弧；支持多条通知在右下角垂直顺延堆叠，自带 14px Slide-In 进场微动效与操作按钮。",
            panel, 760, 480);
    }

    private static Border CreateToastBubble(string kind, string title, string desc, string? actionLabel)
    {
        var border = new Border
        {
            Width = 480,
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = PopoverBg,
            BorderBrush = BorderBrush,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.22, Color = Colors.Black }
        };
        var container = new DockPanel { LastChildFill = true };

        var dismissBtn = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Child = CreatePathIcon("M18,6 L6,18 M6,6 L18,18", 12, MutedFg)
        };
        DockPanel.SetDock(dismissBtn, Dock.Right);
        container.Children.Add(dismissBtn);

        FrameworkElement icon = kind switch
        {
            "success" => CreatePathIcon("M20,6 L9,17 L4,12", 15, Fg),
            "error" => CreatePathIcon("M12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 Z M12,8 L12,12 M12,16 L12.01,16", 15, DestructiveBrush),
            "loading" => CreateSpinner(15),
            _ => CreatePathIcon("M12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 Z M12,16 L12,12 M12,8 L12.01,8", 15, Fg)
        };
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 2, 0, 0);
        DockPanel.SetDock(icon, Dock.Left);
        container.Children.Add(icon);

        var body = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist
        };
        body.Children.Add(t);
        if (!string.IsNullOrEmpty(desc))
        {
            var d = new TextBlock
            {
                Text = desc, FontSize = 12,
                Foreground = MutedFg, FontFamily = Geist,
                Margin = new Thickness(0, 2, 0, 0)
            };
            body.Children.Add(d);
        }
        if (!string.IsNullOrEmpty(actionLabel))
        {
            var act = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = actionLabel, FontSize = 11, Foreground = Fg, FontFamily = Geist }
            };
            body.Children.Add(act);
        }
        container.Children.Add(body);
        border.Child = container;
        return border;
    }

    private static FrameworkElement CreateSpinner(double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,3 A9,9 0 0,1 21,12"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Stroke = Fg
        };
        canvas.Children.Add(arc);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    // 4. Skeleton & Shimmer Card
    private static Border BuildSkeletonCard()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        // 方案 1：Pulse 脉冲呼吸
        var col1 = new StackPanel { Width = 320, Margin = new Thickness(0, 0, 40, 0) };
        col1.Children.Add(new TextBlock
        {
            Text = "经典脉冲呼吸 (Pulse with SineEase)",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var card1 = new Border
        {
            Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = CardBg, BorderBrush = BorderBrush
        };
        var s1 = new StackPanel();
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row1.Children.Add(CreateSkeletonBox(44, 44, 22)); // 圆形头像
        var l1 = new StackPanel { Margin = new Thickness(12, 4, 0, 0) };
        l1.Children.Add(CreateSkeletonBox(180, 12, 4));
        l1.Children.Add(new Border { Height = 8 });
        l1.Children.Add(CreateSkeletonBox(120, 11, 4));
        row1.Children.Add(l1);
        s1.Children.Add(row1);
        s1.Children.Add(CreateSkeletonBox(280, 10, 4));
        s1.Children.Add(new Border { Height = 8 });
        s1.Children.Add(CreateSkeletonBox(240, 10, 4));
        s1.Children.Add(new Border { Height = 8 });
        s1.Children.Add(CreateSkeletonBox(160, 10, 4));
        card1.Child = s1;
        col1.Children.Add(card1);
        panel.Children.Add(col1);

        // 方案 2：Shimmer 流光扫光
        var col2 = new StackPanel { Width = 320 };
        col2.Children.Add(new TextBlock
        {
            Text = "45° 流光扫光 (Shimmer Wave Overlay)",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Fg, FontFamily = Geist,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var card2 = new Border
        {
            Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = CardBg, BorderBrush = BorderBrush
        };
        var s2 = new StackPanel();
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        row2.Children.Add(CreateShimmerBox(44, 44, 22));
        var l2 = new StackPanel { Margin = new Thickness(12, 4, 0, 0) };
        l2.Children.Add(CreateShimmerBox(180, 12, 4));
        l2.Children.Add(new Border { Height = 8 });
        l2.Children.Add(CreateShimmerBox(120, 11, 4));
        row2.Children.Add(l2);
        s2.Children.Add(row2);
        s2.Children.Add(CreateShimmerBox(280, 10, 4));
        s2.Children.Add(new Border { Height = 8 });
        s2.Children.Add(CreateShimmerBox(240, 10, 4));
        s2.Children.Add(new Border { Height = 8 });
        s2.Children.Add(CreateShimmerBox(160, 10, 4));
        card2.Child = s2;
        col2.Children.Add(card2);
        panel.Children.Add(col2);

        return BuildCard("Skeleton & Shimmer", "正弦呼吸脉冲 (Pulse) 与 45° 渐变扫光 (Shimmer)",
            "升级原本生硬的线性闪烁为 SineEase 平滑呼吸动效；新增现代前端高阶的 Shimmer 流光扫光，在深浅主题下均能呈现丝滑柔和的高光条纹移动质感。",
            panel, 760, 440);
    }

    private static Border CreateSkeletonBox(double w, double h, double r)
    {
        return new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(r),
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)), // Muted #27272a
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static Border CreateShimmerBox(double w, double h, double r)
    {
        var box = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(r),
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = true
        };
        var overlay = new System.Windows.Shapes.Rectangle { Width = w, Height = h };
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 255, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
        overlay.Fill = brush;
        box.Child = overlay;
        return box;
    }

    // 5. Input Groups Card
    private static Border BuildInputGroupCard()
    {
        var panel = new StackPanel { Width = 660, HorizontalAlignment = HorizontalAlignment.Center };

        // 形态 1：搜索输入框
        var f1 = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        f1.Children.Add(new TextBlock { Text = "搜索输入组 (内嵌放大镜矢量图标与 ⌘K 快捷键提示)", FontSize = 12, Foreground = MutedFg, FontFamily = Geist, Margin = new Thickness(0, 0, 0, 5) });
        var g1 = new Border
        {
            Height = 36, CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1), BorderBrush = RingBrush, Background = InputBg
        };
        var g1Grid = new DockPanel();
        var searchIcon = CreatePathIcon("M11,19 C15.42,19 19,15.42 19,11 C19,6.58 15.42,3 11,3 C6.58,3 3,6.58 3,11 C3,15.42 6.58,19 11,19 Z M21,21 L16.65,16.65", 14, MutedFg);
        searchIcon.Margin = new Thickness(10, 0, 8, 0);
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(searchIcon, Dock.Left);
        g1Grid.Children.Add(searchIcon);

        var kbd = new Border
        {
            Background = AccentBg, CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "⌘K", FontSize = 11, Foreground = MutedFg, FontFamily = Geist }
        };
        DockPanel.SetDock(kbd, Dock.Right);
        g1Grid.Children.Add(kbd);

        g1Grid.Children.Add(new TextBlock
        {
            Text = "搜索组件、属性、或快速查找文档...",
            FontSize = 13, Foreground = MutedFg, FontFamily = Geist,
            VerticalAlignment = VerticalAlignment.Center
        });
        g1.Child = g1Grid;
        f1.Children.Add(g1);
        panel.Children.Add(f1);

        // 形态 2：带操作按钮的输入组
        var f2 = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        f2.Children.Add(new TextBlock { Text = "操作输入组 (内嵌“复制链接”紧凑操作按钮)", FontSize = 12, Foreground = MutedFg, FontFamily = Geist, Margin = new Thickness(0, 0, 0, 5) });
        var g2 = new Border
        {
            Height = 36, CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1), BorderBrush = BorderBrush, Background = InputBg
        };
        var g2Grid = new DockPanel();
        var copyBtn = new Border
        {
            Background = AccentBg, CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "复制链接", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Fg, FontFamily = Geist }
        };
        DockPanel.SetDock(copyBtn, Dock.Right);
        g2Grid.Children.Add(copyBtn);

        g2Grid.Children.Add(new TextBlock
        {
            Text = "https://yanzi.dev/ui/components",
            FontSize = 13, Foreground = Fg, FontFamily = Geist,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        g2.Child = g2Grid;
        f2.Children.Add(g2);
        panel.Children.Add(f2);

        // 形态 3：前缀与后缀输入组
        var f3 = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        f3.Children.Add(new TextBlock { Text = "前缀与后缀输入组 (https:// 与 .com 浅灰语义包裹)", FontSize = 12, Foreground = MutedFg, FontFamily = Geist, Margin = new Thickness(0, 0, 0, 5) });
        var g3 = new Border
        {
            Height = 36, CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1), BorderBrush = BorderBrush, Background = InputBg
        };
        var g3Grid = new DockPanel();
        var prefix = new TextBlock
        {
            Text = "https://", FontSize = 13, Foreground = MutedFg, FontFamily = Geist,
            Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(prefix, Dock.Left);
        g3Grid.Children.Add(prefix);

        var suffix = new TextBlock
        {
            Text = ".com", FontSize = 13, Foreground = MutedFg, FontFamily = Geist,
            Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(suffix, Dock.Right);
        g3Grid.Children.Add(suffix);

        g3Grid.Children.Add(new TextBlock
        {
            Text = "yanzi.design", FontSize = 13, Foreground = Fg, FontFamily = Geist,
            VerticalAlignment = VerticalAlignment.Center
        });
        g3.Child = g3Grid;
        f3.Children.Add(g3);
        panel.Children.Add(f3);

        return BuildCard("Input & Input Group", "幽灵占位符 + 内嵌图标/键帽 + 内嵌操作按钮",
            "原生 TextBox 包装为复合语义外框，支持内嵌放大镜矢量图标、⌘K 快捷键徽标、右侧紧凑操作按钮及前缀/后缀文本，焦点环统一高亮。",
            panel, 760, 450);
    }

    private static FrameworkElement CreatePathIcon(string geometry, double size, Brush stroke)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        var p = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Stroke = stroke,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        canvas.Children.Add(p);
        return new Viewbox
        {
            Width = size, Height = size, Child = canvas,
            Stretch = Stretch.Uniform, IsHitTestVisible = false
        };
    }
}

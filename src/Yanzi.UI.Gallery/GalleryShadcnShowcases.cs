using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

/// <summary>
/// Representative shadcn scenarios, not token-sized API smoke tests. Each component
/// retains real WPF input and can be interacted with on the comparison stage.
/// </summary>
internal sealed partial class GalleryWindow
{
    private static Border ReferenceStage(UIElement content)
    {
        var stage = new Border
        {
            MinHeight = 388,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(24, 27, 24, 27),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };
        stage.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Background");
        stage.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        return stage;
    }

    private static BitmapImage? DemoBitmap(string file)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", file);
        if (!File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private UIElement? BuildReferenceShowcase(string name) => name switch
    {
        "Accordion" => AccordionShowcase(),
        "Alert" => AlertShowcase(),
        "Attachment" => AttachmentShowcase(),
        "Avatar" => AvatarShowcase(),
        "Button Group" => ButtonGroupShowcase(),
        "Calendar" => CalendarShowcase(),
        "Card" => CardShowcase(),
        "Combobox" => ComboboxShowcase(),
        "Button" => ButtonShowcase(),
        "Input" => InputShowcase(),
        "Select" => SelectShowcase(),
        "Dialog" => DialogShowcase(),
        "Dropdown Menu" => DropdownMenuShowcase(),
        _ => null
    };

    private UIElement AccordionShowcase()
    {
        var accordion = new YanziAccordion { Width = 388, HorizontalAlignment = HorizontalAlignment.Center };
        accordion.Add("What are your shipping options?",
            Text("We offer standard and express shipping worldwide. Rates are calculated at checkout.",
                14, false, "Yanzi.Color.MutedForeground"));
        accordion.Add("What is your return policy?",
            Text("Returns accepted within 30 days. Items must be unused and in original packaging. Refunds processed within 5–7 business days.",
                14, false, "Yanzi.Color.Foreground"));
        accordion.Add("How can I contact customer support?",
            Text("You can reach our support team via email or live chat at any time.",
                14, false, "Yanzi.Color.MutedForeground"));
        accordion.Expand(1);
        return accordion;
    }

    private UIElement AlertShowcase()
    {
        var list = new StackPanel { Width = 448, HorizontalAlignment = HorizontalAlignment.Center };
        var success = YanziPrimitives.Alert("Payment successful",
            "Your payment of $29.99 has been processed. A receipt has been sent to your email address.", icon: "✓");
        success.Margin = new Thickness(0, 0, 0, 13);
        list.Children.Add(success);
        list.Children.Add(YanziPrimitives.Alert("New feature available",
            "We've added dark mode support. You can enable it in your account settings."));
        return list;
    }

    private UIElement AttachmentShowcase()
    {
        var all = new StackPanel { Width = 448, HorizontalAlignment = HorizontalAlignment.Center };
        var thumbs = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 13) };
        var files = new[]
        {
            ("office-1.jpg", "workspace.png", "PNG · 820 KB"),
            ("office-2.jpg", "desk-reference.jpg", "JPG · 1.1 MB"),
            ("office-3.jpg", "office-reference.jpg", "JPG · 940 KB")
        };
        foreach (var (photo, name, meta) in files)
            thumbs.Children.Add(AttachmentImage(photo, name, meta));
        all.Children.Add(thumbs);
        all.Children.Add(AttachmentFile("sales-dashboard.pdf", "Uploading · 64%", "↻", .64, true));
        all.Children.Add(AttachmentFile("message-render.tsx", "TypeScript · 12 KB", "⌘", 0, false));
        return all;
    }

    private Border AttachmentImage(string imageName, string label, string meta)
    {
        var card = new Border
        {
            Width = 138, Padding = new Thickness(7),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        var col = new StackPanel();
        var imageFrame = new Border { Height = 112, CornerRadius = new CornerRadius(7),
            ClipToBounds = true };
        var source = DemoBitmap(imageName);
        if (source is not null)
            imageFrame.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        else
            imageFrame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        col.Children.Add(imageFrame);
        var fileName = Text(label, 12, true, "Yanzi.Color.Foreground", new Thickness(3, 8, 0, 2));
        fileName.TextTrimming = TextTrimming.CharacterEllipsis;
        fileName.TextWrapping = TextWrapping.NoWrap;
        col.Children.Add(fileName);
        col.Children.Add(Text(meta, 11, false, "Yanzi.Color.MutedForeground", new Thickness(3, 0, 0, 2)));
        card.Child = col;
        return card;
    }

    private Border AttachmentFile(string name, string metadata, string icon, double progress, bool uploading)
    {
        var item = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 9, 11, 9), Margin = new Thickness(0, 0, 0, 10) };
        item.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        item.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        var glyph = new Border { Width = 37, Height = 39, CornerRadius = new CornerRadius(7) };
        glyph.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        glyph.Child = Text(icon, 17, true, "Yanzi.Color.Foreground");
        grid.Children.Add(glyph);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Text(name, 13, true, "Yanzi.Color.Foreground"));
        stack.Children.Add(Text(metadata, 12, false, "Yanzi.Color.MutedForeground",
            new Thickness(0, 3, 0, 0)));
        if (uploading)
        {
            var bar = new ProgressBar { Height = 3, Minimum = 0, Maximum = 1, Value = progress,
                Margin = new Thickness(0, 6, 7, 0) };
            YanziUi.WithStyle(bar, YanziUi.Styles.Progress);
            stack.Children.Add(bar);
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        var close = Button("×", YanziUi.Styles.GhostButton, () =>
        {
            item.Visibility = Visibility.Collapsed;
            Status("移除演示附件：" + name);
        });
        close.Width = 24;
        close.Height = 26;
        close.Margin = new Thickness(0);
        close.Padding = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetName(close, "Remove " + name);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        item.Child = grid;
        return item;
    }

    private UIElement AvatarShowcase()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center };
        var single = PictureAvatar("person-1.jpg", 36, false);
        single.Margin = new Thickness(0, 0, 36, 0);
        line.Children.Add(single);
        var statusAvatar = PictureAvatar("person-2.jpg", 36, true);
        statusAvatar.Margin = new Thickness(0, 0, 34, 0);
        line.Children.Add(statusAvatar);
        var grouped = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 1; i <= 3; i++)
        {
            var avatar = PictureAvatar("person-" + i + ".jpg", 33, false);
            avatar.Margin = new Thickness(i == 1 ? 0 : -10, 0, 0, 0);
            avatar.BorderThickness = new Thickness(2);
            grouped.Children.Add(avatar);
        }
        var more = YanziPrimitives.Avatar("+3", 33);
        more.Margin = new Thickness(-10, 0, 0, 0);
        grouped.Children.Add(more);
        line.Children.Add(grouped);
        return line;
    }

    private Border PictureAvatar(string file, double diameter, bool online)
    {
        var grid = new Grid { Width = diameter + 3, Height = diameter + 3 };
        var portrait = new Ellipse { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center };
        var bitmap = DemoBitmap(file);
        if (bitmap is not null)
            portrait.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        else portrait.SetResourceReference(Shape.FillProperty, "Yanzi.Color.Secondary");
        grid.Children.Add(portrait);
        if (online)
        {
            var dot = new Ellipse { Width = 10, Height = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                StrokeThickness = 2 };
            dot.SetResourceReference(Shape.StrokeProperty, "Yanzi.Color.Background");
            dot.SetResourceReference(Shape.FillProperty, "Yanzi.Color.Success");
            grid.Children.Add(dot);
        }
        return new Border { Width = diameter + 4, Height = diameter + 4,
            CornerRadius = new CornerRadius((diameter + 4)/2), Child = grid };
    }

    private UIElement ButtonGroupShowcase()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center };
        var back = Button("←", YanziUi.Styles.PillOutlineButton, () => Status("Back"));
        back.Width = 34;
        back.Height = 32;
        back.Margin = new Thickness(0, 0, 7, 0);
        back.Padding = new Thickness(0);
        row.Children.Add(back);
        var actions = new YanziSegmentedButtonGroup { Margin = new Thickness(0, 0, 7, 0) };
        var archive = actions.Add("Archive", () => Status("Archive"));
        var report = actions.Add("Report", () => Status("Report"));
        foreach (var item in new[] { archive, report })
        {
            item.Padding = new Thickness(11, 5, 11, 5);
            item.MinWidth = 35;
        }
        row.Children.Add(actions);
        var followups = new YanziSegmentedButtonGroup();
        var snooze = followups.Add("Snooze", () => Status("Snooze"));
        var more = followups.Add("•••", () => Status("More actions"));
        snooze.Padding = new Thickness(11, 5, 11, 5);
        more.Padding = new Thickness(8, 5, 8, 5);
        snooze.MinWidth = more.MinWidth = 35;
        row.Children.Add(followups);
        return row;
    }

    private UIElement CalendarShowcase()
    {
        var calendar = new YanziCalendarMonth(new DateTime(2026, 10, 1))
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        calendar.SelectDate(new DateTime(2026, 10, 8));
        calendar.DateSelected += (_, d) => Status("选中日期：" + d.ToString("yyyy-MM-dd"));
        return calendar;
    }

    private UIElement CardShowcase()
    {
        // All three Card slots are now rendered by the shared WPF component.
        var outer = new YanziCard { Width = 380,
            HorizontalAlignment = HorizontalAlignment.Center };
        var top = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Text("Login to your account", 16, true, "Yanzi.Color.Foreground"));
        var signup = Button("Sign Up", YanziUi.Styles.GhostButton, () => Status("Sign up · 演示"));
        signup.Height = 24;
        signup.Margin = new Thickness(0);
        Grid.SetColumn(signup, 1);
        header.Children.Add(signup);
        top.Children.Add(header);
        top.Children.Add(Text("Enter your email below to login to your account",
            14, false, "Yanzi.Color.MutedForeground", new Thickness(0, 7, 0, 0)));
        outer.SetHeader(top);
        var inputs = new StackPanel();
        inputs.Children.Add(Text("Email", 14, true, "Yanzi.Color.Foreground", new Thickness(0, 9, 0, 7)));
        var email = YanziUi.WithStyle(new TextBox { Height = 35, Text = "m@example.com",
            FontSize = 14, Padding = new Thickness(10, 4, 10, 4) }, YanziUi.Styles.InputSoft);
        inputs.Children.Add(email);
        var pwdLabel = new Grid { Margin = new Thickness(0, 17, 0, 7) };
        pwdLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pwdLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pwdLabel.Children.Add(Text("Password", 14, true, "Yanzi.Color.Foreground"));
        var forgot = Button("Forgot your password?", YanziUi.Styles.GhostButton, () => Status("Password reset · 演示"));
        forgot.Height = 24;
        forgot.Margin = new Thickness(0);
        Grid.SetColumn(forgot, 1);
        pwdLabel.Children.Add(forgot);
        inputs.Children.Add(pwdLabel);
        var password = YanziUi.WithStyle(new PasswordBox { Height = 35 }, YanziUi.Styles.Password);
        password.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Input");
        password.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
        password.SetResourceReference(Control.BorderBrushProperty, "Yanzi.Color.Border");
        inputs.Children.Add(password);
        outer.SetBody(inputs);
        var footer = new StackPanel { Background = Brushes.Transparent };
        var login = Button("Login", YanziUi.Styles.PillDefaultButton,
            () => Status("Login · 演示表单不发送凭据"));
        login.Margin = new Thickness(0, 0, 0, 8);
        login.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(login);
        var google = Button("Login with Google", YanziUi.Styles.PillSecondaryButton,
            () => Status("Google login · 演示"));
        google.Margin = new Thickness(0);
        google.HorizontalAlignment = HorizontalAlignment.Stretch;
        footer.Children.Add(google);
        outer.SetFooter(footer);
        return outer;
    }

    private UIElement ComboboxShowcase()
    {
        var box = new YanziCombobox("Select a framework") { Width = 218,
            HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var value in new[] { "Next.js", "SvelteKit", "Nuxt.js", "Remix", "Astro" })
            box.Add(value);
        box.SelectionChanged += (_, value) => Status("Combobox：" + (value ?? "未选择"));
        return box;
    }
}

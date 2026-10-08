using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

/// <summary>Owner-bound right-side sheet. Does not mutate other application windows.</summary>
public static class YanziSheet
{
    public static Window Show(Window owner, string title, UIElement content, bool modal = true)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        owner.VerifyAccess();
        var height = Math.Max(420, Math.Min(720, owner.ActualHeight));
        var sheet = new Window
        {
            Owner = owner, Title = title,
            Width = 390, Height = height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = owner.Left + Math.Max(0, owner.ActualWidth - 390),
            Top = owner.Top
        };
        YanziUi.ApplyTo(sheet, YanziUi.GetTheme(owner));
        sheet.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Background");
        sheet.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
        var root = new DockPanel { Margin = new Thickness(19) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        var headline = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 19 };
        headline.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        header.Children.Add(headline);
        var cancel = YanziUi.WithStyle(new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) }, YanziUi.Styles.OutlineButton);
        cancel.Click += (_, _) => sheet.Close();
        header.Children.Add(cancel);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        sheet.Content = root;
        if (modal) sheet.ShowDialog();
        else sheet.Show();
        return sheet;
    }
}

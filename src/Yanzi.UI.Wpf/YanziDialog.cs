using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Consistent, owner-aware destructive-action confirmation. UI thread only.</summary>
public static class YanziDialog
{
    public static bool Confirm(Window owner, string title, string description, string confirmLabel = "确定", bool dangerous = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.VerifyAccess();

        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 360,
            MaxWidth = 500,
            ShowInTaskbar = false
        };
        YanziUi.ApplyTo(dialog, YanziUi.GetTheme(owner));
        dialog.SetResourceReference(Control.BackgroundProperty, "Yanzi.Brush.Window");
        dialog.SetResourceReference(Control.ForegroundProperty, "Yanzi.Brush.Text");

        var layout = new StackPanel { Margin = new Thickness(20) };
        var headline = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold };
        headline.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Brush.Text");
        layout.Children.Add(headline);

        var content = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 18),
            MaxWidth = 430
        };
        YanziUi.WithStyle(content, YanziUi.Styles.TextSecondary);
        layout.Children.Add(content);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = YanziUi.WithStyle(new Button { Content = "取消", Margin = new Thickness(0, 0, 8, 0), IsCancel = true }, YanziUi.Styles.SecondaryButton);
        var accept = YanziUi.WithStyle(new Button { Content = confirmLabel, IsDefault = true }, dangerous ? YanziUi.Styles.DangerButton : YanziUi.Styles.PrimaryButton);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        accept.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(accept);
        layout.Children.Add(actions);
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }
}

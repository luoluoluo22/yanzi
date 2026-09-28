using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yanzi.Capture;

public sealed class TextInputWindow : Window
{
    private readonly TextBox _box = new();

    public string EnteredText => _box.Text?.Trim() ?? string.Empty;

    public TextInputWindow()
    {
        Title = "添加文字";
        Width = 420;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#171A20");
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "输入要放到截图上的文字",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });

        _box.AcceptsReturn = true;
        _box.TextWrapping = TextWrapping.Wrap;
        _box.Background = Brush("#20242B");
        _box.Foreground = Brushes.White;
        _box.BorderBrush = Brush("#353B46");
        _box.BorderThickness = new Thickness(1);
        _box.Padding = new Thickness(10);
        _box.FontSize = 14;
        AutomationProperties.SetAutomationId(_box, "capture.text.input");
        Grid.SetRow(_box, 2);
        root.Children.Add(_box);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = MakeButton("取消", "capture.text.cancel", false);
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var add = MakeButton("添加", "capture.text.confirm", true);
        add.Margin = new Thickness(8, 0, 0, 0);
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_box.Text)) return;
            DialogResult = true;
            Close();
        };

        actions.Children.Add(cancel);
        actions.Children.Add(add);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        Content = root;

        Loaded += (_, _) => _box.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter &&
                     Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                     !string.IsNullOrWhiteSpace(_box.Text))
            {
                DialogResult = true;
                Close();
            }
        };
    }

    private static Button MakeButton(string text, string id, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 74,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            Background = primary ? Brush("#3A8DFF") : Brush("#292E36"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

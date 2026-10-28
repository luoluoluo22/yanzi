using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Shadcn-style persistent menubar. Its triggers and all popup/submenu content
/// reuse YanziDropdownMenu; no operating-system Menu/MenuItem templates.
/// </summary>
public sealed class YanziMenubar : Border
{
    private readonly StackPanel _items = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly List<(Button Trigger, YanziDropdownMenu Menu)> _menus = [];

    public int MenuCount => _menus.Count;
    public IReadOnlyList<YanziDropdownMenu> Menus => _menus.Select(x => x.Menu).ToList();

    public YanziMenubar()
    {
        Padding = new Thickness(4);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Card");
        SetResourceReference(BorderBrushProperty, "Yanzi.Color.Border");
        Child = _items;
        PreviewKeyDown += OnArrowNavigation;
        // Popup has its own HWND: always dismiss it when an owning page/window is removed.
        Unloaded += (_, _) =>
        {
            foreach (var (_, menu) in _menus) menu.IsOpen = false;
        };
    }

    public Button AddMenu(string label, Action<YanziDropdownMenu> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(configure);
        var trigger = YanziUi.WithStyle(new Button
        {
            Content = label, MinHeight = 30,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(1, 0, 1, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        }, YanziUi.Styles.GhostButton);
        AutomationProperties.SetName(trigger, label);

        var dropdown = new YanziDropdownMenu { PreferAbove = false, AlignStart = true };
        configure(dropdown);
        dropdown.Attach(trigger);
        trigger.Click += (_, _) =>
        {
            foreach (var (_, other) in _menus)
                if (!ReferenceEquals(other, dropdown)) other.IsOpen = false;
        };
        trigger.MouseEnter += (_, _) =>
        {
            if (!_menus.Any(x => x.Menu.IsOpen) || dropdown.IsOpen) return;
            foreach (var (_, other) in _menus)
                if (!ReferenceEquals(other, dropdown)) other.IsOpen = false;
            dropdown.IsOpen = true;
        };
        _items.Children.Add(trigger);
        _menus.Add((trigger, dropdown));
        return trigger;
    }

    private void OnArrowNavigation(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || _menus.Count == 0) return;
        var selected = _menus.FindIndex(m => m.Trigger.IsKeyboardFocusWithin);
        if (selected < 0) return;
        var target = (selected + (e.Key == Key.Right ? 1 : _menus.Count - 1)) % _menus.Count;
        var wasOpen = _menus.Any(x => x.Menu.IsOpen);
        foreach (var (_, menu) in _menus) menu.IsOpen = false;
        _menus[target].Trigger.Focus();
        if (wasOpen) _menus[target].Menu.IsOpen = true;
        e.Handled = true;
    }
}

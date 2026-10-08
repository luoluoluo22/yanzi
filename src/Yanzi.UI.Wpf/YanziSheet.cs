using System.Windows;

namespace Yanzi.UI.Wpf;

/// <summary>
/// Backward-compatible Sheet facade. Keeps the existing Show signature, now with
/// a borderless modal owner overlay instead of a secondary native titlebar window.
/// </summary>
public static class YanziSheet
{
    public static Window Show(Window owner, string title, UIElement content, bool modal = true) =>
        ShowAt(owner, title, content, YanziSheetSide.Right, modal);

    public static Window ShowAt(Window owner, string title, UIElement content,
        YanziSheetSide side, bool modal = true)
    {
        var sheet = new YanziSheetOverlay(owner, title, content, side);
        if (modal) sheet.ShowDialog();
        else sheet.Show();
        return sheet;
    }

    public static Window ShowDrawer(Window owner, string title, UIElement content, bool modal = true) =>
        ShowAt(owner, title, content, YanziSheetSide.Bottom, modal);
}

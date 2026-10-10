using System.Windows;

namespace Yanzi.UI.Wpf;

/// <summary>Owner-modal shadcn-style alert confirmation without Windows toolwindow chrome.</summary>
public static class YanziDialog
{
    public static bool Confirm(Window owner, string title, string description,
        string confirmLabel = "确定", bool dangerous = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.VerifyAccess();
        return new YanziAlertDialog(owner, title, description, confirmLabel, dangerous).ShowDialog() == true;
    }
}

using System.Windows;
namespace OpenQuickHost;
public partial class App
{
    internal FrameworkElement? CreateExternalAccessPanel() => _externalAccessApproval?.CreateCenterPanel();
}

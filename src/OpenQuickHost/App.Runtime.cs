namespace OpenQuickHost;

public partial class App
{
    internal void ShowExternalApprovals()
    {
        if (HostRuntimeProfile.IsShell) RuntimeConnection.Queue("ui.approvals");
        else _externalAccessApproval?.ShowCenter();
    }
}

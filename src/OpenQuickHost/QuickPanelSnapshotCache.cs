namespace OpenQuickHost;

internal sealed record QuickPanelSnapshotKey(
    long SettingsVersion,
    string ContextProcess,
    string? GlobalGroupId,
    string? ContextGroupId,
    bool GlobalFavorites,
    bool ContextFavorites,
    int CommandsCount);

/// <summary>
/// 快捷面板槽位状态快照缓存：通过单调递增设置版本、进程标识、分组与命令总数进行毫秒级纯内存校验，
/// 杜绝磁盘 IO 与对全局上千个命令的高频 PropertyChanged 监听，使连续呼出时 99% 命中就地复用。
/// </summary>
internal sealed class QuickPanelSnapshotCache : IDisposable
{
    private QuickPanelSnapshotKey? _key;
    private volatile bool _dirty;
    private bool _ready;

    public bool CanReuse(QuickPanelSnapshotKey? key)
    {
        if (!_ready || _dirty || key == null || _key == null) return false;
        return key == _key;
    }

    public void UpdateKey(QuickPanelSnapshotKey? key)
    {
        _key = key;
        _dirty = false;
        _ready = true;
    }

    public void Invalidate()
    {
        _dirty = true;
        _ready = false;
        _key = null;
    }

    public void Dispose()
    {
        Invalidate();
    }
}

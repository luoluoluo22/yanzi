using System.ComponentModel;

namespace OpenQuickHost;

internal sealed record QuickPanelSnapshotKey(
    string SettingsPath, long WriteVersion, long LastWriteTicks, long FileLength,
    string ContextProcess, bool GlobalFavorites, bool ContextFavorites);

// UI-thread owned, with property-change invalidation allowed from background work.
internal sealed class QuickPanelSnapshotCache<T> : IDisposable where T : class, INotifyPropertyChanged
{
    private QuickPanelSnapshotKey? _key;
    private T[] _items = [];
    private volatile bool _dirty;
    private bool _ready;

    public bool CanReuse(QuickPanelSnapshotKey? key, IReadOnlyList<T> items)
    {
        if (!_ready || _dirty || key == null || key != _key || items.Count != _items.Length) return false;
        for (var i = 0; i < items.Count; i++)
            if (!ReferenceEquals(items[i], _items[i])) return false;
        return true;
    }

    public void BeginUpdate(QuickPanelSnapshotKey? key, IReadOnlyList<T> items)
    {
        Dispose();
        _key = key;
        _items = items.ToArray();
        _dirty = false;
        foreach (var item in _items) item.PropertyChanged += Invalidate;
    }

    public void CompleteUpdate() => _ready = true;

    private void Invalidate(object? sender, PropertyChangedEventArgs e) => _dirty = true;

    public void Dispose()
    {
        _ready = false;
        foreach (var item in _items) item.PropertyChanged -= Invalidate;
        _items = [];
    }
}

namespace OpenQuickHost;

/// <summary>Thread-safe burst gate: at most one scheduled flush per batch of watcher events.</summary>
internal sealed class ExtensionWatcherBatchGate
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingExtensions = new(StringComparer.OrdinalIgnoreCase);
    private bool _scheduled;
    private bool _catalogChanged;
    private int _eventCount;

    /// <returns>True only for the caller that should schedule the next flush.</returns>
    public bool Record(string extensionId, bool catalogChanged)
    {
        lock (_gate)
        {
            _pendingExtensions.Add(extensionId);
            _catalogChanged |= catalogChanged;
            _eventCount++;
            if (_scheduled)
                return false;
            _scheduled = true;
            return true;
        }
    }

    public ExtensionWatcherBatch Drain()
    {
        lock (_gate)
        {
            var batch = new ExtensionWatcherBatch(_eventCount, _pendingExtensions.Count, _catalogChanged);
            _pendingExtensions.Clear();
            _eventCount = 0;
            _catalogChanged = false;
            _scheduled = false;
            return batch;
        }
    }
}

internal readonly record struct ExtensionWatcherBatch(int EventCount, int ExtensionCount, bool CatalogChanged);

namespace OpenQuickHost;

/// <summary>
/// UI-thread-owned tracker. Separate conversations may run concurrently,
/// but each conversation admits only one pending or active request.
/// </summary>
public sealed class AiChatRequestTracker
{
    private readonly HashSet<string> _preparing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);
    public bool IsPreparing(string topicId) => _preparing.Contains(topicId);
    public bool IsActive(string topicId) => _active.ContainsKey(topicId);

    public bool TryPrepare(string topicId) =>
        !_active.ContainsKey(topicId) && _preparing.Add(topicId);

    public void Start(string topicId, CancellationTokenSource cancellation)
    {
        if (!_preparing.Contains(topicId) || !_active.TryAdd(topicId, cancellation))
            throw new InvalidOperationException("A conversation request is already active.");
        _preparing.Remove(topicId);
    }

    public bool TryGetActive(string topicId, out CancellationTokenSource cancellation)
    {
        if (_active.TryGetValue(topicId, out var active))
        {
            cancellation = active;
            return true;
        }
        cancellation = null!;
        return false;
    }

    public void EndPreparing(string topicId) => _preparing.Remove(topicId);
    public void Finish(string topicId) => _active.Remove(topicId);
}

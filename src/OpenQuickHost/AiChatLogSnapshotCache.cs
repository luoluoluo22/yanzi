using System.IO;

namespace OpenQuickHost;

/// <summary>
/// Reuses an unchanged log snapshot across the two-second UI refreshes. The caller
/// performs this work on a background thread; no WPF dispatcher access is required.
/// An in-process revision complements filesystem metadata (e.g. same-size writes).
/// </summary>
internal sealed class AiChatLogSnapshotCache
{
    private readonly object _gate = new();
    private string? _path;
    private LogStamp _stamp;
    private string[]? _lines;
    private string? _filteredTopic;
    private bool _filteredAllTopics;
    private string? _filteredText;

    private readonly record struct LogStamp(DateTime WriteUtc, long Length, long Revision);

    internal string ReadRecent(
        string path,
        string? topicId,
        bool allTopics,
        Func<long> revisionReader,
        Func<string, string[]> loadLines)
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(path))
                {
                    Clear();
                    return "尚无 AI 对话日志。发送消息后会记录请求阶段、耗时、HTTP 状态和错误类别。\n不会记录密钥、消息正文或模型回复。";
                }

                var stamp = GetStamp(path, revisionReader);
                if (_lines == null ||
                    !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) ||
                    _stamp != stamp)
                {
                    // Reader is expected to use the writer's file gate to avoid partial lines.
                    var fresh = loadLines(path);
                    var after = GetStamp(path, revisionReader);
                    // A concurrent append or rotation invalidates this read, so the next
                    // refresh will retry rather than serving stale data from the cache.
                    _lines = fresh;
                    _path = path;
                    _stamp = stamp == after ? stamp : default;
                    _filteredText = null;
                }

                if (_filteredText != null &&
                    string.Equals(_filteredTopic, topicId, StringComparison.Ordinal) &&
                    _filteredAllTopics == allTopics)
                {
                    return _filteredText;
                }

                var selected = _lines.Where(line => allTopics || string.IsNullOrEmpty(topicId) ||
                        line.Contains("|topic=" + topicId + "|", StringComparison.Ordinal))
                    .TakeLast(250).ToArray();
                var result = selected.Length > 0 ? string.Join(Environment.NewLine, selected)
                    : "当前对话尚无请求日志，可勾选「显示全部对话」查看其他记录。";
                _filteredTopic = topicId;
                _filteredAllTopics = allTopics;
                _filteredText = result;
                return result;
            }
        }
        catch (IOException e)
        {
            return "日志暂时无法读取：" + e.GetType().Name;
        }
        catch (UnauthorizedAccessException e)
        {
            return "日志暂时无法读取：" + e.GetType().Name;
        }
    }

    private static LogStamp GetStamp(string path, Func<long> revisionReader)
    {
        var info = new FileInfo(path);
        return new LogStamp(info.LastWriteTimeUtc, info.Length, revisionReader());
    }

    private void Clear()
    {
        _path = null;
        _stamp = default;
        _lines = null;
        _filteredTopic = null;
        _filteredText = null;
    }
}

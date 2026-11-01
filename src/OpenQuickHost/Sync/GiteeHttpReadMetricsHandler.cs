using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace OpenQuickHost.Sync;

/// <summary>
/// Counts actual Gitee GET dispatches at HttpMessageHandler, including retries,
/// without retaining or logging request URLs, paths, credentials or response bodies.
/// A completed one-minute window is reported on the next GET; idle periods are silent.
/// </summary>
internal sealed class GiteeHttpReadMetricsHandler : DelegatingHandler
{
    private readonly object _gate = new();
    private readonly Action<GiteeHttpReadMetricsSnapshot> _report;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _window;
    private DateTimeOffset _windowStart;
    private long _sentTotal;
    private long _completedTotal;
    private long _sent;
    private long _completed;
    private long _success;
    private long _notFound;
    private long _retryable;
    private long _otherHttp;
    private long _transportErrors;
    private long _canceled;
    private long _durationMsTotal;
    private long _durationMsMax;

    internal GiteeHttpReadMetricsHandler(
        HttpMessageHandler inner,
        Action<GiteeHttpReadMetricsSnapshot> report,
        TimeSpan? window = null,
        Func<DateTimeOffset>? clock = null) : base(inner)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _window = window ?? TimeSpan.FromMinutes(1);
        if (_window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        _windowStart = _clock();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        GiteeHttpReadMetricsSnapshot? due;
        lock (_gate)
        {
            due = TakeDueSnapshot();
            _sent++;
            _sentTotal++;
        }
        ReportSafely(due);

        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Complete(response.StatusCode, null, Stopwatch.GetElapsedTime(start));
            return response;
        }
        catch (OperationCanceledException)
        {
            Complete(null, true, Stopwatch.GetElapsedTime(start));
            throw;
        }
        catch
        {
            Complete(null, false, Stopwatch.GetElapsedTime(start));
            throw;
        }
    }

    private void Complete(HttpStatusCode? status, bool? canceled, TimeSpan elapsed)
    {
        lock (_gate)
        {
            _completed++;
            _completedTotal++;
            if (canceled == true) _canceled++;
            else if (canceled == false) _transportErrors++;
            else if ((int)status! >= 200 && (int)status! < 300) _success++;
            else if (status == HttpStatusCode.NotFound) _notFound++;
            else if ((int)status! == 429 || ((int)status! >= 500 && (int)status! < 600)) _retryable++;
            else _otherHttp++;
            var ms = Math.Max(0, (long)elapsed.TotalMilliseconds);
            _durationMsTotal += ms;
            _durationMsMax = Math.Max(_durationMsMax, ms);
        }
    }

    private GiteeHttpReadMetricsSnapshot? TakeDueSnapshot()
    {
        var now = _clock();
        if (now - _windowStart < _window) return null;
        var elapsed = Math.Max(1, (long)(now - _windowStart).TotalSeconds);
        _windowStart = now;
        if (_sent == 0) return null;
        var result = new GiteeHttpReadMetricsSnapshot(
            elapsed, _sent, _completed, _sentTotal, _completedTotal,
            _success, _notFound, _retryable, _otherHttp, _transportErrors,
            _canceled, _durationMsTotal, _durationMsMax);
        _sent = _completed = _success = _notFound = _retryable = _otherHttp =
            _transportErrors = _canceled = _durationMsTotal = _durationMsMax = 0;
        return result;
    }

    private void ReportSafely(GiteeHttpReadMetricsSnapshot? snapshot)
    {
        if (snapshot == null) return;
        try { _report(snapshot); }
        catch { /* diagnostics must never interrupt synchronization */ }
    }
}

internal sealed record GiteeHttpReadMetricsSnapshot(
    long WindowSeconds, long Sent, long Completed,
    long SentTotal, long CompletedTotal,
    long Success2xx, long NotFound404, long Retryable429Or5xx,
    long OtherHttp, long TransportErrors, long Canceled,
    long DurationMsTotal, long DurationMsMax);

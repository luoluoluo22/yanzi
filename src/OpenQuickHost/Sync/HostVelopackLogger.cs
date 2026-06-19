using System;
using Microsoft.Extensions.Logging;

namespace OpenQuickHost.Sync;

public class HostVelopackLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var logLine = $"[Velopack {logLevel}] {message}";
        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        HostAssets.AppendLog(logLine);
    }
}

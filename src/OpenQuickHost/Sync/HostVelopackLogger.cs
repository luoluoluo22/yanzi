using System;
using Velopack.Logging;

namespace OpenQuickHost.Sync;

public class HostVelopackLogger : IVelopackLogger
{
    public void Log(VelopackLogLevel level, string? message, Exception? exception)
    {
        var logLine = $"[Velopack {level}] {message ?? string.Empty}";
        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        HostAssets.AppendLog(logLine);
    }
}

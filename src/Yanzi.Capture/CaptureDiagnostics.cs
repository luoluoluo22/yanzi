using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Yanzi.Capture;

public static class CaptureDiagnostics
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Sync = new();
    private static string _runId = Guid.NewGuid().ToString("N")[..8];
    private static string _logPath = string.Empty;
    private static StreamWriter? _writer;

    public static string LogPath => _logPath;

    public static void Initialize(string[] args)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost",
                "Logs");
            Directory.CreateDirectory(root);
            _logPath = Path.Combine(root, "yanzi-capture-perf.log");

            RotateIfNeeded(_logPath);

            var stream = new FileStream(
                _logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                FileOptions.SequentialScan);

            _writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024)
            {
                AutoFlush = false
            };

            Mark("process.start", ("args", string.Join(" ", args)));
        }
        catch
        {
            // Diagnostics must never break capture.
        }
    }

    public static IDisposable Measure(
        string name,
        params (string Key, object? Value)[] data)
    {
        Mark(name + ".begin", data);
        return new Scope(name);
    }

    public static void Mark(
        string name,
        params (string Key, object? Value)[] data)
    {
        if (_writer is null) return;

        try
        {
            using var process = Process.GetCurrentProcess();
            var fields = new List<string>
            {
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                "run=" + _runId,
                "pid=" + Environment.ProcessId,
                "t=" + Clock.Elapsed.TotalMilliseconds.ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + "ms",
                "event=" + name,
                "ws=" + (process.WorkingSet64 / 1024d / 1024d).ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + "MB",
                "private=" + (process.PrivateMemorySize64 / 1024d / 1024d).ToString(
                    "F1",
                    CultureInfo.InvariantCulture) + "MB",
                "gc=" + GC.GetTotalMemory(false) / 1024 / 1024 + "MB"
            };

            foreach (var (key, value) in data)
                fields.Add(key + "=" + Sanitize(value));

            lock (Sync)
            {
                _writer.WriteLine(string.Join(" | ", fields));
                if (ShouldFlush(name))
                    _writer.Flush();
            }
        }
        catch
        {
            // Diagnostics must never break capture.
        }
    }

    private static bool ShouldFlush(string eventName) =>
        eventName == "process.start" ||
        eventName.EndsWith("contentRendered", StringComparison.Ordinal) ||
        eventName == "ocr.end" ||
        eventName == "clipboard.image.failed" ||
        eventName == "app.run.end" ||
        eventName.EndsWith(".closed", StringComparison.Ordinal);

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
                return;

            var backup = path + ".1";
            File.Move(path, backup, overwrite: true);
        }
        catch
        {
            // Rotation failure should not affect capture.
        }
    }

    public static void Shutdown()
    {
        CloseWriter();
    }

    private static void CloseWriter()
    {
        lock (Sync)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
            }
            finally
            {
                _writer = null;
            }
        }
    }

    private static string Sanitize(object? value)
    {
        if (value is null) return "null";
        return value.ToString()!
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "/");
    }

    private sealed class Scope(string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var elapsed = Stopwatch.GetElapsedTime(_start);
            Mark(
                name + ".end",
                ("durationMs", elapsed.TotalMilliseconds.ToString(
                    "F2",
                    CultureInfo.InvariantCulture)));
        }
    }
}

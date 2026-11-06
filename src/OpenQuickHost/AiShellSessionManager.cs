using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

// Owned by ONE agent turn. Session IDs from other topics/turns never resolve.
// This is process lifecycle containment, not a filesystem/network sandbox.
internal sealed class AiShellSessionManager(string defaultWorkdir) : IAsyncDisposable
{
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1);
    private bool disposed;

    public async Task<string> InvokeAsync(string name, JsonElement args, CancellationToken cancellationToken,
        Action<string>? onProgress = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be an object.");
            var limit = Integer(args, "max_output_chars", 16000, 256, 64000);
            var wait = Integer(args, "yield_time_ms", 1000, 0, 30000);
            Session session;
            if (name is "exec_command" or "execute_command")
            {
                var allowed = new[] { "tool", "cmd", "command", "workdir", "timeout_ms", "yield_time_ms", "max_output_chars" };
                ValidateKeys(args, allowed);
                if (sessions.Count >= 4) throw new ArgumentException("Session limit reached; finish or terminate an existing session first.");
                var command = Text(args, name == "execute_command" ? "command" : "cmd", required: true)!;
                if (command.Length > 10000) throw new ArgumentException("Command exceeds 10000 characters; use a script file.");
                var workdir = Text(args, "workdir") ?? defaultWorkdir;
                workdir = Path.GetFullPath(workdir, Path.GetFullPath(defaultWorkdir));
                if (!Directory.Exists(workdir)) throw new ArgumentException("Working directory does not exist.");
                var timeout = Integer(args, "timeout_ms", 120000, 100, 600000);
                session = new Session(command, workdir, timeout);
                sessions.Add(session.Id, session);
            }
            else
            {
                ValidateKeys(args, ["tool", "session_id", "chars", "yield_time_ms", "max_output_chars"]);
                var id = Text(args, "session_id", required: true)!;
                if (!sessions.TryGetValue(id, out session!)) throw new ArgumentException("Session not found in this turn (expired or belongs to another conversation).");
                if (name == "terminate_command") session.Stop("terminated");
                else if (name == "write_stdin")
                {
                    var chars = Text(args, "chars") ?? "";
                    if (chars.Length > 16000) throw new ArgumentException("stdin exceeds 16000 characters.");
                    if (chars.Length != 0) await session.WriteAsync(chars, cancellationToken);
                }
                else throw new ArgumentException("Unknown shell tool.");
            }
            using var registration = cancellationToken.Register(() => session.Stop("cancelled"));
            var waiting = Stopwatch.StartNew();
            do
            {
                var remaining = Math.Max(0, wait - (int)waiting.ElapsedMilliseconds);
                await Task.WhenAny(session.Completion, Task.Delay(Math.Min(200, remaining), cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (!session.Completion.IsCompleted && onProgress != null)
                    onProgress(JsonSerializer.Serialize(session.Snapshot(limit, drain: false).Value));
            } while (!session.Completion.IsCompleted && waiting.ElapsedMilliseconds < wait);
            cancellationToken.ThrowIfCancellationRequested();
            var result = session.Snapshot(limit);
            if (result.Completed)
            {
                sessions.Remove(session.Id);
                await session.DisposeAsync();
            }
            return JsonSerializer.Serialize(result.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return JsonSerializer.Serialize(new { status = "error", error = ex.Message });
        }
        finally { gate.Release(); }
    }

    private static void ValidateKeys(JsonElement args, string[] allowed)
    {
        foreach (var p in args.EnumerateObject())
            if (!allowed.Contains(p.Name, StringComparer.Ordinal)) throw new ArgumentException("Unsupported argument: " + p.Name);
    }
    private static string? Text(JsonElement args, string key, bool required = false)
    {
        if (!args.TryGetProperty(key, out var value))
        {
            if (required) throw new ArgumentException("Missing argument: " + key);
            return null;
        }
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException(key + " must be a string.");
        var text = value.GetString();
        if (required && string.IsNullOrWhiteSpace(text)) throw new ArgumentException(key + " cannot be empty.");
        return text;
    }
    private static int Integer(JsonElement args, string key, int fallback, int min, int max)
    {
        if (!args.TryGetProperty(key, out var value)) return fallback;
        if (!value.TryGetInt32(out var number) || number < min || number > max)
            throw new ArgumentException($"{key} must be an integer between {min} and {max}.");
        return number;
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            disposed = true;
            foreach (var session in sessions.Values) session.Stop("turn_finished");
            foreach (var session in sessions.Values) await session.DisposeAsync();
            sessions.Clear();
        }
        finally { gate.Release(); }
    }

    private sealed class Session : IAsyncDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        private readonly Process process;
        private readonly AiShellJob job;
        private readonly EventWaitHandle launchGate;
        private readonly EventWaitHandle inputReadyGate;
        private readonly OutputBuffer stdout = new(), stderr = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource deadline;
        private readonly CancellationTokenRegistration timeoutRegistration;
        private readonly string workdir;
        private string? stopReason;
        private int? exitCode;
        public Task Completion { get; }

        public Session(string command, string workdir, int timeoutMs)
        {
            this.workdir = workdir;
            // Wait for the parent's handshake BEFORE executing any model code;
            // this closes the race between process creation and job assignment.
            // Configure stdin encoding before the handshake. Resetting the reader
            // after a buffered read can lose a fast follow-up write_stdin call.
            // Readiness travels over a named event, NEVER the same stdin pipe
            // used by write_stdin. Fast user input must remain untouched.
            launchGate = new EventWaitHandle(false, EventResetMode.ManualReset,
                @"Local\YanziAiShell_" + Id);
            inputReadyGate = new EventWaitHandle(false, EventResetMode.ManualReset,
                @"Local\YanziAiShellReady_" + Id);
            var script =
                "$ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Stop';\n" +
                "[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); " +
                "[Console]::InputEncoding=New-Object System.Text.UTF8Encoding($false); $OutputEncoding=[Console]::OutputEncoding;\n" +
                "$gate=[System.Threading.EventWaitHandle]::OpenExisting('Local\\YanziAiShell_" + Id + "');" +
                "try { if (-not $gate.WaitOne(30000)) { throw 'shell_start_gate_timeout' } } finally { $gate.Dispose() };\n" +
                "$null=[Console]::ReadLine();\n" +
                "$ready=[System.Threading.EventWaitHandle]::OpenExisting('Local\\YanziAiShellReady_" + Id + "'); $null=$ready.Set(); $ready.Dispose();\n" +
                command;
            process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                WorkingDirectory = workdir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            }};
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-EncodedCommand");
            process.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            try
            {
                process.Start();
                job = new AiShellJob(process);
                _ = launchGate.Set();
                process.StandardInput.WriteLine("start");
                process.StandardInput.Flush();

            }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                job?.Dispose();
                launchGate.Dispose(); inputReadyGate.Dispose();
                process.Dispose();
                throw;
            }
            deadline = new CancellationTokenSource();
            timeoutRegistration = deadline.Token.Register(() => Stop("timed_out"));
            Completion = MonitorAsync();
            deadline.CancelAfter(timeoutMs);
        }
        private async Task MonitorAsync()
        {
            var readers = Task.WhenAll(PumpAsync(process.StandardOutput, stdout), PumpAsync(process.StandardError, stderr));
            try
            {
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;
            }
            finally
            {
                // Descendants that retain stdout handles must not stall EOF.
                job.Dispose();
                await readers;
                clock.Stop();
            }
        }
        private static async Task PumpAsync(StreamReader reader, OutputBuffer buffer)
        {
            var chars = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(chars.AsMemory())) != 0) buffer.Append(chars, count);
            }
            catch (IOException ex) { buffer.Append(ex.Message.ToCharArray(), ex.Message.Length); }
        }
        public async Task WriteAsync(string chars, CancellationToken token)
        {
            if (Completion.IsCompleted) throw new ArgumentException("Session has already exited; poll without chars to collect its final output.");
            // Wait until the PowerShell engine has initialized redirected stdin;
            // input written before -EncodedCommand starts can be silently lost.
            var ready = Task.Run(() => inputReadyGate.WaitOne(12000), token);
            if (await Task.WhenAny(ready, Completion) != ready || !await ready)
                throw new IOException("PowerShell did not become ready to accept stdin.");
            await process.StandardInput.WriteAsync(chars.AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
        }
        public void Stop(string reason)
        {
            if (Completion?.IsCompleted == true) return;
            Interlocked.CompareExchange(ref stopReason, reason, null);
            job.Dispose();
        }
        public (object Value, bool Completed) Snapshot(int limit, bool drain = true)
        {
            // Capture completion BEFORE draining. If the readers complete while
            // we drain, retain the session for a final poll instead of losing
            // the last bytes or reporting a premature completed state.
            var completed = Completion.IsCompleted;
            var (output, outDropped) = stdout.Read(limit / 2, drain);
            var (error, errDropped) = stderr.Read(limit / 2, drain);
            return (new
            {
                session_id = completed ? null : Id,
                status = completed ? stopReason ?? "completed" : "running",
                exit_code = completed ? exitCode : null,
                stdout = output, stderr = error,
                truncated = outDropped + errDropped > 0,
                omitted_chars = outDropped + errDropped,
                wall_time_seconds = clock.Elapsed.TotalSeconds,
                workdir, shell = "powershell", sandbox = "none"
            }, completed);
        }
        public async ValueTask DisposeAsync()
        {
            Stop("disposed");
            await Completion;
            await timeoutRegistration.DisposeAsync();
            deadline.Dispose();
            launchGate.Dispose(); inputReadyGate.Dispose();
            process.Dispose();
        }
    }
    private sealed class OutputBuffer
    {
        private readonly StringBuilder text = new();
        private long dropped;
        public void Append(char[] chars, int count)
        {
            lock (text)
            {
                text.Append(chars, 0, count);
                if (text.Length > 64000)
                {
                    var excess = text.Length - 64000;
                    text.Remove(0, excess);
                    dropped += excess;
                }
            }
        }
        public (string, long) Read(int limit, bool drain)
        {
            lock (text)
            {
                const string marker = "\n[output truncated]\n";
                var retained = text.Length <= limit ? text.Length : limit - marker.Length;
                var head = retained / 2;
                var tail = retained - head;
                var result = text.Length <= limit ? text.ToString() : text.ToString(0, head) +
                    marker + text.ToString(text.Length - tail, tail);
                var omitted = dropped + text.Length - retained;
                if (drain) { text.Clear(); dropped = 0; }
                return (result, omitted);
            }
        }
    }
}

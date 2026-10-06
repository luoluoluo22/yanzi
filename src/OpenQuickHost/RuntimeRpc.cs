using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>Versioned, bounded IPC restricted to the current Windows user.</summary>
public static class RuntimeRpc
{
    public const int Version = 1;
    private const int MaxMessageBytes = 8 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string PipeName { get; } = ResolvePipeName();

    private static string ResolvePipeName()
    {
        var verification = Environment.GetEnvironmentVariable("YANZI_RUNTIME_PIPE");
        if (!string.IsNullOrEmpty(verification))
        {
            if (verification.Length > 120 || verification.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_'))
                throw new ArgumentException("Runtime 管道名称无效。");
            return verification;
        }
        return "Yanzi.Runtime.v1." + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];
    }

    public static async Task<JsonElement> CallAsync(string operation, object? payload = null,
        CancellationToken cancellationToken = default, string? pipeName = null, TimeSpan? requestTimeout = null)
    {
        var serializedPayload = JsonSerializer.SerializeToElement(payload ?? new { }, Json);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout ?? DefaultRequestTimeout(operation, serializedPayload));
        using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, timeout.Token).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        await WriteAsync(pipe, new RuntimeRpcRequest(Version, id, operation,
            serializedPayload), timeout.Token).ConfigureAwait(false);
        var reply = await ReadAsync<RuntimeRpcReply>(pipe, timeout.Token).ConfigureAwait(false);
        if (reply.Version != Version || reply.Id != id) throw new InvalidDataException("Runtime 协议或请求标识不匹配。");
        if (!reply.Success) throw new InvalidOperationException(reply.Error ?? "Runtime 请求失败。");
        return reply.Data;
    }

    internal static TimeSpan DefaultRequestTimeout(string operation, JsonElement payload)
        => operation == "capability.invoke" && payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
            (string.Equals(name.GetString()?.Trim(), "chat.send", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name.GetString()?.Trim(), "chat.status", StringComparison.OrdinalIgnoreCase))
            ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(45);

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > MaxMessageBytes) throw new InvalidDataException("Runtime 消息超过限制。");
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(body, Json) ?? throw new InvalidDataException("Runtime 消息为空。");
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (body.Length > MaxMessageBytes) throw new InvalidDataException("Runtime 消息超过限制。");
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record RuntimeRpcRequest(int Version, string Id, string Operation, JsonElement Payload);
internal sealed record RuntimeRpcReply(int Version, string Id, bool Success, JsonElement Data, string? Error);

public sealed class RuntimeRpcServer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _pipeName;
    private readonly Func<string, JsonElement, Task<object?>> _handler;
    private Task? _loop;
    public RuntimeRpcServer(string pipeName, Func<string, JsonElement, Task<object?>> handler)
        => (_pipeName, _handler) = (pipeName, handler);

    public void Start() => _loop ??= Task.Run(AcceptAsync);

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 16,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                _ = ServeAsync(pipe);
                pipe = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                HostAssets.AppendLog("Runtime IPC accept failed: " + ex.Message);
                try { await Task.Delay(300, _stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                var request = await RuntimeRpc.ReadAsync<RuntimeRpcRequest>(pipe, timeout.Token).ConfigureAwait(false);
                timeout.CancelAfter(RuntimeRpc.DefaultRequestTimeout(request.Operation, request.Payload));
                RuntimeRpcReply reply;
                try
                {
                    if (request.Version != RuntimeRpc.Version) throw new InvalidDataException("不支持的 Runtime 协议版本。");
                    var data = await _handler(request.Operation, request.Payload).WaitAsync(timeout.Token).ConfigureAwait(false);
                    reply = new(RuntimeRpc.Version, request.Id, true, JsonSerializer.SerializeToElement(data, RuntimeRpc.Json), null);
                }
                catch (Exception ex)
                {
                    reply = new(RuntimeRpc.Version, request.Id, false, JsonSerializer.SerializeToElement<object?>(null), ex.Message);
                }
                await RuntimeRpc.WriteAsync(pipe, reply, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or InvalidDataException)
            {
                // A client can disappear mid-request; that must not stop the Runtime.
                HostAssets.AppendLog("Runtime IPC client disconnected: " + ex.GetType().Name);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // Do not block the UI dispatcher on requests awaiting that dispatcher.
    }
}

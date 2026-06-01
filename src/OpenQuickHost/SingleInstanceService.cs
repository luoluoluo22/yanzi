using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace OpenQuickHost;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public SingleInstanceService(string appId)
    {
        _mutexName = $@"Global\{appId}.Singleton";
        _pipeName = $"{appId}.Pipe";
    }

    public bool TryAcquirePrimaryInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
    }

    public void StartServer(Func<string, Task> onMessageAsync)
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(onMessageAsync, _cts.Token));
    }

    public async Task<bool> SendToPrimaryInstanceAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(200, cancellationToken);
            await using var writer = new StreamWriter(client);
            await writer.WriteAsync(message);
            await writer.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenLoopAsync(Func<string, Task> onMessageAsync, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var message = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    await onMessageAsync(message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Single instance pipe error: {ex.Message}");
                await Task.Delay(300, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                _listenTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Ignore pipe shutdown errors.
            }

            _cts.Dispose();
            _cts = null;
        }

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore disposal edge cases.
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}

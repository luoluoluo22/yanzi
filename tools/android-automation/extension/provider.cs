using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;

// An extension-owned capability lease. No arbitrary ADB command or checkout API.
public static class YanziAction
{
    private static readonly SemaphoreSlim Serial = new(1, 1);

    public static async Task<string> RunAsync(YanziActionContext context)
    {
      try {
        var key = context.ExtensionId + "-service";
        if (context.GetRegisteredObject?.Invoke(key) is AndroidShoppingService previous)
        {
            previous.Quit();
            await Task.Delay(300);
        }
        var service = new AndroidShoppingService();
        context.RegisterObject?.Invoke(key, service);
        context.Capabilities.Register("android.device.status", args => Execute(context, "android.device.status", args));
        context.Capabilities.Register("pdd.product.search", args => Execute(context, "pdd.product.search", args));
        context.Capabilities.Register("pdd.cart.inspect", args => Execute(context, "pdd.cart.inspect", args));
        context.Capabilities.Register("pdd.orders.preview", args => Execute(context, "pdd.orders.preview", args));
        context.Capabilities.Register("pdd.orders.collect", args => Execute(context, "pdd.orders.collect", args));
        await service.Stopped.Task;
        return "手机购物助手已停止";
      } catch (Exception ex) {
        File.WriteAllText(Path.Combine(context.ExtensionDirectory, "startup-error.log"), ex.ToString());
        throw;
      }
    }

    private static async Task<object?> Execute(YanziActionContext context, string operation, JsonElement payload)
    {
        var entry = Path.Combine(context.ExtensionDirectory, "automation", "src", "capability-cli.mjs");
        if (!File.Exists(entry)) throw new FileNotFoundException("手机自动化模块未安装");
        await Serial.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var processInfo = new ProcessStartInfo
            {
                FileName = "node",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(entry)!
            };
            processInfo.ArgumentList.Add(entry);
            processInfo.ArgumentList.Add(operation);
            processInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.GetRawText())));
            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Node 进程启动失败");
            try
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(timeout.Token);
                var response = await output;
                _ = await error;
                if (response.Length > 100000) throw new InvalidOperationException("手机操作输出过大");
                using var parsed = JsonDocument.Parse(response);
                var result = parsed.RootElement;
                if (!result.TryGetProperty("ok", out var flag) || flag.ValueKind != JsonValueKind.True)
                    throw new InvalidOperationException(
                        result.TryGetProperty("error", out var message) ? message.GetString() : "手机操作失败");
                return result.Clone();
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new TimeoutException("手机操作超时，请先确认手机当前页面后再决定是否继续");
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(context.ExtensionDirectory, "capability-error.log"),
                operation + " " + ex.GetType().Name + ": " + ex.Message);
            throw;
        }
        finally { Serial.Release(); }
    }
}

public sealed class AndroidShoppingService
{
    public readonly TaskCompletionSource<bool> Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsRunning => !Stopped.Task.IsCompleted;
    public bool RetainAfterOnDemand => true;
    public void Quit() => Stopped.TrySetResult(true);
}

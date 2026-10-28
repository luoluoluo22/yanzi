using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziCapabilityInvocationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AsyncLocal<string[]?> InvocationPath = new();

    public static async Task<YanziCapabilityInvocationResult> InvokeAsync(string capabilityName, object? payload = null,
        YanziCapabilityCaller? caller = null)
    {
        using var trace = YanziOperationTrace.Push();
        caller ??= YanziCapabilityCaller.Anonymous;
        capabilityName = capabilityName?.Trim() ?? string.Empty;
        var previousPath = InvocationPath.Value ?? Array.Empty<string>();
        string provider = "unknown";
        IAsyncDisposable? providerLease = null;

        try
        {
            if (string.IsNullOrWhiteSpace(capabilityName))
                throw new ArgumentException("能力名称不能为空");
            if (previousPath.Length >= 16 || previousPath.Contains(capabilityName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"能力调用存在循环或超过嵌套上限：{string.Join(" -> ", previousPath.Append(capabilityName))}");

            if (!YanziCapabilityRegistry.Contains(capabilityName))
            {
                try
                {
                    providerLease = await YanziFileWorkflow.AcquireProviderAsync(capabilityName);
                }
                catch (IOException ex) when (ex.Message.StartsWith("workflow_provider_not_installed", StringComparison.Ordinal))
                {
                    throw new KeyNotFoundException($"未知燕子能力：{capabilityName}", ex);
                }
            }

            if (!YanziCapabilityRegistry.Contains(capabilityName))
                throw new KeyNotFoundException($"能力提供者未能注册：{capabilityName}");

            provider = FindProvider(capabilityName);
            InvocationPath.Value = previousPath.Append(capabilityName).ToArray();
            var result = await YanziCapabilityRegistry.InvokeAsync(capabilityName, payload, caller);
            YanziCapabilityCallLog.Add(capabilityName, provider, true, caller.Id);
            return YanziCapabilityInvocationResult.Ok(result, YanziOperationTrace.Current);
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                UnauthorizedAccessException => "permission_denied",
                KeyNotFoundException => "capability_not_found",
                ArgumentException or JsonException => "invalid_parameters",
                _ => "invocation_failed"
            };
            YanziCapabilityCallLog.Add(capabilityName, provider, false, caller.Id, code);
            return YanziCapabilityInvocationResult.Failed(ex.Message, code, YanziOperationTrace.Current);
        }
        finally
        {
            InvocationPath.Value = previousPath;
            if (providerLease != null)
            {
                try { await providerLease.DisposeAsync(); }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Capability provider release failed: {capabilityName}: {ex.Message}");
                }
            }
        }
    }

    private static string FindProvider(string capabilityName)
        => YanziCapabilityRegistry.TryGet(capabilityName, out var definition)
            ? definition?.ProviderExtensionId ?? "unknown"
            : "unknown";

    public static async Task<string> InvokeJsonAsync(string requestJson, YanziCapabilityCaller? caller = null)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CapabilityInvokeRequest>(requestJson, JsonOptions);
            if (request == null)
                return JsonSerializer.Serialize(YanziCapabilityInvocationResult.Failed("请求为空", "invalid_request"), JsonOptions);

            using var trace = YanziOperationTrace.Push(request.TraceId);
            var result = await InvokeAsync(request.Name, request.Payload, caller);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(YanziCapabilityInvocationResult.Failed(ex.Message, "invalid_request"), JsonOptions);
        }
    }
}

public sealed class CapabilityInvokeRequest
{
    public string Name { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public string? TraceId { get; set; }
}

public sealed class YanziCapabilityInvocationResult
{
    public string? TraceId { get; init; }
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static YanziCapabilityInvocationResult Ok(object? data, string? traceId = null)
        => new() { Success = true, Data = data, TraceId = traceId };

    public static YanziCapabilityInvocationResult Failed(string error, string errorCode = "invocation_failed", string? traceId = null)
        => new() { Success = false, Error = error, ErrorCode = errorCode, TraceId = traceId };
}

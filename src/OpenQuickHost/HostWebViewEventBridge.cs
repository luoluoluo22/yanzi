using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace OpenQuickHost;

/// <summary>
/// WebView2 host-event contract. All inputs are JSON-encoded, event dispatch does
/// not activate a window, and shutdown returns true only after the Promise settles.
/// </summary>
public static class HostWebViewEventBridge
{
    public static string EventNameForLaunchSource(string source) => source switch
    {
        "idle" => "host.idle",
        "scheduler" => "host.schedule",
        "recovery" => "host.recover",
        "host.resume" or "host.unlock" or "network.online" => source,
        _ => string.Empty
    };

    public static bool IsBackgroundLaunchSource(string source) =>
        source is "app-startup" or "idle" or "scheduler" or
            "host.resume" or "host.unlock" or "network.online" or "recovery";

    public static string BuildEventExpression(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 80)
            throw new ArgumentException("Invalid host event", nameof(eventName));
        var name = JsonSerializer.Serialize(eventName);
        return "(async()=>{const detail={eventName:" + name +
            ",occurredUtc:new Date().toISOString()};" +
            "window.dispatchEvent(new CustomEvent('yanzi:host-event',{detail}));" +
            "if(typeof window.yanzi?.onHostEvent==='function')" +
            "await window.yanzi.onHostEvent(detail);return true;})()";
    }

    public const string ExitExpression =
        "(async()=>{if(typeof window.yanzi?.beforeExit!=='function')return false;" +
        "const saved=await window.yanzi.beforeExit();return saved!==false;})()";

    public static async Task<bool> EvaluateAsync(CoreWebView2 webView, string expression,
        TimeSpan timeout, CancellationToken token = default)
    {
        var json = await webView.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }))
            .WaitAsync(timeout, token);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out _)) return false;
        if (!doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("value", out var value)) return false;
        return value.ValueKind == JsonValueKind.True;
    }

    public static Task<bool> NotifyAsync(CoreWebView2 view, string eventName,
        CancellationToken token = default) =>
        EvaluateAsync(view, BuildEventExpression(eventName), TimeSpan.FromSeconds(8), token);

    public static Task<bool> PrepareForExitAsync(CoreWebView2 view, TimeSpan timeout,
        CancellationToken token = default) =>
        EvaluateAsync(view, ExitExpression, timeout, token);
}

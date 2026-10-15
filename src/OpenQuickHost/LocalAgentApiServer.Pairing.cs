using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private async Task<bool> TryPairingApiAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path != "/pair" && !path.StartsWith("/v1/me/devices/pairs", StringComparison.Ordinal)) return false;
        if (request.RemoteEndPoint is not { } remote || !IPAddress.IsLoopback(remote.Address))
        { await WriteJsonAsync(response, 403, new {error = "local_pairing_only"}); return true; }
        if (request.HttpMethod != "GET" && request.Headers["Origin"] is { } origin &&
            (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
                !(originUri.Host == "localhost" || IPAddress.TryParse(originUri.Host, out var originIp) && IPAddress.IsLoopback(originIp)) || originUri.Port != request.LocalEndPoint.Port))
        { await WriteJsonAsync(response, 403, new {error = "pairing_origin_denied"}); return true; }
        if (path == "/pair" && request.HttpMethod == "GET")
        {
            const string html = """
<!doctype html><html lang="zh"><meta charset="utf-8"><title>燕子同账号设备连接</title>
<style>body{font:16px system-ui;max-width:800px;margin:40px auto;padding:20px}button{font:inherit;padding:8px}</style>
<h1>同账号设备连接</h1><p>手机和电脑登录同一个燕子账号后自动连接。在同一局域网时优先直连，无需配对码或复制密钥。</p>
<p>连接信息在后台自动更新，聊天、文件和远程操作沿用同一设备连接。</p><button onclick="location.reload()">刷新状态</button><div id="list"></div>
<script>fetch('/v1/me/devices/pairs',{credentials:'same-origin'}).then(r=>r.json()).then(j=>{for(const p of j.items){const row=document.createElement('p');row.textContent=p.displayName+' · 已连接';list.append(row)}if(!j.items.length)list.textContent='登录同一账号后，连接会自动建立。';});</script></html>
""";
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes); response.Close(); return true;
        }
        if (path == "/v1/me/devices/pairs" && request.HttpMethod == "GET")
        {
            await WriteJsonAsync(response, 200, new { items = YanziLanPairing.List().Select(x => new {
                pairId = x.PairId, deviceId = x.DeviceId, displayName = x.DisplayName, scopes = x.Scopes, expiresAt = x.ExpiresAt }) });
            return true;
        }
        if (path == "/v1/me/devices/pairs" && request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream);
            using var document = JsonDocument.Parse(await reader.ReadToEndAsync());
            var root = document.RootElement;
            var scopes = root.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()!).Distinct().ToArray();
            await WriteJsonAsync(response, 200, YanziLanPairing.Create(root.GetProperty("deviceId").GetString()!,
                root.GetProperty("displayName").GetString() ?? "", scopes, request.LocalEndPoint.Port));
            return true;
        }
        if (path.StartsWith("/v1/me/devices/pairs/", StringComparison.Ordinal) && request.HttpMethod == "DELETE")
        {
            YanziLanPairing.Revoke(path["/v1/me/devices/pairs/".Length..]);
            await WriteJsonAsync(response, 200, new {ok = true}); return true;
        }
        await WriteJsonAsync(response, 405, new {error = "method_not_allowed"}); return true;
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziLanMobileTransfer
{
    public static async Task SendAsync(IPAddress ip, int port, string token, string filePath, string kind, string id)
    {
        var file = new FileInfo(filePath);
        if (file.Length <= 0 || file.Length > 30L * 1024 * 1024) throw new IOException("文件大小需为 1 字节至 30 MB。");
        string hash;
        using (var stream = File.OpenRead(filePath)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        using var handler = new SecureLanHttpHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        if (file.Length >= 2L * 1048576)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { await SendChunksAsync(client, $"http://{ip}:{port}", file, kind, id, hash); break; }
                catch (HttpRequestException error) when (attempt < 2 && (error.StatusCode == null || (int)error.StatusCode >= 500 || (int)error.StatusCode == 429))
                { await Task.Delay(250 * (attempt + 1)); }
            }
            return;
        }
        using var input = File.OpenRead(filePath);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"http://{ip}:{port}/v1/lan/transfers/{id}?kind={kind}&name={Uri.EscapeDataString(file.Name)}");
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("X-Content-Sha256", hash);
        request.Content = new StreamContent(input, 65536);
        request.Content.Headers.ContentLength = file.Length;
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!result.RootElement.GetProperty("success").GetBoolean()) throw new IOException("手机未确认文件接收。");
        HostAssets.AppendLog($"Desktop LAN file delivered: id={id}, kind={kind}, bytes={file.Length}");
    }
    private static async Task SendChunksAsync(HttpClient client, string baseUrl, FileInfo file, string kind, string id, string hash)
    {
        var hashes = new List<string>();
        using (var input = File.OpenRead(file.FullName))
        {
            var bytes = new byte[1048576]; int read;
            while ((read = await ReadBlockAsync(input, bytes)) > 0) hashes.Add(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, read))).ToLowerInvariant());
        }
        var path = baseUrl + "/v1/lan/transfer-sessions/" + id;
        using var manifest = new StringContent(JsonSerializer.Serialize(new {id, sourceDeviceId = Sync.DeviceIdentityStore.GetOrCreateDesktopDeviceId(), name = file.Name, kind,
            size = file.Length, sha256 = hash, blockHashes = hashes}), System.Text.Encoding.UTF8, "application/json");
        using var begin = await client.PostAsync(path, manifest);
        begin.EnsureSuccessStatusCode();
        using var state = JsonDocument.Parse(await begin.Content.ReadAsStringAsync());
        if (state.RootElement.GetProperty("state").GetString() == "completed")
        {
            using var previous = JsonDocument.Parse(state.RootElement.GetProperty("result").GetString()!);
            if (!previous.RootElement.GetProperty("success").GetBoolean()) throw new IOException("transfer_processing_failed");
            return;
        }
        long uploaded = 0;
        using (var input = File.OpenRead(file.FullName))
        foreach (var missing in state.RootElement.GetProperty("missingBlocks").EnumerateArray())
        {
            int index = missing.GetInt32();
            var bytes = new byte[(int)Math.Min(1048576, file.Length - index * 1048576L)];
            input.Position = index * 1048576L; await input.ReadExactlyAsync(bytes);
            using var content = new ByteArrayContent(bytes);
            using var saved = await client.PutAsync(path + "/blocks/" + index, content); saved.EnsureSuccessStatusCode(); uploaded += bytes.Length;
        }
        using var commitBody = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var commit = await client.PostAsync(path + "/commit", commitBody); commit.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await commit.Content.ReadAsStringAsync());
        if (!result.RootElement.GetProperty("success").GetBoolean()) throw new IOException("transfer_processing_failed");
        HostAssets.AppendLog($"Desktop LAN chunks delivered: id={id}, uploadedBytes={uploaded}, bytes={file.Length}");
    }
    private static async Task<int> ReadBlockAsync(Stream input, byte[] bytes)
    {
        int count = 0, read;
        while (count < bytes.Length && (read = await input.ReadAsync(bytes.AsMemory(count))) > 0) count += read;
        return count;
    }
}

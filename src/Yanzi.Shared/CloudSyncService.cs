using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Yanzi.Shared;

public sealed class CloudSyncService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://sync.luoluoluo.cc.cd"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static string? CurrentAuthToken { get; set; }
    public static string? CurrentUserEmail { get; set; }

    public static async Task<bool> SendVerificationCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { email });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("/v1/auth/email/send-code", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Success, string? Token, string? Error)> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { email, code });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync("/v1/auth/email/verify-code", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, null, errorText);
            }

            var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                CurrentAuthToken = token;
                CurrentUserEmail = email;
                return (true, token, null);
            }

            return (false, null, "未能从响应中解析授权凭证");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static async Task<(bool Success, string? Message)> UploadBackupAsync(string configPayloadJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(CurrentAuthToken))
        {
            return (false, "未登录云端账号，请先在设置中登录。");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/sync/objects");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CurrentAuthToken);
            request.Content = new StringContent(configPayloadJson, Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "云端备份成功！");
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"备份失败: {err}");
        }
        catch (Exception ex)
        {
            return (false, $"网络请求异常: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string? Data, string? Error)> DownloadBackupAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(CurrentAuthToken))
        {
            return (false, null, "未登录云端账号");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sync/objects");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CurrentAuthToken);

            var response = await HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return (true, content, null);
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, null, err);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}

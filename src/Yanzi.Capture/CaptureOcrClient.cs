using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

/// <summary>
/// Routes screenshots to Yanzi's existing ocr.recognize capability (PP-OCRv6 Small).
/// Windows OCR is used only when the host capability fails, times out or is unavailable.
/// </summary>
public sealed class CaptureOcrClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private readonly string _baseUrl;
    private readonly string _token;
    private readonly string _dataDirectory;
    private readonly Action<string>? _log;

    public CaptureOcrClient(string baseUrl, string token, string dataDirectory, Action<string>? log = null)
    {
        _baseUrl = baseUrl;
        _token = token;
        _dataDirectory = dataDirectory;
        _log = log;
    }

    public async Task<CaptureOcrResult> RecognizeAsync(BitmapSource image)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var text = await RecognizeWithPaddleAsync(image);
            timer.Stop();
            CaptureDiagnostics.Mark("ocr.provider.success",
                ("engine", "paddle"), ("elapsedMs", timer.ElapsedMilliseconds.ToString()));
            return new CaptureOcrResult(text, "燕子 PaddleOCR", false, timer.Elapsed);
        }
        catch (Exception ex)
        {
            _log?.Invoke("燕子 PaddleOCR 调用失败，回退 Windows OCR：" + ex.GetType().Name + " - " + ex.Message);
            CaptureDiagnostics.Mark("ocr.provider.fallback", ("reason", ex.GetType().Name));
            var fallbackText = await WindowsOcrService.RecognizeAsync(image);
            timer.Stop();
            return new CaptureOcrResult(fallbackText, "Windows OCR（自动回退）", true, timer.Elapsed);
        }
    }

    // Exposed for repeatable, same-image OCR engine comparison.
    public async Task<string> RecognizeWithPaddleAsync(BitmapSource image)
    {
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri)
            || !baseUri.IsLoopback
            || baseUri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("燕子本机 Agent API 地址不可用，禁止向非本机地址上传截图。");

        var directory = Path.Combine(_dataDirectory, "ocr-tmp");
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "capture-" + Guid.NewGuid().ToString("N") + ".png");

        // BitmapSource is WPF thread-affine: encode before the first await.
        EditorWindow.SavePng(image, imagePath);
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/v1/capabilities/invoke"));
            if (!string.IsNullOrWhiteSpace(_token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    name = "ocr.recognize",
                    payload = new { imagePath, includeLines = false }
                }), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"燕子 OCR 接口返回 HTTP {(int)response.StatusCode}。");

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success)
                || success.ValueKind != JsonValueKind.True)
            {
                var error = root.TryGetProperty("error", out var reason)
                    ? reason.GetString()
                    : "未知错误";
                throw new InvalidOperationException("燕子 OCR 执行失败：" + error);
            }

            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("燕子 OCR 没有返回文本字段。");

            return text.GetString() ?? string.Empty;
        }
        finally
        {
            try { File.Delete(imagePath); } catch { }
        }
    }
}

public sealed record CaptureOcrResult(
    string Text, string Engine, bool UsedFallback, TimeSpan Elapsed);

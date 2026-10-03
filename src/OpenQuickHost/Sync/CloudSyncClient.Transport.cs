using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed partial class CloudSyncClient
{
    private static readonly HttpRequestOptionsKey<long> SessionGenerationKey = new("Yanzi.SessionGeneration");
    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, string body, bool includeAuth)
    {
        var request = CreateRequest(method, path, includeAuth);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool includeAuth)
    {
        var request = new HttpRequestMessage(method, path);
        request.Options.Set(SessionGenerationKey, AccountCoordinator.Generation);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeAuth && HasValidSession())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object body, bool includeAuth, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(method, path, JsonSerializer.Serialize(body), includeAuth);
        return await SendAsyncWithFallback(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithFallback(HttpMethod method, string path, bool includeAuth, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, includeAuth);
        return await SendAsyncWithFallback(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithFallback(HttpRequestMessage request, CancellationToken cancellationToken, bool largeTransfer = false)
    {
        ThrowIfTransportCoolingDown(cancellationToken);
        var generation = request.Options.TryGetValue(SessionGenerationKey, out var savedGeneration)
            ? savedGeneration : AccountCoordinator.Generation;
        AccountCoordinator.RequireCurrent(generation);
        Exception? lastError = null;
        var attempts = largeTransfer
            ? new (HttpClient client, string label)[]
            {
                (_largeTransferHttpClient, "proxy-large"),
                (_directLargeTransferHttpClient, "direct-large"),
                (_largeTransferHttpClient, "proxy-large-retry")
            }
            : new (HttpClient client, string label)[]
            {
                (_httpClient, "proxy"),
                (_directHttpClient, "direct"),
                (_httpClient, "proxy-retry")
            };
        var maxAttempts = IsIdempotentMethod(request.Method) ? attempts.Length : 1;

        for (var index = 0; index < maxAttempts; index++)
        {
            try
            {
                AccountCoordinator.RequireCurrent(generation);
                using var attemptRequest = await CloneRequestAsync(request, cancellationToken);
                var response = await attempts[index].client.SendAsync(attemptRequest, cancellationToken);
                try { AccountCoordinator.RequireCurrent(generation); }
                catch { response.Dispose(); throw; }
                ResetTransportBackoff();
                return response;
            }
            catch (Exception ex) when (AccountCoordinator.Generation == generation && IsRetryableTransportException(ex, cancellationToken) && index < maxAttempts - 1)
            {
                lastError = ex;
                CloudSyncDiagnostics.Log(
                    "CloudSyncClient.Http",
                    "Retryable request failure",
                    ("method", request.Method.Method),
                    ("uri", request.RequestUri?.ToString()),
                    ("attempt", index + 1),
                    ("channel", attempts[index].label),
                    ("error", ex.Message));
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (index + 1)), cancellationToken);
            }
            catch (Exception ex) when (AccountCoordinator.Generation == generation && IsRetryableTransportException(ex, cancellationToken))
            {
                lastError = ex;
                RegisterTransportFailure();
                break;
            }
        }

        CloudSyncDiagnostics.Log(
            "CloudSyncClient.Http",
            "Request failed after fallback",
            ("method", request.Method.Method),
            ("uri", request.RequestUri?.ToString()),
            ("error", lastError?.Message));
        throw lastError ?? new HttpRequestException("Cloud request failed before receiving a response.");
    }

    private static HttpClient CreateHttpClient(
        string baseUrl,
        bool useProxy,
        TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = useProxy
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YanziClient-Desktop", "0.2.3"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Yanzi-Client", "desktop");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Yanzi-Client-Version", "0.2.3");
        return client;
    }

    private static bool IsRetryableTransportException(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (ex is HttpRequestException httpException && httpException.StatusCode == null)
        {
            return true;
        }

        if (ex is OperationCanceledException)
        {
            return true; // HttpClient timeout, rather than caller cancellation.
        }

        var message = ex.ToString();
        return message.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("request was canceled", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIdempotentMethod(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head ||
        method == HttpMethod.Put || method == HttpMethod.Delete ||
        method == HttpMethod.Options;

    private void ThrowIfTransportCoolingDown(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_transportRetryLock)
        {
            if (DateTimeOffset.UtcNow < _transportRetryAfterUtc)
            {
                throw new HttpRequestException("网络连接暂时不可用，稍后自动重试。");
            }
        }
    }

    private void RegisterTransportFailure()
    {
        lock (_transportRetryLock)
        {
            _consecutiveTransportFailures = Math.Min(_consecutiveTransportFailures + 1, 8);
            if (_consecutiveTransportFailures < 2)
            {
                return;
            }

            var delaySeconds = Math.Min(15 * (1 << Math.Min(_consecutiveTransportFailures - 2, 5)), 300);
            _transportRetryAfterUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            CloudSyncDiagnostics.Log("CloudSyncClient.Http", "Transport retry cooldown", ("seconds", delaySeconds), ("failures", _consecutiveTransportFailures));
        }
    }

    private void ResetTransportBackoff()
    {
        lock (_transportRetryLock)
        {
            _consecutiveTransportFailures = 0;
            _transportRetryAfterUtc = DateTimeOffset.MinValue;
        }
    }

    public void ResumeTransportAfterNetworkChange() => ResetTransportBackoff();

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bytes = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content == null)
        {
            return clone;
        }

        var content = new ByteArrayContent(bytes!);
        foreach (var header in request.Content.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Content = content;
        return clone;
    }

}

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost;

// Long-lived, HTTP-only, same-origin browser sign-in for the local API console.
// Cookie is HMAC-signed with the existing native API token: no second secret,
// database, plain-text browser storage, or token embedded in public HTML.
public sealed partial class LocalAgentApiServer
{
    private const string ConsoleCookieName = "YanziLocalConsole";
    private static readonly TimeSpan ConsoleCookieLifetime = TimeSpan.FromDays(365);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private static bool IsConsoleOriginAllowed(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) &&
            !string.Equals(origin, request.Url?.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
            return false;
        var fetchSite = request.Headers["Sec-Fetch-Site"];
        return string.IsNullOrEmpty(fetchSite) ||
               string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase);
    }

    private byte[] SignConsoleSession(string data)
    {
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(_token),
            Encoding.UTF8.GetBytes("yanzi-console-v1|" + data));
    }

    private bool IsValidConsoleCookie(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token) || !IsConsoleOriginAllowed(request))
            return false;
        var cookie = request.Cookies[ConsoleCookieName]?.Value;
        if (string.IsNullOrWhiteSpace(cookie)) return false;
        var parts = cookie.Split('.');
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                out var expiresUnix) ||
            expiresUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            expiresUnix > DateTimeOffset.UtcNow.Add(ConsoleCookieLifetime)
                .AddMinutes(1).ToUnixTimeSeconds() ||
            parts[1].Length != 43)
            return false;
        try
        {
            var actual = FromBase64Url(parts[2]);
            var expected = SignConsoleSession(parts[0] + "." + parts[1]);
            return actual.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private void SetConsoleCookie(HttpListenerResponse response)
    {
        var expiry = DateTimeOffset.UtcNow.Add(ConsoleCookieLifetime);
        var payload = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
                      "." + Base64Url(RandomNumberGenerator.GetBytes(32));
        var cookieValue = payload + "." + Base64Url(SignConsoleSession(payload));
        response.AppendHeader("Set-Cookie", ConsoleCookieName + "=" + cookieValue +
            "; Path=/v1; Max-Age=31536000; HttpOnly; SameSite=Strict");
        response.Headers["Cache-Control"] = "no-store";
    }

    private static void ClearConsoleCookie(HttpListenerResponse response)
    {
        response.AppendHeader("Set-Cookie", ConsoleCookieName +
            "=; Path=/v1; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT" +
            "; HttpOnly; SameSite=Strict");
        response.Headers["Cache-Control"] = "no-store";
    }

    private async Task<bool> TryHandleConsoleLogoutAsync(
        HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path != "/v1/console/logout" || request.HttpMethod != "POST")
            return false;
        if (!IsConsoleOriginAllowed(request))
        {
            await WriteJsonAsync(response, 403, new { ok = false, error = "invalid_origin" });
            return true;
        }
        ClearConsoleCookie(response);
        await WriteJsonAsync(response, 200, new { ok = true, signedOut = true });
        return true;
    }

    private async Task<bool> TryHandleConsoleSessionApiAsync(
        HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path == "/v1/console/login" && request.HttpMethod == "POST")
        {
            if (!IsConsoleOriginAllowed(request) || string.IsNullOrWhiteSpace(_token) ||
                !string.Equals(request.Headers["X-Yanzi-Token"], _token,
                    StringComparison.Ordinal))
            {
                await WriteJsonAsync(response, 403, new { ok = false, error = "invalid_console_login" });
                return true;
            }
            SetConsoleCookie(response);
            await WriteJsonAsync(response, 200, new
            {
                ok = true, remembered = true, lifetimeDays = 365,
                authMode = "http_only_same_site_cookie"
            });
            return true;
        }
        if (path == "/v1/console/session" && request.HttpMethod == "GET")
        {
            var viaCookie = IsValidConsoleCookie(request);
            if (viaCookie) SetConsoleCookie(response); // sliding annual renewal on visiting console
            await WriteJsonAsync(response, 200, new
            {
                ok = true, authenticated = true, remembered = viaCookie,
                authRequired = !string.IsNullOrWhiteSpace(_token)
            });
            return true;
        }
        return false;
    }
}


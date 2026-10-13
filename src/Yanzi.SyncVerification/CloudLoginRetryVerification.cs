using System.Net;
using OpenQuickHost.Sync;

internal static class CloudLoginRetryVerification
{
    public static void Run()
    {
        static void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Cloud login retry regression: " + label);
        }

        var now = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
        var retry = new CloudLoginRetryPolicy();
        Check(!retry.IsCoolingDown(now), "fresh instance allows automatic sign-in");

        Check(retry.RecordFailure(null, null, now) == TimeSpan.FromSeconds(30), "first network failure");
        Check(retry.IsCoolingDown(now.AddSeconds(29)), "no retry every 10 seconds");
        Check(!retry.IsCoolingDown(now.AddSeconds(30)), "retry resumes on due time");
        Check(retry.RecordFailure(null, null, now.AddSeconds(30)) == TimeSpan.FromSeconds(60), "exponential growth");
        Check(retry.RecordFailure(HttpStatusCode.InternalServerError, null, now.AddSeconds(90)) == TimeSpan.FromSeconds(120), "server error increases cooldown");
        Check(retry.RecordFailure(null, null, now.AddSeconds(210)) == TimeSpan.FromSeconds(240), "fourth failure");
        Check(retry.RecordFailure(null, null, now.AddSeconds(450)) == TimeSpan.FromSeconds(300), "bounded max");
        Check(retry.RecordFailure(null, null, now.AddSeconds(750)) == TimeSpan.FromSeconds(300), "maximum remains bounded");

        retry.Reset();
        Check(!retry.IsCoolingDown(now), "manual login success resets background cooldown");
        Check(retry.RecordFailure(HttpStatusCode.Unauthorized, null, now) == TimeSpan.FromMinutes(10), "wrong credentials");
        Check(retry.IsCoolingDown(now.AddMinutes(9)), "401 must suppress background login");
        retry.Reset();

        Check(retry.RecordFailure(HttpStatusCode.Forbidden, null, now) == TimeSpan.FromMinutes(30), "WAF or VIP 403 pauses background");
        Check(retry.IsCoolingDown(now.AddMinutes(29)), "403 has lasting cooldown");
        retry.Reset();

        Check(retry.RecordFailure(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(125), now) == TimeSpan.FromSeconds(125), "honor Retry-After");
        retry.Reset();
        Check(retry.RecordFailure(HttpStatusCode.TooManyRequests, TimeSpan.FromHours(3), now) == TimeSpan.FromHours(1), "bounded Retry-After");
        retry.Reset();
        Check(retry.RecordFailure(HttpStatusCode.BadRequest, null, now) == TimeSpan.FromMinutes(10), "other client errors do not loop");
        retry.Reset();
        Check(retry.RecordFailure(HttpStatusCode.UpgradeRequired, null, now) == TimeSpan.FromMinutes(30), "legacy upgrade response");
        // A login POST must only switch transport after a handshake failure,
        // never after a timeout or an HTTP response that might have committed state.
        var fallbackMethod = typeof(CloudSyncClient).GetMethod("IsSafeAuthTransportFallback",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing safe login transport classifier");
        bool CanFallback(Exception e) => (bool)(fallbackMethod.Invoke(null, [e]) ?? false);
        Check(CanFallback(new HttpRequestException("TLS failure",
            new System.Security.Authentication.AuthenticationException("Handshake failed"))),
            "TLS handshake can use alternate path");
        Check(!CanFallback(new TaskCanceledException("Timed out")), "timeout must not resend POST");
        Check(!CanFallback(new HttpRequestException("response ended prematurely")), "partial reply must not resend POST");
        Check(!CanFallback(new HttpRequestException("HTTP blocked", null, HttpStatusCode.Forbidden)),
            "WAF 403 must not be bypassed by automatic POST replay");
        Console.WriteLine("CLOUD_LOGIN_RETRY_POLICY_PASSED");
    }
}

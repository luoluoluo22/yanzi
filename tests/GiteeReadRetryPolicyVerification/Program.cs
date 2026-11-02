using System.Net;
using System.Net.Http.Headers;
using OpenQuickHost.Sync;

static void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
Check(GiteeReadRetryPolicy.ExponentialDelay(1) == TimeSpan.FromMilliseconds(400) &&
      GiteeReadRetryPolicy.ExponentialDelay(2) == TimeSpan.FromMilliseconds(800) &&
      GiteeReadRetryPolicy.ExponentialDelay(3) == TimeSpan.FromMilliseconds(1600),
      "bounded exponential retry intervals");
Check(GiteeReadRetryPolicy.ExponentialDelay(1, 200) == TimeSpan.FromMilliseconds(600) &&
      GiteeReadRetryPolicy.ExponentialDelay(1, 999) == TimeSpan.FromMilliseconds(600),
      "jitter is capped at 200 ms");
using (var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests))
{
    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
    Check(GiteeReadRetryPolicy.ComputeDelay(response, TimeSpan.FromMilliseconds(400), now) ==
          TimeSpan.FromSeconds(5), "429 Retry-After delta is respected");
}
using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
{
    response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(12));
    Check(GiteeReadRetryPolicy.ComputeDelay(response, TimeSpan.FromMilliseconds(800), now) ==
          TimeSpan.FromSeconds(12), "503 Retry-After HTTP date is respected");
}
using (var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests))
{
    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
    Check(GiteeReadRetryPolicy.ComputeDelay(response, TimeSpan.FromMilliseconds(400), now) is null,
          "long server throttle returns response without premature retry");
}
using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
{
    Check(GiteeReadRetryPolicy.ComputeDelay(response, TimeSpan.FromMilliseconds(800), now) ==
          TimeSpan.FromMilliseconds(800), "missing Retry-After uses jittered backoff");
    response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(-3));
    Check(GiteeReadRetryPolicy.ComputeDelay(response, TimeSpan.FromMilliseconds(800), now) ==
          TimeSpan.FromMilliseconds(800), "expired Retry-After uses backoff");
}
Console.WriteLine("GITEE_RETRY_AFTER_POLICY_PASS cases=7");

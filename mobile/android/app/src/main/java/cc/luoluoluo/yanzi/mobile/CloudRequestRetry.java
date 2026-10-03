package cc.luoluoluo.yanzi.mobile;

import java.io.IOException;

final class CloudRequestRetry {
    static final class SessionChanged extends IOException {
        SessionChanged() { super("account_session_changed"); }
    }
    interface Request<T> { T run(boolean systemRoute) throws Exception; }
    // Respect the user's VPN for these facades; only safe reads may try a physical network.
    static <T> T systemFirst(boolean safe, Request<T> request) throws Exception {
        return execute(safe, fallback -> request.run(!fallback));
    }
    static boolean safe(String method, String path, boolean stableMessageId) {
        return "GET".equals(method) || "POST".equals(method) &&
                ("/v1/me/devices".equals(path) || "/v1/me/devices/lan-links".equals(path)
                        || "/v1/me/mobile/messages".equals(path) && stableMessageId);
    }
    static boolean retryable(Throwable error) {
        if (Thread.currentThread().isInterrupted()) return false;
        boolean transportFailure = false;
        for (int depth = 0; error != null && depth < 32; depth++, error = error.getCause()) {
            if (error instanceof HttpResponseBody.TooLarge || error instanceof InterruptedException || error instanceof SessionChanged) return false;
            transportFailure |= error instanceof IOException;
        }
        return transportFailure;
    }
    static <T> T execute(boolean safe, Request<T> request) throws Exception {
        try { return request.run(false); }
        catch (IOException directFailure) {
            if (!safe || !retryable(directFailure)) throw directFailure;
            try { return request.run(true); }
            catch (Exception systemFailure) { systemFailure.addSuppressed(directFailure); throw systemFailure; }
        }
    }
}

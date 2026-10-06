package cc.luoluoluo.yanzi.mobile;
import org.json.JSONObject;
import java.net.HttpURLConnection;
import java.net.URL;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

/** Cloud-only transport; inbox requests never use the desktop LAN API. */
final class MobileMessageClient {
    static final class HttpFailure extends Exception {
        final int status;
        final String code;
        HttpFailure(int status) { this(status, ""); }
        HttpFailure(int status, String code) { super("HTTP " + status + (code.isEmpty() ? "" : " " + code)); this.status = status; this.code = code; }
    }
    static JSONObject request(String base, String path, String token, String method, JSONObject payload) throws Exception {
        if ("POST".equals(method) && "/v1/me/mobile/messages".equals(path) && payload != null && !payload.has("clientMessageId"))
            payload.put("clientMessageId", java.util.UUID.randomUUID().toString());
        java.io.File saved = null;
        if ("POST".equals(method) && "/v1/me/mobile/messages".equals(path) && payload != null && MobileApplicationContext.get() != null && !(BuildConfig.DEBUG && token.equals("disposable-cloud-transfer-test"))) {
            String kind = payload.optString("kind");
            if ((kind.startsWith("run-") || kind.startsWith("fs-") || kind.equals("capability.invoke")) && !payload.has("expiresAt"))
                payload.put("expiresAt", java.time.Instant.now().plusSeconds(120).toString());
            saved = MobileMessageOutbox.save(base, token, payload);
        }
        JSONObject response = requestWithoutQueue(base, path, token, method, payload);
        MobileMessageOutbox.complete(saved);
        return response;
    }
    static JSONObject requestWithoutQueue(String base, String path, String token, String method, JSONObject payload) throws Exception {
        boolean safe = CloudRequestRetry.safe(method, path, payload != null && !payload.optString("clientMessageId").isEmpty());
        try {
            // Repeating the same per-message/device ACK is idempotent. Keep its transport
            // consistent with the attachment GETs so successful downloads can finish.
            boolean ack = "POST".equals(method) && path.matches("/v1/me/mobile/messages/[^/]+/ack");
            boolean claim = "POST".equals(method) && path.matches("/v1/me/mobile/messages/[^/]+/claim");
            // Claim is intentionally never retried: a lost success response is ambiguous and
            // retrying could suppress a command after the server already moved it to executing.
            // Use the system/default route for that single attempt so an active VPN remains
            // consistent with heartbeat/ACK traffic instead of forcing the underlying network.
            JSONObject response = ack
                    ? CloudRequestRetry.systemFirst(true, systemRoute -> requestOnce(base, path, token, method, payload, systemRoute))
                    : claim
                        ? CloudRequestRetry.systemFirst(false, systemRoute -> requestOnce(base, path, token, method, payload, systemRoute))
                        : CloudRequestRetry.execute(safe, systemRoute -> requestOnce(base, path, token, method, payload, systemRoute));
            try {
                if ("POST".equals(method) && "/v1/me/mobile/messages".equals(path) && payload != null) MobileTaskJournal.accepted(base, token, payload, response);
                if ("GET".equals(method) && path.matches("/v1/me/mobile/messages/[^/]+")) MobileTaskJournal.observed(base, token, path.substring(path.lastIndexOf('/')+1), response);
            } catch (Exception journalError) { android.util.Log.w("YanziTasks", "Receipt journal update failed"); }
            return response;
        } catch (Exception error) {
            if ("POST".equals(method) && "/v1/me/mobile/messages".equals(path) && payload != null) {
                try { MobileTaskJournal.transportError(base, token, payload, error); } catch (Exception ignored) { }
            }
            throw error;
        }
    }
    static JSONObject readSystemFirst(String base, String path, String token) throws Exception {
        return CloudRequestRetry.systemFirst(true,
                systemRoute -> requestOnce(base, path, token, "GET", null, systemRoute));
    }
    private static JSONObject requestOnce(String base, String path, String token, String method, JSONObject payload, boolean systemRoute) throws Exception {
        URL url = new URL(base + path);
        if (systemRoute) android.util.Log.i("YanziCloudRetry", "Cloud request route=system: " + path);
        HttpURLConnection connection = systemRoute ? (HttpURLConnection)url.openConnection() : MobileNetworkRouting.openCloudConnection(url);
        try {
            connection.setConnectTimeout(8000); connection.setReadTimeout(8000);
            connection.setRequestMethod(method);
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/" + BuildConfig.VERSION_NAME);
            connection.setRequestProperty("X-Yanzi-Client", "mobile");
            if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
            if (payload != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                try (java.io.OutputStream output = connection.getOutputStream()) {
                    output.write(payload.toString().getBytes(StandardCharsets.UTF_8));
                }
            }
            int status = connection.getResponseCode();
            if (status < 200 || status >= 300) {
                String code = "";
                try {
                    code = new JSONObject(HttpResponseBody.read(connection.getErrorStream(), 16384)).optString("error", "");
                    if (!code.matches("[a-z0-9_]{1,100}")) code = "";
                } catch (Exception ignored) {}
                throw new HttpFailure(status, code);
            }
            String body = HttpResponseBody.read(connection.getInputStream(), 2000000);
            return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
        } finally { connection.disconnect(); }
    }
}

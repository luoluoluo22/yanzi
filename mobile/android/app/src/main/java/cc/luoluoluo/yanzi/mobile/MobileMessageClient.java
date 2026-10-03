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
        HttpFailure(int status) { super("HTTP " + status); this.status = status; }
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
        return CloudRequestRetry.execute(safe, systemRoute -> requestOnce(base, path, token, method, payload, systemRoute));
    }
    private static JSONObject requestOnce(String base, String path, String token, String method, JSONObject payload, boolean systemRoute) throws Exception {
        URL url = new URL(base + path);
        if (systemRoute) android.util.Log.i("YanziCloudRetry", "Retrying safe cloud request on system route: " + path);
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
            if (status < 200 || status >= 300) throw new HttpFailure(status);
            String body = HttpResponseBody.read(connection.getInputStream(), 2000000);
            return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
        } finally { connection.disconnect(); }
    }
}

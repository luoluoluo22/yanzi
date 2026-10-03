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
        if ("POST".equals(method) && "/v1/me/mobile/messages".equals(path) && payload != null && MainActivity.sContext != null && !(BuildConfig.DEBUG && token.equals("disposable-cloud-transfer-test"))) {
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
        HttpURLConnection connection = MobileNetworkRouting.openCloudConnection(new URL(base + path));
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
            try (InputStream input = connection.getInputStream(); java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream()) {
                byte[] bytes = new byte[4096]; int count;
                while ((count = input.read(bytes)) != -1) {
                    buffer.write(bytes, 0, count);
                    if (buffer.size() > 2000000) throw new java.io.IOException("Response too large");
                }
                String body = buffer.toString("UTF-8");
                return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
            }
        } finally { connection.disconnect(); }
    }
}

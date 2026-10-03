package cc.luoluoluo.yanzi.mobile;

import org.json.JSONObject;
import java.net.URL;
import java.net.HttpURLConnection;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;

/** HTTP mechanics only; routing and account state belong to the calling facade. */
final class MobileApiTransport {
    interface ErrorFormatter { String format(String action, String path, int status, String message); }
    static JSONObject request(
                String baseUrl,
                String path,
                String token,
                String action,
                String method,
                JSONObject payload,
                int timeoutMs,
                android.net.Network network, boolean lan, ErrorFormatter errorFormatter) throws Exception {

            URL url = new URL(baseUrl + path);
            MobileSessionStore.Snapshot session = MobileSessionStore.snapshot(MobileApplicationContext.get());
            if (!lan && token != null && !token.isEmpty()
                    && session.baseUrl.equals(baseUrl.replaceAll("/+$", "")) && !session.matches(baseUrl, token))
                throw new CloudRequestRetry.SessionChanged();
            HttpURLConnection connection = network == null
                    ? (lan ? MobileNetworkRouting.openLanConnection(url) : (HttpURLConnection)url.openConnection())
                    : (HttpURLConnection)network.openConnection(url);

            try {
                connection.setRequestMethod(method);
                connection.setConnectTimeout(lan ? Math.min(1500, timeoutMs) : timeoutMs);
                connection.setReadTimeout(timeoutMs);
                connection.setRequestProperty("User-Agent", "YanziClient-Mobile/" + BuildConfig.VERSION_NAME);
                connection.setRequestProperty("X-Yanzi-Client", "mobile");
                connection.setRequestProperty("X-Yanzi-Client-Version", BuildConfig.VERSION_NAME);
                connection.setRequestProperty("Accept", "application/json");
                if (payload != null) {
                    connection.setDoOutput(true);
                    connection.setRequestProperty(
                            "Content-Type",
                            "application/json; charset=utf-8");
                }
                if (token != null && !token.trim().isEmpty()) {
                    connection.setRequestProperty("Authorization", "Bearer " + token);
                }
                if (payload != null) {
                    try (OutputStreamWriter writer = new OutputStreamWriter(
                            connection.getOutputStream(),
                            StandardCharsets.UTF_8)) {
                        writer.write(payload.toString());
                    }
                }

                String body = HttpResponseBody.read(connection);
                int statusCode = connection.getResponseCode();
                if (statusCode < 200 || statusCode >= 300) {
                    String message = body;
                    try {
                        message = new JSONObject(body).optString("message", body);
                    }
                    catch (Exception ignored) {
                    }
                    throw new IllegalStateException(
                            errorFormatter.format(
                                    action,
                                    path,
                                    statusCode,
                                    message));
                }
                session.requireCurrent();
                return body.trim().isEmpty()
                        ? new JSONObject()
                        : new JSONObject(body);
            }
            finally {
                connection.disconnect();
            }
        }

}

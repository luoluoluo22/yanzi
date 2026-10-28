package cc.luoluoluo.yanzi.mobile;

import org.json.JSONObject;

import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

final class MobileExtensionStorageClient {
    private static final int SCHEMA_VERSION = 1;
    private static final String OBJECT_PREFIX = "extensionData.v1.";

    private MobileExtensionStorageClient() {
    }

    static String buildObjectId(String extensionId, String key) throws Exception {
        String normalizedExtensionId = normalizeExtensionId(extensionId);
        String normalizedKey = normalizeKey(key);
        return OBJECT_PREFIX + sha256Hex(normalizedExtensionId + "\0" + normalizedKey);
    }

    static JSONObject read(
            String baseUrl,
            String token,
            String extensionId,
            String key) throws Exception {
        String normalizedExtensionId = normalizeExtensionId(extensionId);
        String normalizedKey = normalizeKey(key);
        String objectId = buildObjectId(normalizedExtensionId, normalizedKey);

        HttpResult response = request(
                baseUrl,
                "/v1/sync/objects/" + objectId,
                token,
                "GET",
                null);

        if (response.statusCode == 404) {
            return new JSONObject()
                    .put("ok", true)
                    .put("exists", false)
                    .put("deleted", false)
                    .put("revision", 0L)
                    .put("objectId", objectId)
                    .put("source", "account");
        }

        ensureSuccess(response, "读取小程序云数据");
        JSONObject envelope = parseObject(response.body);
        JSONObject object = envelope.optJSONObject("object");
        if (object == null) {
            throw new IllegalStateException("云端同步对象响应缺少 object。");
        }
        return parseStorageObject(
                object,
                normalizedExtensionId,
                normalizedKey,
                objectId);
    }

    static JSONObject write(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String extensionId,
            String key,
            String content,
            long expectedRevision) throws Exception {
        return writeCore(
                baseUrl, token, deviceId, deviceName,
                extensionId, key, content, false, expectedRevision);
    }

    static JSONObject delete(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String extensionId,
            String key,
            long expectedRevision) throws Exception {
        return writeCore(
                baseUrl, token, deviceId, deviceName,
                extensionId, key, "", true, expectedRevision);
    }

    private static JSONObject writeCore(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String extensionId,
            String key,
            String content,
            boolean deleted,
            long expectedRevision) throws Exception {
        String normalizedExtensionId = normalizeExtensionId(extensionId);
        String normalizedKey = normalizeKey(key);
        String normalizedContent = content == null ? "" : content;
        String objectId = buildObjectId(normalizedExtensionId, normalizedKey);

        long revision = expectedRevision;
        if (revision < 0) {
            JSONObject observed = read(
                    baseUrl, token, normalizedExtensionId, normalizedKey);
            revision = observed.optLong("revision", 0L);
        }

        JSONObject storagePayload = new JSONObject()
                .put("extensionId", normalizedExtensionId)
                .put("key", normalizedKey)
                .put("contentType", "text/plain; charset=utf-8")
                .put("content", deleted ? "" : normalizedContent)
                .put("contentHash", sha256Hex(deleted ? "" : normalizedContent));

        JSONObject requestPayload = new JSONObject()
                .put("schemaVersion", SCHEMA_VERSION)
                .put("expectedRevision", revision)
                .put("deleted", deleted)
                .put("payload", storagePayload)
                .put("updatedByDeviceId", deviceId == null ? "" : deviceId)
                .put("updatedByDeviceName", deviceName == null ? "" : deviceName);

        HttpResult response = request(
                baseUrl,
                "/v1/sync/objects/" + objectId,
                token,
                "PUT",
                requestPayload);

        if (response.statusCode == 409) {
            JSONObject body = parseObject(response.body);
            JSONObject details = body.optJSONObject("details");
            return new JSONObject()
                    .put("ok", false)
                    .put("conflict", true)
                    .put("objectId", objectId)
                    .put("expectedRevision", revision)
                    .put("currentRevision",
                            details == null ? 0L : details.optLong("currentRevision", 0L))
                    .put("error", body.optString("message", "revision conflict"));
        }

        ensureSuccess(response, deleted ? "删除小程序云数据" : "写入小程序云数据");
        JSONObject envelope = parseObject(response.body);

        JSONObject object = envelope.optJSONObject("object");
        if (object == null) {
            throw new IllegalStateException("云端同步对象写入响应缺少 object。");
        }
        JSONObject result = parseStorageObject(
                object,
                normalizedExtensionId,
                normalizedKey,
                objectId);
        result.put("conflict", false);
        return result;
    }

    private static JSONObject parseStorageObject(
            JSONObject object,
            String expectedExtensionId,
            String expectedKey,
            String objectId) throws Exception {
        JSONObject payload = object.optJSONObject("payload");
        if (payload == null) {
            throw new IllegalStateException("小程序云数据对象缺少 payload。");
        }

        String extensionId = payload.optString(
                "extensionId", payload.optString("ExtensionId", ""));
        String key = payload.optString(
                "key", payload.optString("Key", ""));
        if (!extensionId.equalsIgnoreCase(expectedExtensionId)
                || !key.equals(expectedKey)) {
            throw new IllegalStateException("小程序云数据对象作用域不匹配。");
        }

        boolean deleted = object.optBoolean("deleted", false);
        String content = payload.optString(
                "content", payload.optString("Content", ""));
        String contentHash = payload.optString(
                "contentHash", payload.optString("ContentHash", ""));
        if (!deleted && !sha256Hex(content).equalsIgnoreCase(contentHash)) {
            throw new IllegalStateException("小程序云数据对象 SHA-256 校验失败。");
        }

        return new JSONObject()
                .put("ok", true)
                .put("exists", !deleted)
                .put("deleted", deleted)
                .put("revision", object.optLong("revision", 0L))
                .put("objectId", objectId)
                .put("content", deleted ? JSONObject.NULL : content)

                .put("contentType", payload.optString(
                        "contentType",
                        payload.optString(
                                "ContentType",
                                "text/plain; charset=utf-8")))
                .put("updatedAtUtc", object.optString("updatedAtUtc", ""))
                .put("updatedByDeviceId", object.optString("updatedByDeviceId", ""))
                .put("updatedByDeviceName", object.optString("updatedByDeviceName", ""))
                .put("source", "account");
    }

    private static String normalizeExtensionId(String extensionId) {
        String normalized = extensionId == null ? "" : extensionId.trim();
        if (normalized.isEmpty()) {
            throw new IllegalArgumentException("extensionId 不能为空。");
        }
        return normalized;
    }

    private static String normalizeKey(String key) {
        String normalized = key == null ? "" : key.replace('\\', '/');
        while (normalized.startsWith("/")) normalized = normalized.substring(1);
        while (normalized.endsWith("/")) normalized =
                normalized.substring(0, normalized.length() - 1);

        if (normalized.trim().isEmpty()) {
            throw new IllegalArgumentException("storage key 不能为空。");
        }

        List<String> segments = new ArrayList<>();
        for (String raw : normalized.split("/")) {
            String segment = raw.trim();
            if (segment.isEmpty()) continue;
            if (".".equals(segment) || "..".equals(segment)) {
                throw new IllegalArgumentException(
                        "storage key 不能包含 . 或 .. 路径段。");
            }
            segments.add(segment);
        }
        if (segments.isEmpty()) {
            throw new IllegalArgumentException("storage key 不能为空。");
        }
        return String.join("/", segments);
    }

    private static String sha256Hex(String value) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        byte[] hash = digest.digest(
                (value == null ? "" : value).getBytes(StandardCharsets.UTF_8));

        StringBuilder builder = new StringBuilder(hash.length * 2);
        for (byte b : hash) {
            builder.append(String.format(Locale.ROOT, "%02x", b & 0xff));
        }
        return builder.toString();
    }

    private static HttpResult request(
            String baseUrl,
            String path,
            String token,
            String method,
            JSONObject payload) throws Exception {
        MobileSessionStore.Snapshot session = MobileSessionStore.snapshot(MobileApplicationContext.get());
        return CloudRequestRetry.systemFirst("GET".equals(method),
                systemRoute -> requestOnce(baseUrl,path,token,method,payload,systemRoute,session));
    }

    private static HttpResult requestOnce(String baseUrl,String path,String token,String method,
                                         JSONObject payload,boolean systemRoute,MobileSessionStore.Snapshot session) throws Exception {
        String root = baseUrl == null ? "" : baseUrl.trim();
        while (root.endsWith("/")) {
            root = root.substring(0, root.length() - 1);
        }

        session.requireCurrent();
        URL url=new URL(root+path);
        HttpURLConnection connection = systemRoute ? (HttpURLConnection)url.openConnection()
                : MobileNetworkRouting.openCloudConnection(url);
        try {
        connection.setRequestMethod(method);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.2");
        connection.setRequestProperty("X-Yanzi-Client", "mobile");

        connection.setRequestProperty("Accept", "application/json");
        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty("Authorization", "Bearer " + token.trim());
        }

        if (payload != null) {
            connection.setDoOutput(true);
            connection.setRequestProperty(
                    "Content-Type", "application/json; charset=utf-8");
            try (OutputStreamWriter writer = new OutputStreamWriter(
                    connection.getOutputStream(), StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }
        }

        int statusCode = connection.getResponseCode();
        String body = HttpResponseBody.read(connection);
        session.requireCurrent();
        return new HttpResult(statusCode, body);
        } finally {connection.disconnect();}
    }

    private static JSONObject parseObject(String body) throws Exception {
        if (body == null || body.trim().isEmpty()) {
            return new JSONObject();
        }
        return new JSONObject(body);
    }

    private static void ensureSuccess(
            HttpResult result,
            String action) throws Exception {
        if (result.statusCode >= 200 && result.statusCode < 300) return;

        String message = result.body;
        try {
            JSONObject body = parseObject(result.body);
            message = body.optString("message", message);
        } catch (Exception ignored) {
        }
        throw new IllegalStateException(
                action + "失败，HTTP " + result.statusCode
                        + (message == null || message.trim().isEmpty()
                        ? "" : "：" + message));
    }

    private static final class HttpResult {
        final int statusCode;
        final String body;

        HttpResult(int statusCode, String body) {
            this.statusCode = statusCode;
            this.body = body == null ? "" : body;
        }
    }
}

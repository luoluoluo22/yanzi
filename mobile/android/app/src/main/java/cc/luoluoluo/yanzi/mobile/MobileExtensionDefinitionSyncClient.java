package cc.luoluoluo.yanzi.mobile;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.LinkedHashSet;
import java.util.Locale;

final class MobileExtensionDefinitionSyncClient {
    private static final int SCHEMA_VERSION = 1;
    private static final String INDEX_OBJECT_ID = "mobileExtensions.index.v1";
    private static final String OBJECT_PREFIX = "mobileExtension.v1.";

    private MobileExtensionDefinitionSyncClient() {
    }

    static String buildObjectId(String extensionId) throws Exception {
        String id = normalizeExtensionId(extensionId);
        return OBJECT_PREFIX + sha256Hex(id);
    }

    static JSONObject fetchState(String baseUrl, String token) throws Exception {
        JSONObject index = readObject(
                baseUrl,
                token,
                INDEX_OBJECT_ID);
        if (index == null || index.optBoolean("deleted", false)) {
            return new JSONObject()
                    .put("available", true)
                    .put("initialized", false)
                    .put(
                            "indexRevision",
                            index == null
                                    ? 0L
                                    : index.optLong("revision", 0L))
                    .put("extensions", new JSONArray());
        }

        LinkedHashSet<String> ids = parseIndexIds(index);
        JSONArray extensions = new JSONArray();
        for (String objectId : ids) {
            JSONObject object = readObject(
                    baseUrl,
                    token,
                    objectId);
            if (object == null || object.optBoolean("deleted", false)) {
                continue;
            }

            JSONObject objectPayload =
                    object.optJSONObject("payload");
            JSONObject definition = objectPayload == null
                    ? null
                    : objectPayload.optJSONObject("definition");
            if (definition == null) {
                continue;
            }

            String extensionId = normalizeExtensionId(
                    definition.optString("id", ""));
            if (!objectId.equals(buildObjectId(extensionId))) {
                throw new IllegalStateException(
                        "手机小程序定义对象 ID 校验失败："
                                + extensionId);
            }
            extensions.put(
                    new JSONObject(definition.toString()));
        }

        return new JSONObject()
                .put("available", true)
                .put("initialized", true)
                .put(
                        "indexRevision",
                        index.optLong("revision", 0L))
                .put("extensions", extensions);
    }

    static JSONObject seedIfMissing(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            JSONArray localExtensions) throws Exception {

        JSONObject state = fetchState(baseUrl, token);
        if (!state.optBoolean("available", false)
                || state.optBoolean("initialized", false)) {
            return state;
        }

        LinkedHashSet<String> objectIds = new LinkedHashSet<String>();
        for (int i = 0; i < localExtensions.length(); i++) {
            JSONObject definition = localExtensions.optJSONObject(i);
            if (definition == null) continue;
            String extensionId = normalizeExtensionId(definition.optString("id", ""));
            String objectId = buildObjectId(extensionId);
            objectIds.add(objectId);

            JSONObject existing = readObject(baseUrl, token, objectId);
            long expectedRevision = existing == null ? 0L : existing.optLong("revision", 0L);
            if (existing != null && !existing.optBoolean("deleted", false)) {
                continue;
            }

            putDefinitionObject(
                    baseUrl,
                    token,
                    deviceId,
                    deviceName,
                    objectId,
                    definition,
                    expectedRevision,
                    false);
        }

        updateIndex(
                baseUrl,
                token,
                deviceId,
                deviceName,
                objectIds,
                true);

        return fetchState(baseUrl, token);
    }

    static JSONObject upsert(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            JSONObject definition) throws Exception {

        String extensionId = normalizeExtensionId(definition.optString("id", ""));
        String objectId = buildObjectId(extensionId);
        JSONObject existing = readObject(baseUrl, token, objectId);
        long expectedRevision = existing == null ? 0L : existing.optLong("revision", 0L);

        JSONObject saved = putDefinitionObject(
                baseUrl,
                token,
                deviceId,
                deviceName,
                objectId,
                definition,
                expectedRevision,
                false);

        updateIndexMembership(
                baseUrl,
                token,
                deviceId,
                deviceName,
                objectId,
                true);

        return saved;
    }

    static void delete(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String extensionId) throws Exception {

        String normalizedId = normalizeExtensionId(extensionId);
        String objectId = buildObjectId(normalizedId);
        JSONObject existing = readObject(baseUrl, token, objectId);
        if (existing != null) {
            long expectedRevision = existing.optLong("revision", 0L);
            JSONObject definition = new JSONObject().put("id", normalizedId);
            putDefinitionObject(
                    baseUrl,
                    token,
                    deviceId,
                    deviceName,
                    objectId,
                    definition,
                    expectedRevision,
                    true);
        }

        updateIndexMembership(
                baseUrl,
                token,
                deviceId,
                deviceName,
                objectId,
                false);
    }

    private static JSONObject unavailableState() throws Exception {
        return new JSONObject()
                .put("available", false)
                .put("initialized", false)
                .put("indexRevision", 0L)
                .put("extensions", new JSONArray());
    }

    private static JSONObject putDefinitionObject(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String objectId,
            JSONObject definition,
            long expectedRevision,
            boolean deleted) throws Exception {

        JSONObject payload = new JSONObject()
                .put("extensionId", definition.optString("id", ""))
                .put("definition", new JSONObject(definition.toString()));

        JSONObject requestPayload = new JSONObject()
                .put("schemaVersion", SCHEMA_VERSION)
                .put("expectedRevision", expectedRevision)
                .put("deleted", deleted)
                .put("payload", payload)
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
            throw new IllegalStateException(
                    "手机小程序定义存在并发修改："
                            + body.optString("message", "revision conflict"));
        }

        ensureSuccess(response, deleted ? "删除手机小程序定义" : "保存手机小程序定义");
        JSONObject envelope = parseObject(response.body);
        JSONObject object = envelope.optJSONObject("object");
        if (object == null) {
            throw new IllegalStateException("手机小程序定义写入响应缺少 object。");
        }
        return object;
    }

    private static LinkedHashSet<String> parseIndexIds(
            JSONObject index) {
        LinkedHashSet<String> ids = new LinkedHashSet<String>();
        if (index == null || index.optBoolean("deleted", false)) {
            return ids;
        }

        JSONObject payload = index.optJSONObject("payload");
        JSONArray array = payload == null
                ? null
                : payload.optJSONArray("objectIds");
        if (array == null) {
            return ids;
        }

        for (int i = 0; i < array.length(); i++) {
            String value = array.optString(i, "").trim();
            if (!value.isEmpty() && value.startsWith(OBJECT_PREFIX)) {
                ids.add(value);
            }
        }
        return ids;
    }

    private static void updateIndex(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            LinkedHashSet<String> desiredIds,
            boolean onlyIfMissing) throws Exception {

        for (int attempt = 0; attempt < 3; attempt++) {
            JSONObject index = readObject(baseUrl, token, INDEX_OBJECT_ID);
            if (onlyIfMissing && index != null && !index.optBoolean("deleted", false)) {
                return;
            }

            LinkedHashSet<String> merged =
                    parseIndexIds(index);

            if (onlyIfMissing) {
                merged.addAll(desiredIds);
            } else {
                merged.clear();
                merged.addAll(desiredIds);
            }

            JSONArray ids = new JSONArray();
            for (String id : merged) {
                ids.put(id);
            }

            long expectedRevision = index == null ? 0L : index.optLong("revision", 0L);
            JSONObject requestPayload = new JSONObject()
                    .put("schemaVersion", SCHEMA_VERSION)
                    .put("expectedRevision", expectedRevision)
                    .put("deleted", false)
                    .put("payload", new JSONObject().put("objectIds", ids))
                    .put("updatedByDeviceId", deviceId == null ? "" : deviceId)
                    .put("updatedByDeviceName", deviceName == null ? "" : deviceName);

            HttpResult response = request(
                    baseUrl,
                    "/v1/sync/objects/" + INDEX_OBJECT_ID,
                    token,
                    "PUT",
                    requestPayload);

            if (response.statusCode == 409) {
                continue;
            }

            ensureSuccess(response, "更新手机小程序定义索引");
            return;
        }

        throw new IllegalStateException("手机小程序定义索引存在并发修改，请稍后重试。");
    }


    private static void updateIndexMembership(
            String baseUrl,
            String token,
            String deviceId,
            String deviceName,
            String objectId,
            boolean present) throws Exception {

        for (int attempt = 0; attempt < 4; attempt++) {
            JSONObject index = readObject(
                    baseUrl,
                    token,
                    INDEX_OBJECT_ID);
            LinkedHashSet<String> ids = parseIndexIds(index);

            boolean changed = present
                    ? ids.add(objectId)
                    : ids.remove(objectId);
            if (!changed) {
                return;
            }

            if (index == null && !present) {
                return;
            }

            JSONArray array = new JSONArray();
            for (String id : ids) {
                array.put(id);
            }

            long expectedRevision = index == null
                    ? 0L
                    : index.optLong("revision", 0L);
            JSONObject requestPayload = new JSONObject()
                    .put("schemaVersion", SCHEMA_VERSION)
                    .put("expectedRevision", expectedRevision)
                    .put("deleted", false)
                    .put(
                            "payload",
                            new JSONObject().put("objectIds", array))
                    .put(
                            "updatedByDeviceId",
                            deviceId == null ? "" : deviceId)
                    .put(
                            "updatedByDeviceName",
                            deviceName == null ? "" : deviceName);

            HttpResult response = request(
                    baseUrl,
                    "/v1/sync/objects/" + INDEX_OBJECT_ID,
                    token,
                    "PUT",
                    requestPayload);

            if (response.statusCode == 409) {
                continue;
            }

            ensureSuccess(response, "更新手机小程序定义索引");
            return;
        }

        throw new IllegalStateException(
                "手机小程序定义索引存在并发修改，请稍后重试。");
    }

    private static JSONObject readObject(
            String baseUrl,
            String token,
            String objectId) throws Exception {
        HttpResult response = request(
                baseUrl,
                "/v1/sync/objects/" + objectId,
                token,
                "GET",
                null);

        if (response.statusCode == 404) {
            return null;
        }

        ensureSuccess(response, "读取手机小程序定义对象");
        JSONObject envelope = parseObject(response.body);
        return envelope.optJSONObject("object");
    }

    private static String normalizeExtensionId(String value) {
        String normalized = value == null ? "" : value.trim();
        if (normalized.isEmpty()) {
            throw new IllegalArgumentException("手机小程序 id 不能为空。");
        }
        return normalized;
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

        String root = baseUrl == null ? "" : baseUrl.trim();
        while (root.endsWith("/")) {
            root = root.substring(0, root.length() - 1);
        }

        HttpURLConnection connection =
                MobileNetworkRouting.openCloudConnection(
                        new URL(root + path));
        connection.setRequestMethod(method);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.2");
        connection.setRequestProperty("X-Yanzi-Client", "mobile");
        connection.setRequestProperty("Accept", "application/json");

        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty(
                    "Authorization",
                    "Bearer " + token.trim());
        }

        if (payload != null) {
            connection.setDoOutput(true);
            connection.setRequestProperty(
                    "Content-Type",
                    "application/json; charset=utf-8");
            try (OutputStreamWriter writer = new OutputStreamWriter(
                    connection.getOutputStream(),
                    StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }
        }

        int statusCode = connection.getResponseCode();
        String body = readBody(connection, statusCode);
        connection.disconnect();
        return new HttpResult(statusCode, body);
    }

    private static String readBody(
            HttpURLConnection connection,
            int statusCode) throws Exception {
        InputStream stream = statusCode >= 200 && statusCode < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
        if (stream == null) return "";

        StringBuilder builder = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(stream, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                builder.append(line);
            }
        }
        return builder.toString();
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
        if (result.statusCode >= 200 && result.statusCode < 300) {
            return;
        }

        String message = result.body;
        try {
            JSONObject body = parseObject(result.body);
            message = body.optString("message", message);
        } catch (Exception ignored) {
        }

        throw new IllegalStateException(
                action + "失败，HTTP " + result.statusCode
                        + (message == null || message.trim().isEmpty()
                        ? ""
                        : "：" + message));
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

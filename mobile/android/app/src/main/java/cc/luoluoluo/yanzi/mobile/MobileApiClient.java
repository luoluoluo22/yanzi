package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.drawable.Icon;
import android.util.Base64;
import android.util.Log;
import java.util.ArrayList;
import cc.luoluoluo.yanzi.mobile.LanDiscoveryManager;
import cc.luoluoluo.yanzi.mobile.MobileDiagnostics;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ConcurrentHashMap;
import cc.luoluoluo.yanzi.sdk.DeviceTargets;
import org.json.JSONArray;
import org.json.JSONObject;

class MobileApiClient {
        public static boolean sLanFailedThisSession = false;
        private static final ConcurrentHashMap<String, JSONObject> sImmediateMessageResults = new ConcurrentHashMap<>();

        static String login(String baseUrl, String email, String password) throws Exception {
            return loginResponse(baseUrl, email, password).getString("accessToken");
        }

        static JSONObject loginResponse(String baseUrl, String email, String password) throws Exception {
            JSONObject payload = new JSONObject().put("email", (Object)email).put("password", (Object)password);
            return MobileApiClient.postJson(baseUrl, "/v1/auth/login", payload, null, "\u767b\u5f55");
        }

        static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
            registerDevice(baseUrl, token, deviceId, displayName, false);
        }

        static void registerDevice(String baseUrl, String token, String deviceId, String displayName, boolean reconnectRemoved) throws Exception {
            JSONObject capabilities = new JSONObject().put("shareText", true).put("sendToDesktop", true)
                    .put("receiveMobileMessages", true).put("receiveAttachments", true)
                    .put("receiveAccountChat", true).put("deviceMessageProtocolVersions", new JSONArray().put(1))
                    .put("autoAccountLan", true).put("lanPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981)
                    .put("receiveLanAttachments", true).put("maxAttachmentBytes", MobileAttachmentClient.LIMIT)
                    .put("appVersion", BuildConfig.VERSION_NAME).put("versionCode", BuildConfig.VERSION_CODE)
                    .put("packageName", BuildConfig.APPLICATION_ID).put("messageProtocol", 2);
            JSONObject payload = new JSONObject().put("deviceId", (Object)deviceId).put("platform", (Object)"android").put("displayName", (Object)displayName).put("capabilities", (Object)capabilities);
            if (reconnectRemoved) payload.put("reactivateRemovedDevice", true);
            MobileApiClient.postJson(baseUrl, "/v1/me/devices", payload, token, "\u8bbe\u5907\u6ce8\u518c");
        }

        static String sendTextToDesktop(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
            JSONObject envelope = new JSONObject()
                    .put("sourceDeviceId", sourceDeviceId)
                    .put("kind", "text")
                    .put("title", "YanziChat")
                    .put("text", text)
                    .put("routing", "account-chat")
                    .put("payload", new JSONObject()
                            .put("source", "android")
                            .put("sourceDeviceName", MobileDeviceIdentity.buildDeviceDisplayName())
                            .put("createdAt", System.currentTimeMillis()));
            return MobileDeviceMessageSender.send(baseUrl, token, envelope).optString("messageId", "unknown");
        }

        static String sendPhotoToDesktop(String baseUrl, String token, String sourceDeviceId, byte[] jpegBytes, int width, int height) throws Exception {
            return MobileDesktopTransfer.sendBytes(MobileApplicationContext.get(), baseUrl, token, sourceDeviceId, "photo",
                    "photo-" + System.currentTimeMillis() + ".jpg", "image/jpeg", jpegBytes, "手机照片 " + width + "x" + height);
        }

        static String postScreenshotDirectMessage(String baseUrl, String token, String sourceDeviceId, String screenshotDataUrl, int bytes, int width, int height) throws Exception {
            JSONObject payload = new JSONObject().put("sourceDeviceId", (Object)sourceDeviceId).put("targetPlatform", (Object)"desktop").put("kind", (Object)"screenshot").put("title", (Object)"\u624b\u673a\u7167\u7247").put("text", (Object)("\u624b\u673a\u7167\u7247\uff1a" + width + "x" + height)).put("payload", (Object)new JSONObject().put("source", (Object)"android-mobile").put("sourceDeviceName", (Object)MobileDeviceIdentity.buildDeviceDisplayName()).put("screenshotMime", (Object)"image/jpeg").put("screenshotWidth", width).put("screenshotHeight", height).put("screenshotBytes", bytes).put("screenshotDataUrl", (Object)screenshotDataUrl).put("expiresAt", System.currentTimeMillis() + 2592000000L).put("createdAt", System.currentTimeMillis()));
            return MobileApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u53d1\u9001\u7167\u7247").optString("messageId", "unknown");
        }

        static WebDavConfig fetchWebDavConfig(String baseUrl, String token) throws Exception {
            JSONObject json = MobileApiClient.getJson(baseUrl, "/v1/sync/webdav-config", token, "\u8bfb\u53d6 WebDAV");
            WebDavConfig config = new WebDavConfig();
            config.serverUrl = json.optString("serverUrl", "https://dav.jianguoyun.com/dav/");
            config.rootPath = json.optString("rootPath", "/yanzi");
            config.username = json.optString("username", "");
            config.password = json.optString("password", "");
            if (!json.optBoolean("enabled", false) || config.username.trim().isEmpty() || config.password.trim().isEmpty()) {
                throw new IllegalStateException("\u8d26\u53f7\u672a\u914d\u7f6e\u53ef\u7528\u7684 WebDAV\u3002");
            }
            return config;
        }

        static String uploadMobilePhotoToWebDav(WebDavConfig config, byte[] bytes) throws Exception {
            String day = new SimpleDateFormat("yyyyMMdd", Locale.ROOT).format(new Date());
            String fileName = "mobile-photo-" + day + "-" + UUID.randomUUID().toString().replace("-", "") + ".jpg";
            MobileApiClient.putWebDavBytes(config, fileName, bytes, "image/jpeg");
            return fileName;
        }


        static void putWebDavBytes(WebDavConfig config, String relativePath, byte[] bytes, String contentType) throws Exception {
            HttpURLConnection connection = MobileApiClient.openWebDav(config, relativePath);
            connection.setRequestMethod("PUT");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(30000);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", contentType);
            connection.setFixedLengthStreamingMode(bytes.length);
            connection.connect();
            try (OutputStream output = connection.getOutputStream();){
                output.write(bytes);
            }
            String body = MobileApiClient.readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                throw new IllegalStateException("WebDAV \u4e0a\u4f20\u5931\u8d25\uff0cHTTP " + connection.getResponseCode() + "\uff1a" + body);
            }
        }

        static HttpURLConnection openWebDav(WebDavConfig config, String relativePath) throws Exception {
            String root;
            String server;
            String string = server = config.serverUrl == null ? "" : config.serverUrl.trim();
            if (!server.endsWith("/")) {
                server = server + "/";
            }
            String string2 = root = config.rootPath == null ? "" : config.rootPath.trim();
            if (!root.startsWith("/")) {
                root = "/" + root;
            }
            if (!root.endsWith("/")) {
                root = root + "/";
            }
            String path = root + relativePath;
            while (path.contains("//")) {
                path = path.replace("//", "/");
            }
            URL url = new URL(server + path.substring(1));
            HttpURLConnection connection = (HttpURLConnection)url.openConnection();
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            String userpass = (config.username == null ? "" : config.username) + ":" + (config.password == null ? "" : config.password);
            String encoded = Base64.encodeToString((byte[])userpass.getBytes(StandardCharsets.UTF_8), (int)2);
            connection.setRequestProperty("Authorization", "Basic " + encoded);
            return connection;
        }

        public static String runExtensionOnDesktop(String baseUrl, String token, String sourceDeviceId, String sourceDeviceName, String extensionId, String inputText) throws Exception {
            JSONObject payload = new JSONObject().put("sourceDeviceId", (Object)sourceDeviceId).put("targetPlatform", (Object)"desktop").put("kind", (Object)"run-extension").put("title", (Object)"\u624b\u673a\u8bf7\u6c42\u6267\u884c\u6269\u5c55").put("text", (Object)(inputText == null ? "" : inputText)).put("payload", (Object)new JSONObject().put("source", (Object)"android").put("sourceDeviceName", (Object)sourceDeviceName).put("extensionId", (Object)extensionId).put("createdAt", System.currentTimeMillis()));
            String targetDeviceId = MobileApiClient.resolveDesktopTargetDeviceId(baseUrl, token, sourceDeviceId);
            if (!targetDeviceId.isEmpty()) payload.put("targetDeviceId", targetDeviceId);
            JSONObject response = MobileApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u6267\u884c\u6269\u5c55");
            String messageId = response.optString("messageId", "unknown");
            if ("lan".equals(response.optString("_transport")) && !"unknown".equals(messageId)) {
                JSONObject executionResult = new JSONObject().put("output", response.optString("output", ""));
                JSONObject detail = new JSONObject()
                        .put("status", response.optBoolean("success") ? "completed" : "failed")
                        .put("payload", new JSONObject().put("executionResult", executionResult));
                sImmediateMessageResults.put(messageId, detail);
            }
            return messageId;
        }

        static final class MissingSourceDeviceException extends Exception {
            MissingSourceDeviceException() { super("本机已从账号中删除，请重新登录后再执行。电脑执行请求尚未发送。"); }
        }

        private static String resolveDesktopTargetDeviceId(String baseUrl, String token, String sourceDeviceId) throws Exception {
            JSONArray items = null;
            java.io.IOException networkError = null;
            try {
                JSONObject devices = MobileMessageClient.requestWithoutQueue(baseUrl, "/v1/me/devices", token, "GET", null);
                items = devices.optJSONArray("items");
                if (items == null) throw new org.json.JSONException("设备列表响应无效，请稍后重试。");
            } catch (java.io.IOException unavailable) { networkError = unavailable; }
            if (items != null) {
                boolean sourcePresent = false;
                for (int i = 0; i < items.length(); i++) {
                    JSONObject device = items.optJSONObject(i);
                    if (device != null && sourceDeviceId.equals(device.optString("deviceId"))) sourcePresent = true;
                }
                if (!sourcePresent) throw new MissingSourceDeviceException();
            }
            Context context = MobileApplicationContext.get();
            if (context != null) {
                try {
                    String pairedDeviceId = LanDiscoveryManager.getLanDeviceId(context);
                    if (pairedDeviceId != null && !pairedDeviceId.trim().isEmpty()
                            && LanDiscoveryManager.checkHealth(context) == LanConnectionHealth.Status.AVAILABLE) {
                        return pairedDeviceId.trim();
                    }
                }
                catch (Exception ignored) {
                }
            }
            if (items == null) throw networkError;
            JSONObject target = DeviceTargets.uniqueOnlineDesktop(items);
            return target == null ? "" : target.optString("deviceId", "").trim();
        }

        public static JSONObject fetchMessageDetail(String baseUrl, String token, String messageId) throws Exception {
            JSONObject immediate = sImmediateMessageResults.remove(messageId);
            if (immediate != null) return immediate;
            return MobileApiClient.getJson(baseUrl, "/v1/me/mobile/messages/" + MobileApiClient.encodePath(messageId), token, "\u83b7\u53d6\u6d88\u606f\u8be6\u60c5");
        }

        static List<RemoteExtension> fetchRunnableExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = MobileApiClient.getJson(baseUrl, "/v1/me/extensions", token, "读取小程序列表");
            JSONArray items = payload.optJSONArray("items");
            ArrayList<RemoteExtension> result = new ArrayList<RemoteExtension>();
            if (items == null) {
                return result;
            }

            java.util.concurrent.ExecutorService pool = java.util.concurrent.Executors.newFixedThreadPool(8);
            List<java.util.concurrent.Future<RemoteExtension>> futures = new ArrayList<java.util.concurrent.Future<RemoteExtension>>();

            for (int i = 0; i < items.length(); ++i) {
                JSONObject item = items.optJSONObject(i);
                if (item == null || item.optInt("enabled", 1) == 0) continue;
                final String extensionId = MobileJson.firstNonEmpty(new String[]{item.optString("extension_id"), item.optString("extensionId"), item.optString("ExtensionId"), item.optString("Extension_id")});
                if (extensionId.isEmpty() || "yanzi-webdav-settings".equals(extensionId) || "yanzi-webdav-setting".equals(extensionId) || "yanzi-quickpanel-settings".equals(extensionId) || "yanzi-quickpanel-setting".equals(extensionId) || "yanzi-personal-sync-settings".equals(extensionId) || "yanzi-personal-sync-setting".equals(extensionId) || "yanzi-ai-settings".equals(extensionId) || "yanzi-ai-setting".equals(extensionId) || "yanzi-general-settings".equals(extensionId) || "yanzi-general-setting".equals(extensionId)) continue;
                final RemoteExtension installedSummary = MobileApiClient.remoteExtensionFromInstalledItem(item, extensionId);

                futures.add(pool.submit(new java.util.concurrent.Callable<RemoteExtension>() {
                    @Override
                    public RemoteExtension call() {
                        try {
                            JSONObject detail = MobileApiClient.getJson(baseUrl, "/v1/extensions/" + MobileApiClient.encodePath(extensionId), token, "读取小程序详情");
                            JSONObject manifest = detail.optJSONObject("manifest");
                            String name = MobileJson.firstNonEmpty(new String[]{detail.optString("display_name"), detail.optString("displayName"), detail.optString("DisplayName"), detail.optString("name"), detail.optString("Name"), manifest == null ? "" : manifest.optString("name"), manifest == null ? "" : manifest.optString("Name"), manifest == null ? "" : manifest.optString("display_name"), manifest == null ? "" : manifest.optString("displayName"), manifest == null ? "" : manifest.optString("DisplayName"), installedSummary.name, extensionId});
                            String description = MobileJson.firstNonEmpty(new String[]{detail.optString("description"), detail.optString("Description"), manifest == null ? "" : manifest.optString("description"), manifest == null ? "" : manifest.optString("Description"), installedSummary.description});
                            String icon = MobileJson.firstNonEmpty(new String[]{detail.optString("icon"), detail.optString("Icon"), manifest == null ? "" : manifest.optString("icon"), manifest == null ? "" : manifest.optString("Icon"), installedSummary.icon});
                            String accentHex = MobileJson.firstNonEmpty(new String[]{detail.optString("accent_hex"), detail.optString("accentHex"), detail.optString("AccentHex"), manifest == null ? "" : manifest.optString("accent_hex"), manifest == null ? "" : manifest.optString("accentHex"), manifest == null ? "" : manifest.optString("AccentHex"), installedSummary.accentHex});
                            return new RemoteExtension(extensionId, name, description, icon, accentHex);
                        }
                        catch (Exception ignored) {
                            return installedSummary;
                        }
                    }
                }));
            }

            for (java.util.concurrent.Future<RemoteExtension> future : futures) {
                try {
                    result.add(future.get());
                }
                catch (Exception ignored) {}
            }
            pool.shutdown();
            return result;
        }

        static RemoteExtension remoteExtensionFromInstalledItem(JSONObject item, String extensionId) {
            JSONObject settings = null;
            try {
                String settingsJson = item.optString("settings_json", "");
                if (!settingsJson.trim().isEmpty()) {
                    settings = new JSONObject(settingsJson);
                } else {
                    settings = item.optJSONObject("settings");
                }
            }
            catch (Exception ignored) {}
            JSONObject manifest = settings == null ? null : settings.optJSONObject("manifest");
            String name = MobileJson.firstNonEmpty(new String[]{
                item.optString("display_name"), item.optString("displayName"), item.optString("name"),
                settings == null ? "" : settings.optString("display_name"),
                settings == null ? "" : settings.optString("displayName"),
                settings == null ? "" : settings.optString("name"),
                settings == null ? "" : settings.optString("title"),
                manifest == null ? "" : manifest.optString("displayName"),
                manifest == null ? "" : manifest.optString("name"),
                extensionId
            });
            String description = MobileJson.firstNonEmpty(new String[]{
                item.optString("description"),
                settings == null ? "" : settings.optString("description"),
                manifest == null ? "" : manifest.optString("description"),
                "小程序详情暂不可用，仍可尝试远程执行。"
            });
            String icon = MobileJson.firstNonEmpty(new String[]{
                item.optString("icon"),
                settings == null ? "" : settings.optString("icon"),
                manifest == null ? "" : manifest.optString("icon")
            });
            String accentHex = MobileJson.firstNonEmpty(new String[]{
                item.optString("accent_hex"), item.optString("accentHex"),
                settings == null ? "" : settings.optString("accent_hex"),
                settings == null ? "" : settings.optString("accentHex"),
                manifest == null ? "" : manifest.optString("accent_hex"),
                manifest == null ? "" : manifest.optString("accentHex")
            });
            return new RemoteExtension(extensionId, name, description, icon, accentHex);
        }

        static boolean objectSyncAvailable(String baseUrl, String token) throws Exception {
            return MobileObjectRepository.objectSyncAvailable(baseUrl, token);
        }

        static HashMap<String, JSONObject> fetchSyncObjectMap(String baseUrl, String token) throws Exception {
            return MobileObjectRepository.fetchSyncObjectMap(baseUrl, token);
        }

        static long syncObjectRevision(HashMap<String, JSONObject> objectMap, String objectId) {
            return MobileObjectRepository.syncObjectRevision(objectMap, objectId);
        }

        static JSONObject putSyncObject(
                String baseUrl,
                String token,
                String objectId,
                long expectedRevision,
                boolean deleted,
                JSONObject payload,
                String action) throws Exception {
            return MobileObjectRepository.putSyncObject(baseUrl, token, objectId, expectedRevision, deleted, payload, action);
        }

        static String buildYanmStateObjectId(String stateKey) throws Exception {
            return MobileYanmController.buildYanmStateObjectId(stateKey);
        }

        static JSONObject fetchYanmStateFromObjects(String baseUrl, String token) throws Exception {
            return MobileYanmController.fetchYanmStateFromObjects(baseUrl, token);
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            return MobileYanmController.fetchYanmState(baseUrl, token);
        }

        static JSONObject fetchSettings(String baseUrl, String token) throws Exception {
            return MobileYanmController.fetchSettings(baseUrl, token);
        }

        static boolean putYanmStateToObjects(String baseUrl, String token, JSONObject yanm) throws Exception {
            return MobileYanmController.putYanmStateToObjects(baseUrl, token, yanm);
        }

        static boolean putYanmComponentStateToObjects(String baseUrl, String token, JSONObject componentState) throws Exception {
            return MobileYanmController.putYanmComponentStateToObjects(baseUrl, token, componentState);
        }

        static JSONObject putYanmState(String baseUrl, String token, JSONObject yanm) throws Exception {
            return MobileYanmController.putYanmState(baseUrl, token, yanm);
        }

        static JSONObject putYanmComponentState(String baseUrl, String token, JSONObject componentState) throws Exception {
            return MobileYanmController.putYanmComponentState(baseUrl, token, componentState);
        }
        static String fetchPersonalConfig(String baseUrl, String token) throws Exception {
            JSONObject payload = MobileApiClient.getJson(baseUrl, "/v1/sync/personal-config", token, "\u8bfb\u53d6\u540c\u6b65\u914d\u7f6e");
            return payload.toString();
        }

        static String fetchMobileExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = MobileApiClient.getJson(baseUrl, "/v1/me/mobile/extensions", token, "\u8bfb\u53d6\u624b\u673a\u6269\u5c55");
            return payload.optString("extensions", "[]");
        }

        static void putMobileExtensions(String baseUrl, String token, String extensionsJson) throws Exception {
            JSONObject payload = new JSONObject().put("extensions", (Object)extensionsJson);
            MobileApiClient.putJson(baseUrl, "/v1/me/mobile/extensions", payload, token, "\u540c\u6b65\u624b\u673a\u6269\u5c55");
        }

        static byte[] getWebDavBytes(WebDavConfig config, String relativePath) throws Exception {
            HttpURLConnection connection = MobileApiClient.openWebDav(config, relativePath);
            connection.setRequestMethod("GET");
            int status = connection.getResponseCode();
            if (status == 404) {
                return null;
            }
            if (status < 200 || status >= 300) {
                throw new IllegalStateException("WebDAV GET failed: " + status);
            }
            InputStream is = connection.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int len;
            while ((len = is.read(buffer)) != -1) {
                bos.write(buffer, 0, len);
            }
            is.close();
            return bos.toByteArray();
        }

        static String fetchFileFromGitHub(String token, String owner, String repo, String branch, String relativePath) throws Exception {
            String urlStr = "https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Authorization", "Bearer " + token.trim());
            conn.setRequestProperty("Accept", "application/vnd.github.raw");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");

            int code = conn.getResponseCode();
            if (code == 404) {
                return "[]";
            }
            if (code < 200 || code >= 300) {
                throw new java.io.IOException("GitHub read failed: " + code);
            }
            InputStream is = conn.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int len;
            while ((len = is.read(buf)) != -1) {
                bos.write(buf, 0, len);
            }
            is.close();
            return bos.toString("UTF-8");
        }

        static void uploadFileToGitHub(String token, String owner, String repo, String branch, String relativePath, String content) throws Exception {
            String sha = null;
            String urlStr = "https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Authorization", "Bearer " + token.trim());
            conn.setRequestProperty("Accept", "application/json");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");

            int code = conn.getResponseCode();
            if (code == 200) {
                InputStream is = conn.getInputStream();
                ByteArrayOutputStream bos = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int len;
                while ((len = is.read(buf)) != -1) {
                    bos.write(buf, 0, len);
                }
                is.close();
                JSONObject res = new JSONObject(bos.toString("UTF-8"));
                sha = res.optString("sha", null);
            }

            HttpURLConnection putConn = (HttpURLConnection) new URL("https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath)).openConnection();
            putConn.setRequestMethod("PUT");
            putConn.setRequestProperty("Authorization", "Bearer " + token.trim());
            putConn.setRequestProperty("Content-Type", "application/json");
            putConn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            putConn.setDoOutput(true);

            JSONObject payload = new JSONObject();
            payload.put("message", "Sync mobile-extensions.json from Mobile");
            String base64Content = Base64.encodeToString(content.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
            payload.put("content", base64Content);
            if (sha != null) {
                payload.put("sha", sha);
            }
            payload.put("branch", branch);

            OutputStream os = putConn.getOutputStream();
            os.write(payload.toString().getBytes(StandardCharsets.UTF_8));
            os.flush();
            os.close();

            int putCode = putConn.getResponseCode();
            if (putCode < 200 || putCode >= 300) {
                throw new java.io.IOException("GitHub write failed: " + putCode);
            }
        }

        static String fetchFileFromGitee(String token, String owner, String repo, String branch, String relativePath) throws Exception {
            String urlStr = "https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?access_token=" + token.trim() + "&ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");

            int code = conn.getResponseCode();
            if (code == 404) {
                return "[]";
            }
            if (code < 200 || code >= 300) {
                throw new java.io.IOException("Gitee read failed: " + code);
            }
            InputStream is = conn.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int len;
            while ((len = is.read(buf)) != -1) {
                bos.write(buf, 0, len);
            }
            is.close();
            JSONObject res = new JSONObject(bos.toString("UTF-8"));
            String contentBase64 = res.optString("content", "");
            if (contentBase64.isEmpty()) {
                return "[]";
            }
            byte[] decoded = Base64.decode(contentBase64, Base64.DEFAULT);
            return new String(decoded, StandardCharsets.UTF_8);
        }

        static void uploadFileToGitee(String token, String owner, String repo, String branch, String relativePath, String content) throws Exception {
            String sha = null;
            String urlStr = "https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?access_token=" + token.trim() + "&ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");

            int code = conn.getResponseCode();
            if (code == 200) {
                InputStream is = conn.getInputStream();
                ByteArrayOutputStream bos = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int len;
                while ((len = is.read(buf)) != -1) {
                    bos.write(buf, 0, len);
                }
                is.close();
                JSONObject res = new JSONObject(bos.toString("UTF-8"));
                sha = res.optString("sha", null);
            }

            HttpURLConnection putConn = (HttpURLConnection) new URL("https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath)).openConnection();
            putConn.setRequestMethod("PUT");
            putConn.setRequestProperty("Content-Type", "application/json");
            putConn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            putConn.setDoOutput(true);

            JSONObject payload = new JSONObject();
            payload.put("access_token", token.trim());
            payload.put("message", "Sync mobile-extensions.json from Mobile");
            String base64Content = Base64.encodeToString(content.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
            payload.put("content", base64Content);
            if (sha != null) {
                payload.put("sha", sha);
            }
            payload.put("branch", branch);

            OutputStream os = putConn.getOutputStream();
            os.write(payload.toString().getBytes(StandardCharsets.UTF_8));
            os.flush();
            os.close();

            int putCode = putConn.getResponseCode();
            if (putCode < 200 || putCode >= 300) {
                throw new java.io.IOException("Gitee write failed: " + putCode);
            }
        }

        static JSONObject putJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            if (MobileApiClient.isDesktopLocalApi(path)) {
                return MobileApiClient.requestDesktopLocalApi(path, token, action, "PUT", payload);
            }
            if (!sLanFailedThisSession && MobileApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanApiToken(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = MobileApiClient.doRequestLan(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "PUT", payload, timeoutMs);
                        MobileApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        MobileApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return MobileApiClient.doRequest(baseUrl, path, token, action, "PUT", payload, 15000);
        }

        static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            if ("/v1/me/mobile/messages".equals(path)) return MobileDeviceMessageSender.send(baseUrl, token, payload);
            if ("/v1/me/mobile/messages".equals(path) && !payload.has("clientMessageId"))
                payload.put("clientMessageId", java.util.UUID.randomUUID().toString());
            if (MobileApiClient.isDesktopLocalApi(path)) {
                return MobileApiClient.requestDesktopLocalApi(path, token, action, "POST", payload);
            }
            if (!sLanFailedThisSession && MobileApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanApiToken(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = MobileApiClient.doRequestLan(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "POST", payload, timeoutMs);
                        MobileApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        MobileApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return MobileApiClient.doRequest(baseUrl, path, token, action, "POST", payload, 15000);
        }

        static JSONObject getJson(String baseUrl, String path, String token, String action) throws Exception {
            if (MobileApiClient.isDesktopLocalApi(path)) {
                return MobileApiClient.requestDesktopLocalApi(path, token, action, "GET", null);
            }
            if (!sLanFailedThisSession && MobileApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanApiToken(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = MobileApiClient.doRequestLan(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "GET", null, timeoutMs);
                        MobileApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        MobileApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return MobileApiClient.doRequest(baseUrl, path, token, action, "GET", null, 15000);
        }

        static boolean shouldUseLan(String path) {
            return MobileLanPolicy.supports(path);
        }

        static boolean isDesktopLocalApi(String path) {
            return path.startsWith("/v1/fs/") || path.equals("/v1/shell/run");
        }

        static String getDeviceIdStatic(Context context) {
            if (context != null) {
                SharedPreferences p = context.getSharedPreferences("yanzi-mobile", 0);
                String id = p.getString("deviceId", null);
                if (id != null && !id.trim().isEmpty()) return id;
                String created = "android-" + java.util.UUID.randomUUID();
                p.edit().putString("deviceId", created).apply();
                return created;
            }
            return "android-mobile-fallback";
        }

        static String extractRelayResultPayload(JSONObject detail) {
            if (detail == null) return "";
            JSONObject payload = detail.optJSONObject("payload");
            if (payload != null) {
                JSONObject execResult = payload.optJSONObject("executionResult");
                if (execResult != null) {
                    String output = execResult.optString("output", "");
                    if (!output.trim().isEmpty()) return output;
                }
                String payloadResp = payload.optString("responsePayload", payload.optString("output", ""));
                if (!payloadResp.trim().isEmpty()) return payloadResp;
            }
            String resp = detail.optString("responsePayload", detail.optString("ackedOutput", detail.optString("result", "")));
            if (!resp.trim().isEmpty()) return resp;
            return "";
        }

        static JSONObject requestCloudRelayApi(String path, String token, String action, String method, JSONObject payload) throws Exception {
            long startTime = System.currentTimeMillis();
            android.util.Log.i("YanziRelay", "==> [START] requestCloudRelayApi: path=" + path + ", action=" + action);
            String baseUrl = "https://sync.luoluoluo.cc.cd";
            String deviceId = getDeviceIdStatic(MobileApplicationContext.get());
            String deviceName = MobileDeviceIdentity.buildDeviceDisplayName();
            try {
                MobileApiClient.registerDevice(baseUrl, token, deviceId, deviceName);
                android.util.Log.i("YanziRelay", "--> Registered device: id=" + deviceId + " (" + (System.currentTimeMillis() - startTime) + "ms)");
            } catch (Exception regEx) {
                android.util.Log.w("YanziRelay", "--> Register device failed: " + regEx.getMessage());
            }

            String kind = "run-powershell";
            if (path.startsWith("/v1/fs/list")) {
                kind = "fs-list";
            } else if (path.startsWith("/v1/fs/read")) {
                kind = "fs-read";
            } else if (path.startsWith("/v1/fs/write")) {
                kind = "fs-write";
            } else if (path.equals("/v1/shell/run")) {
                kind = "run-powershell";
            }

            String cmdText = payload == null ? "" : payload.optString("command", payload.optString("text", ""));
            JSONObject msgPayload = payload != null ? payload : new JSONObject().put("path", (Object)path);

            JSONObject relayPayload = new JSONObject()
                .put("sourceDeviceId", (Object)deviceId)
                .put("targetPlatform", (Object)"desktop")
                .put("kind", (Object)kind)
                .put("title", (Object)action)
                .put("text", (Object)cmdText)
                .put("payload", (Object)msgPayload);

            long postStart = System.currentTimeMillis();
            String targetDeviceId = msgPayload.optString("targetDeviceId", LanDiscoveryManager.getLanDeviceId(MobileApplicationContext.get()));
            if (!targetDeviceId.isEmpty()) relayPayload.put("targetDeviceId", targetDeviceId);
            relayPayload.put("clientMessageId", msgPayload.optString("clientOperationId", java.util.UUID.randomUUID().toString()));
            JSONObject postRes = MobileMessageClient.request(baseUrl, "/v1/me/mobile/messages", token, "POST", relayPayload);
            String messageId = postRes.optString("messageId", "");
            android.util.Log.i("YanziRelay", "--> Post relay message OK: messageId=" + messageId + " (" + (System.currentTimeMillis() - postStart) + "ms)");

            if (messageId.isEmpty()) {
                throw new IllegalStateException("中继消息投递失败");
            }

            long pollStart = System.currentTimeMillis();
            for (int attempt = 0; attempt < 35; attempt++) {
                int sleepMs = attempt < 15 ? 150 : 350;
                Thread.sleep(sleepMs);
                try {
                    JSONObject detail = MobileMessageClient.request(baseUrl, "/v1/me/mobile/messages/" + messageId, token, "GET", null);
                    String status = detail.optString("status", "");
                    String respPayload = extractRelayResultPayload(detail);
                    android.util.Log.i("YanziRelay", "--> Poll attempt #" + attempt + ": status=" + status + ", payloadLength=" + respPayload.length() + " (" + (System.currentTimeMillis() - pollStart) + "ms)");

                    if ("completed".equalsIgnoreCase(status) || "acked".equalsIgnoreCase(status) || "executed".equalsIgnoreCase(status) || !respPayload.isEmpty()) {
                        if (!respPayload.trim().isEmpty()) {
                            android.util.Log.i("YanziRelay", "<== [SUCCESS] Relay completed in " + (System.currentTimeMillis() - startTime) + "ms total!");
                            try {
                                return new JSONObject(respPayload);
                            } catch (Exception e) {
                                return new JSONObject().put("output", (Object)respPayload).put("exitCode", 0);
                            }
                        }
                    }
                } catch (Exception pollEx) {
                    android.util.Log.w("YanziRelay", "--> Poll attempt #" + attempt + " error: " + pollEx.getMessage());
                }
            }
            throw new IllegalStateException("远程设备未响应，请确认电脑处于在线状态。");
        }

        static JSONObject requestDesktopLocalApi(String path, String token, String action, String method, JSONObject payload) throws Exception {
            long lanStart = System.currentTimeMillis();
            payload = payload == null ? new JSONObject().put("path", path) : new JSONObject(payload.toString());
            if (!payload.has("clientOperationId")) payload.put("clientOperationId", java.util.UUID.randomUUID().toString());
            String kind = path.startsWith("/v1/fs/list") ? "fs-list" : path.startsWith("/v1/fs/read") ? "fs-read" :
                    path.startsWith("/v1/fs/write") ? "fs-write" : "run-powershell";
            String lanBaseUrl = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanBaseUrl;
            android.util.Log.i("YanziRelay", "==> requestDesktopLocalApi: lanBaseUrl=" + lanBaseUrl);

            if (lanBaseUrl != null && !lanBaseUrl.trim().isEmpty() && !lanBaseUrl.contains("127.0.0.1")) {
                try {
                    String lanToken = MobileApplicationContext.get() != null ? LanDiscoveryManager.getLanApiToken(MobileApplicationContext.get()) : LanDiscoveryManager.cachedLanApiToken;
                    JSONObject message = new JSONObject().put("notificationPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981).put("clientMessageId", payload.getString("clientOperationId")).put("sourceDeviceId", getDeviceIdStatic(MobileApplicationContext.get())).put("targetPlatform", "desktop")
                            .put("kind", kind).put("title", action).put("text", payload.optString("command", "")).put("payload", payload);
                    JSONObject delivered = MobileApiClient.doRequestLan(lanBaseUrl, "/v1/me/mobile/messages",
                            lanToken != null ? lanToken : token, action, "POST", message, 65000);
                    if (!delivered.optBoolean("success")) throw new DesktopOperationRejected(delivered.optString("output", "电脑执行失败"));
                    String output = delivered.optString("output", "");
                    JSONObject result;
                    try { result = new JSONObject(output); }
                    catch (Exception ignored) { result = new JSONObject().put("output", output).put("exitCode", 0); }
                    MobileApiClient.handleLanSuccess(action, path);
                    android.util.Log.i("YanziRelay", "<== LAN Direct OK in " + (System.currentTimeMillis() - lanStart) + "ms");
                    return result;
                } catch (DesktopOperationRejected rejected) { throw rejected; }
                catch (Exception e) {
                    android.util.Log.i("YanziRelay", "--> LAN Direct failed (" + (System.currentTimeMillis() - lanStart) + "ms): " + e.getMessage());
                    LanDiscoveryManager.noteTransportFailure(e);
                }
            }

            android.util.Log.i("YanziRelay", "--> Fallback to Cloud Relay");
            try {
                return requestCloudRelayApi(path, token, action, method, payload);
            } catch (Exception relayEx) {
                throw new IllegalStateException("远程中继响应失败：" + relayEx.getMessage(), relayEx);
            }
        }

        static final class DesktopOperationRejected extends Exception {
            DesktopOperationRejected(String message) { super(message); }
        }

        static String toUserMessage(Exception ex) {
            if (ex == null) {
                return "未知错误";
            }
            String message = ex.getMessage();
            if (message == null || message.trim().isEmpty()) {
                message = ex.toString();
            }
            return message;
        }

        static void handleLanSuccess(String action, String path) {
            LanDiscoveryManager.noteSuccess();
            if (MobileApplicationContext.get() != null) {
                MobileDiagnostics.append(MobileApplicationContext.get(), "\u5c40\u57df\u7f51\u76f4\u8fde\u6210\u529f(" + action + "): " + path);
            }
        }

        static void handleLanFailure(String action, Exception e) {
            LanDiscoveryManager.noteTransportFailure(e);
            String message = e.getMessage() == null ? e.toString() : e.getMessage();
            Log.w((String)"ApiClient", (String)("LAN fallback failed: " + message));
            if (MobileApplicationContext.get() != null) {
                MobileDiagnostics.append(MobileApplicationContext.get(), "\u5c40\u57df\u7f51\u76f4\u8fde\u5931\u8d25(" + action + ")\uff0c\u5df2\u56de\u9000\u516c\u7f51\uff1a" + message);
            }
        }

        static JSONObject doRequest(String baseUrl, String path, String token, String action, String method, JSONObject payload, int timeoutMs) throws Exception {
            try {
                return MobileApiClient.doRequestOnce(
                        baseUrl,
                        path,
                        token,
                        action,
                        method,
                        payload,
                        timeoutMs,
                        null);
            }
            catch (Exception firstError) {
                if (!MobileApiClient.shouldRetryOutsideVpn(path, method, firstError)) {
                    throw firstError;
                }

                android.net.Network directNetwork =
                        MobileApiClient.findUnderlyingInternetNetwork();
                if (directNetwork == null) {
                    throw firstError;
                }

                Log.w(
                        "ApiClient",
                        "Cloud request transport failed; retrying over underlying network: "
                                + firstError.getMessage());

                try {
                    JSONObject result = MobileApiClient.doRequestOnce(
                            baseUrl,
                            path,
                            token,
                            action,
                            method,
                            payload,
                            timeoutMs,
                            directNetwork);
                    Log.i(
                            "ApiClient",
                            "Underlying-network retry succeeded for " + path);
                    return result;
                }
                catch (Exception retryError) {
                    Log.w(
                            "ApiClient",
                            "Underlying-network retry failed: "
                                    + retryError.getMessage());
                    throw firstError;
                }
            }
        }

        static JSONObject doRequestOnce(String baseUrl, String path, String token, String action,
                String method, JSONObject payload, int timeoutMs, android.net.Network network) throws Exception {
            return MobileApiTransport.request(baseUrl, path, token, action, method, payload, timeoutMs, network, false, MobileApiClient::formatError);
        }

        static JSONObject doRequestLan(String baseUrl, String path, String token, String action,
                String method, JSONObject payload, int timeoutMs) throws Exception {
            return MobileApiTransport.request(baseUrl, path, token, action, method, payload, timeoutMs, null, true, MobileApiClient::formatError);
        }

        static boolean shouldRetryOutsideVpn(
                String path,
                String method,
                Exception error) {
            if (MobileApplicationContext.get() == null || error == null) {
                return false;
            }

            String upperMethod = method == null
                    ? ""
                    : method.trim().toUpperCase(java.util.Locale.ROOT);
            boolean safeMethod = CloudRequestRetry.safe(upperMethod, path, false);
            if (!safeMethod) {
                return false;
            }

            return CloudRequestRetry.retryable(error);
        }

        static android.net.Network findUnderlyingInternetNetwork() {
            if (MobileApplicationContext.get() == null) {
                return null;
            }

            try {
                android.net.ConnectivityManager manager =
                        (android.net.ConnectivityManager)
                                MobileApplicationContext.get().getSystemService(Context.CONNECTIVITY_SERVICE);
                if (manager == null) {
                    return null;
                }

                for (android.net.Network network : manager.getAllNetworks()) {
                    android.net.NetworkCapabilities capabilities =
                            manager.getNetworkCapabilities(network);
                    if (capabilities == null) {
                        continue;
                    }

                    boolean internet = capabilities.hasCapability(
                            android.net.NetworkCapabilities.NET_CAPABILITY_INTERNET);
                    boolean notVpn = capabilities.hasCapability(
                            android.net.NetworkCapabilities.NET_CAPABILITY_NOT_VPN);
                    boolean physical =
                            capabilities.hasTransport(
                                    android.net.NetworkCapabilities.TRANSPORT_WIFI)
                                    || capabilities.hasTransport(
                                            android.net.NetworkCapabilities.TRANSPORT_CELLULAR);

                    if (internet && notVpn && physical) {
                        return network;
                    }
                }
            }
            catch (Exception ex) {
                Log.w(
                        "ApiClient",
                        "Failed to locate underlying network: "
                                + ex.getMessage());
            }
            return null;
        }

        static String encodePath(String value) {
            return value.replace(" ", "%20").replace("/", "%2F");
        }

        static String formatError(String action, String path, int statusCode, String message) {
            String trimmed;
            String string = trimmed = message == null ? "" : message.trim();
            if (statusCode == 404 && trimmed.toLowerCase().contains("route not found")) {
                return action + "\u63a5\u53e3\u4e0d\u5b58\u5728\uff0c\u8bf7\u786e\u8ba4\u4e91\u7aef\u5730\u5740\u662f " + MobileSessionStore.DEFAULT_BASE_URL + "\uff0c\u5e76\u786e\u8ba4 Worker \u5df2\u53d1\u5e03\u79fb\u52a8\u7aef\u63a5\u53e3\uff1a" + path;
            }
            if (trimmed.isEmpty()) {
                return action + "\u5931\u8d25\uff0cHTTP " + statusCode;
            }
            return trimmed;
        }

        static String readBody(HttpURLConnection connection) throws Exception {
            return HttpResponseBody.read(connection);
        }

        static final class WebDavConfig {
            String serverUrl;
            String rootPath;
            String username;
            String password;

            WebDavConfig() {
            }
        }
    }

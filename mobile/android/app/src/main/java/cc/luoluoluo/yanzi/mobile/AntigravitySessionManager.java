package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;

final class AntigravitySessionManager {
    static final class SessionItem {
        final String id;
        final String rawId;
        String title;
        String preview;
        long updatedAt;

        SessionItem(String id, String rawId, String title, String preview, long updatedAt) {
            this.id = id;
            this.rawId = rawId;
            this.title = title;
            this.preview = preview;
            this.updatedAt = updatedAt;
        }

        JSONObject toJson() {
            try {
                return new JSONObject()
                        .put("id", id)
                        .put("rawId", rawId)
                        .put("title", title)
                        .put("preview", preview)
                        .put("updatedAt", updatedAt);
            } catch (Exception e) {
                return new JSONObject();
            }
        }

        static SessionItem fromJson(JSONObject json) {
            if (json == null) return null;
            String id = json.optString("id", "");
            if (id.isEmpty()) return null;
            String rawId = json.optString("rawId", "");
            String title = json.optString("title", "未命名会话");
            String preview = json.optString("preview", "");
            long updatedAt = json.optLong("updatedAt", System.currentTimeMillis());
            return new SessionItem(id, rawId, title, preview, updatedAt);
        }
    }

    interface SessionsCallback {
        void onLoaded(List<SessionItem> sessions);
    }

    private static String prefsKey(String accountKey, String targetDeviceId) {
        return "antigravity_sessions." + accountKey + "." + targetDeviceId;
    }

    static List<SessionItem> getSessions(Context context, String targetDeviceId) {
        List<SessionItem> list = new ArrayList<>();
        if (targetDeviceId == null || targetDeviceId.isEmpty()) return list;
        SharedPreferences prefs = context.getSharedPreferences("YanziMobilePrefs", Context.MODE_PRIVATE);
        String accountKey = ChatHistoryStore.accountKey(context);
        String raw = prefs.getString(prefsKey(accountKey, targetDeviceId), "[]");
        try {
            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length(); i++) {
                SessionItem item = SessionItem.fromJson(arr.optJSONObject(i));
                if (item != null) list.add(item);
            }
        } catch (Exception ignored) {}

        if (list.isEmpty()) {
            String defaultId = "agent:antigravity:" + targetDeviceId;
            SessionItem def = new SessionItem(defaultId, "", "默认会话", "", System.currentTimeMillis());
            list.add(def);
            saveSessions(context, targetDeviceId, list);
        }
        return list;
    }

    static void saveSessions(Context context, String targetDeviceId, List<SessionItem> list) {
        if (targetDeviceId == null || targetDeviceId.isEmpty()) return;
        SharedPreferences prefs = context.getSharedPreferences("YanziMobilePrefs", Context.MODE_PRIVATE);
        String accountKey = ChatHistoryStore.accountKey(context);
        JSONArray arr = new JSONArray();
        for (SessionItem item : list) {
            arr.put(item.toJson());
        }
        prefs.edit().putString(prefsKey(accountKey, targetDeviceId), arr.toString()).apply();
    }

    static void fetchRemoteSessions(Context context, String baseUrl, String token, String targetDeviceId, SessionsCallback callback) {
        new Thread(() -> {
            try {
                // 1. 优先通过云端 ObjectSync 读取（无论 5G 还是 Wi-Fi 均可秒级拉取真实会话）
                if (baseUrl != null && !baseUrl.isEmpty() && token != null && !token.isEmpty()) {
                    try {
                        JSONObject storage = MobileExtensionStorageClient.read(baseUrl, token, "antigravity-chat", "conversations");
                        if (storage != null && storage.optBoolean("exists")) {
                            String content = storage.optString("content", "");
                            if (content.startsWith("[")) {
                                JSONArray arr = new JSONArray(content);
                                List<SessionItem> remoteList = new ArrayList<>();
                                for (int i = 0; i < arr.length(); i++) {
                                    JSONObject obj = arr.optJSONObject(i);
                                    if (obj == null) continue;
                                    String rawId = obj.optString("id", "");
                                    if (rawId.isEmpty()) continue;
                                    String fullId = "agent:antigravity:" + targetDeviceId + ":" + rawId;
                                    String title = obj.optString("title", "未命名会话");
                                    String preview = obj.optString("preview", "");
                                    long updatedAt = obj.optLong("updatedAtMs", 0);
                                    if (updatedAt <= 0) updatedAt = obj.optLong("updatedAt", System.currentTimeMillis());
                                    remoteList.add(new SessionItem(fullId, rawId, title, preview, updatedAt));
                                }
                                if (!remoteList.isEmpty()) {
                                    saveSessions(context, targetDeviceId, remoteList);
                                    if (callback != null) {
                                        new android.os.Handler(android.os.Looper.getMainLooper()).post(() -> callback.onLoaded(remoteList));
                                    }
                                    return;
                                }
                            }
                        }
                    } catch (Exception e) {
                        android.util.Log.d("AntigravitySession", "Cloud ObjectSync fetch error: " + e.getMessage());
                    }
                }

                // 2. 局域网 Direct API 兜底
                String lanBaseUrl = LanDiscoveryManager.getLanBaseUrl(context);
                String lanToken = LanDiscoveryManager.getLanApiToken(context);
                String jsonResult = null;

                if (lanBaseUrl != null && !lanBaseUrl.isEmpty()) {
                    try {
                        java.net.URL url = new java.net.URL(lanBaseUrl + "/v1/extensions/antigravity-chat/run");
                        java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
                        conn.setRequestMethod("POST");
                        conn.setConnectTimeout(2500);
                        conn.setReadTimeout(5000);
                        conn.setRequestProperty("Content-Type", "application/json");
                        if (lanToken != null && !lanToken.isEmpty()) {
                            conn.setRequestProperty("Authorization", "Bearer " + lanToken);
                        }
                        conn.setDoOutput(true);
                        JSONObject body = new JSONObject().put("input", "/会话列表");
                        try (java.io.OutputStream os = conn.getOutputStream()) {
                            os.write(body.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
                        }
                        if (conn.getResponseCode() == 200) {
                            try (java.io.InputStream is = conn.getInputStream();
                                 java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream()) {
                                byte[] buf = new byte[4096];
                                int len;
                                while ((len = is.read(buf)) > 0) bos.write(buf, 0, len);
                                JSONObject resp = new JSONObject(bos.toString("UTF-8"));
                                if (resp.optBoolean("success")) {
                                    jsonResult = resp.optString("output", "");
                                }
                            }
                        }
                        conn.disconnect();
                    } catch (Exception e) {
                        android.util.Log.d("AntigravitySession", "LAN fetch error: " + e.getMessage());
                    }
                }

                if (jsonResult != null && jsonResult.startsWith("[")) {
                    JSONArray arr = new JSONArray(jsonResult);
                    List<SessionItem> remoteList = new ArrayList<>();
                    for (int i = 0; i < arr.length(); i++) {
                        JSONObject obj = arr.optJSONObject(i);
                        if (obj == null) continue;
                        String rawId = obj.optString("id", "");
                        if (rawId.isEmpty()) continue;
                        String fullId = "agent:antigravity:" + targetDeviceId + ":" + rawId;
                        String title = obj.optString("title", "未命名会话");
                        String preview = obj.optString("preview", "");
                        long updatedAt = obj.optLong("updatedAtMs", 0);
                        if (updatedAt <= 0) {
                            updatedAt = obj.optLong("updatedAt", System.currentTimeMillis());
                        }
                        remoteList.add(new SessionItem(fullId, rawId, title, preview, updatedAt));
                    }
                    if (!remoteList.isEmpty()) {
                        saveSessions(context, targetDeviceId, remoteList);
                        if (callback != null) {
                            new android.os.Handler(android.os.Looper.getMainLooper()).post(() -> callback.onLoaded(remoteList));
                        }
                        return;
                    }
                }
            } catch (Exception e) {
                android.util.Log.w("AntigravitySession", "fetchRemoteSessions failed", e);
            }
            if (callback != null) {
                List<SessionItem> cached = getSessions(context, targetDeviceId);
                new android.os.Handler(android.os.Looper.getMainLooper()).post(() -> callback.onLoaded(cached));
            }
        }).start();
    }

    static SessionItem createNewSession(Context context, String targetDeviceId) {
        List<SessionItem> list = getSessions(context, targetDeviceId);
        long now = System.currentTimeMillis();
        String newId = "agent:antigravity:" + targetDeviceId + ":s_" + now;
        SessionItem newSession = new SessionItem(newId, "s_" + now, "新会话", "", now);
        list.add(0, newSession);
        saveSessions(context, targetDeviceId, list);
        return newSession;
    }

    static String getSessionTitle(Context context, String targetDeviceId, String sessionId) {
        if (sessionId == null || sessionId.isEmpty()) return "默认会话";
        if (sessionId.equals("agent:antigravity:" + targetDeviceId)) return "默认会话";
        List<SessionItem> list = getSessions(context, targetDeviceId);
        for (SessionItem item : list) {
            if (item.id.equals(sessionId) || (sessionId.endsWith(":" + item.rawId) && !item.rawId.isEmpty())) {
                return item.title;
            }
        }
        if (sessionId.contains(":s_")) return "新会话";
        return "历史会话";
    }

    static void updateTitleIfDefault(Context context, String targetDeviceId, String sessionId, String firstMessage) {
        if (firstMessage == null || firstMessage.trim().isEmpty()) return;
        List<SessionItem> list = getSessions(context, targetDeviceId);
        boolean changed = false;
        String cleanMsg = firstMessage.trim().replaceAll("[\\r\\n]+", " ");
        String newTitle = cleanMsg.length() > 14 ? cleanMsg.substring(0, 14) + "..." : cleanMsg;
        for (SessionItem item : list) {
            if (item.id.equals(sessionId)) {
                if ("新会话".equals(item.title) || "未命名会话".equals(item.title) || item.title.isEmpty()) {
                    item.title = newTitle;
                    item.updatedAt = System.currentTimeMillis();
                    changed = true;
                }
                break;
            }
        }
        if (changed) {
            saveSessions(context, targetDeviceId, list);
        }
    }

    static void deleteSession(Context context, String targetDeviceId, String sessionId) {
        List<SessionItem> list = getSessions(context, targetDeviceId);
        list.removeIf(item -> item.id.equals(sessionId));
        saveSessions(context, targetDeviceId, list);
    }

    static void fetchConversationHistory(Context context, String baseUrl, String token, String targetDeviceId, String conversationId, Runnable onComplete) {
        if (conversationId == null || !conversationId.startsWith("agent:antigravity:")) return;
        String[] parts = conversationId.split(":");
        final String rawId = parts.length >= 4 ? parts[3] : "";
        if (rawId.isEmpty() || rawId.startsWith("s_")) return;

        new Thread(() -> {
            boolean imported = false;
            try {
                // 1. 优先通过云端 ObjectSync 读取该会话历史（秒级，无论 5G 还是 Wi-Fi）
                if (baseUrl != null && !baseUrl.isEmpty() && token != null && !token.isEmpty()) {
                    try {
                        JSONObject storage = MobileExtensionStorageClient.read(baseUrl, token, "antigravity-chat", "history." + rawId);
                        if (storage != null && storage.optBoolean("exists")) {
                            String content = storage.optString("content", "");
                            if (content.startsWith("[")) {
                                JSONArray arr = new JSONArray(content);
                                imported = importHistoryMessages(context, conversationId, rawId, arr);
                            }
                        }
                    } catch (Exception e) {
                        android.util.Log.d("AntigravitySession", "Cloud history fetch error: " + e.getMessage());
                    }
                }

                // 2. 局域网 Direct API 兜底
                if (!imported) {
                    String lanBaseUrl = LanDiscoveryManager.getLanBaseUrl(context);
                    String lanToken = LanDiscoveryManager.getLanApiToken(context);
                    if (lanBaseUrl != null && !lanBaseUrl.isEmpty()) {
                        try {
                            java.net.URL url = new java.net.URL(lanBaseUrl + "/v1/extensions/antigravity-chat/run");
                            java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
                            conn.setRequestMethod("POST");
                            conn.setConnectTimeout(2500);
                            conn.setReadTimeout(5000);
                            conn.setRequestProperty("Content-Type", "application/json");
                            if (lanToken != null && !lanToken.isEmpty()) {
                                conn.setRequestProperty("Authorization", "Bearer " + lanToken);
                            }
                            conn.setDoOutput(true);
                            JSONObject body = new JSONObject().put("input", "/历史 " + rawId);
                            try (java.io.OutputStream os = conn.getOutputStream()) {
                                os.write(body.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
                            }
                            if (conn.getResponseCode() == 200) {
                                try (java.io.InputStream is = conn.getInputStream();
                                     java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream()) {
                                    byte[] buf = new byte[4096];
                                    int len;
                                    while ((len = is.read(buf)) > 0) bos.write(buf, 0, len);
                                    JSONObject resp = new JSONObject(bos.toString("UTF-8"));
                                    if (resp.optBoolean("success")) {
                                        String out = resp.optString("output", "");
                                        if (out.startsWith("[")) {
                                            JSONArray arr = new JSONArray(out);
                                            imported = importHistoryMessages(context, conversationId, rawId, arr);
                                        }
                                    }
                                }
                            }
                            conn.disconnect();
                        } catch (Exception e) {
                            android.util.Log.d("AntigravitySession", "LAN history fetch error: " + e.getMessage());
                        }
                    }
                }
            } catch (Exception error) {
                android.util.Log.w("AntigravitySession", "fetchConversationHistory failed", error);
            }

            if (imported && onComplete != null) {
                new android.os.Handler(android.os.Looper.getMainLooper()).post(onComplete);
            }
        }).start();
    }

    private static boolean importHistoryMessages(Context context, String conversationId, String rawId, JSONArray arr) {
        if (arr == null || arr.length() == 0) return false;
        boolean addedAny = false;
        for (int i = 0; i < arr.length(); i++) {
            JSONObject item = arr.optJSONObject(i);
            if (item == null) continue;
            String role = item.optString("role", "peer");
            String kind = item.optString("kind", "text");
            String content = item.optString("content", "");
            long time = item.optLong("time", System.currentTimeMillis());
            if (content.isEmpty()) continue;
            String msgId = "hist_" + rawId.substring(0, Math.min(8, rawId.length())) + "_" + i + "_" + time;
            JSONObject msg = ChatHistoryStore.message(role, kind, content, time, msgId, "desktop-chat", "反重力");
            try {
                msg.put("conversationId", conversationId);
            } catch (Exception ignored) {}
            if (ChatHistoryStore.append(context, msg)) {
                addedAny = true;
            }
        }
        return addedAny;
    }
}

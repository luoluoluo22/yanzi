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
        String title;
        long updatedAt;

        SessionItem(String id, String title, long updatedAt) {
            this.id = id;
            this.title = title;
            this.updatedAt = updatedAt;
        }

        JSONObject toJson() {
            try {
                return new JSONObject()
                        .put("id", id)
                        .put("title", title)
                        .put("updatedAt", updatedAt);
            } catch (Exception e) {
                return new JSONObject();
            }
        }

        static SessionItem fromJson(JSONObject json) {
            if (json == null) return null;
            String id = json.optString("id", "");
            if (id.isEmpty()) return null;
            String title = json.optString("title", "未命名会话");
            long updatedAt = json.optLong("updatedAt", System.currentTimeMillis());
            return new SessionItem(id, title, updatedAt);
        }
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
            SessionItem def = new SessionItem(defaultId, "默认会话", System.currentTimeMillis());
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

    static SessionItem createNewSession(Context context, String targetDeviceId) {
        List<SessionItem> list = getSessions(context, targetDeviceId);
        long now = System.currentTimeMillis();
        String newId = "agent:antigravity:" + targetDeviceId + ":s_" + now;
        SessionItem newSession = new SessionItem(newId, "新会话", now);
        list.add(0, newSession);
        saveSessions(context, targetDeviceId, list);
        return newSession;
    }

    static String getSessionTitle(Context context, String targetDeviceId, String sessionId) {
        List<SessionItem> list = getSessions(context, targetDeviceId);
        for (SessionItem item : list) {
            if (item.id.equals(sessionId)) {
                return item.title;
            }
        }
        return "新会话";
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
}

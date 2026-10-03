package cc.luoluoluo.yanzi.mobile;

import java.util.HashMap;
import org.json.JSONArray;
import org.json.JSONObject;

import static cc.luoluoluo.yanzi.mobile.MobileApiClient.*;

final class MobileObjectRepository {
        static boolean objectSyncAvailable(String baseUrl, String token) throws Exception {
            JSONObject capabilities = MobileApiClient.getJson(
                    baseUrl,
                    "/v1/sync/capabilities",
                    token,
                    "读取同步能力");
            return capabilities.optBoolean("objectSyncAvailable", false);
        }

        static HashMap<String, JSONObject> fetchSyncObjectMap(String baseUrl, String token) throws Exception {
            JSONObject response = MobileApiClient.getJson(
                    baseUrl,
                    "/v1/sync/objects",
                    token,
                    "读取同步对象");
            HashMap<String, JSONObject> objectMap = new HashMap<String, JSONObject>();
            String userId = response.optString("userId", "");
            long cursor = 0;
            while (true) {
            if (!response.optBoolean("ok", false) || userId.isEmpty() || !userId.equals(response.optString("userId", "")))
                throw new IllegalStateException("Invalid sync response or account changed");
            JSONArray objects = response.optJSONArray("objects");
            if (objects == null) {
                throw new IllegalStateException("云端对象列表为空。");
            }

            for (int i = 0; i < objects.length(); ++i) {
                JSONObject item = objects.optJSONObject(i);
                if (item == null) continue;
                String objectId = item.optString("objectId", "").trim();
                if (!objectId.isEmpty()) {
                    objectMap.put(objectId, item);
                }
            }
            if (!response.optBoolean("hasMore", false)) return objectMap;
            long nextCursor = response.optLong("cursorRevision", 0);
            if (nextCursor <= cursor) throw new IllegalStateException("Sync page did not advance");
            cursor = nextCursor;
            response = MobileApiClient.getJson(baseUrl, "/v1/sync/changes?since=" + cursor + "&limit=500", token, "读取同步对象分页");
            }
        }

        static long syncObjectRevision(HashMap<String, JSONObject> objectMap, String objectId) {
            JSONObject item = objectMap.get(objectId);
            return item == null ? 0L : item.optLong("revision", 0L);
        }

        static JSONObject putSyncObject(
                String baseUrl,
                String token,
                String objectId,
                long expectedRevision,
                boolean deleted,
                JSONObject payload,
                String action) throws Exception {
            JSONObject body = new JSONObject()
                    .put("schemaVersion", 1)
                    .put("expectedRevision", expectedRevision)
                    .put("deleted", deleted)
                    .put("payload", payload == null ? new JSONObject() : payload)
                    .put("updatedByDeviceId", getDeviceIdStatic(MobileApplicationContext.get()))
                    .put("updatedByDeviceName", MobileDeviceIdentity.buildDeviceDisplayName());
            JSONObject response = MobileApiClient.putJson(
                    baseUrl,
                    "/v1/sync/objects/" + MobileApiClient.encodePath(objectId),
                    body,
                    token,
                    action);
            JSONObject result = response.optJSONObject("object");
            if (result == null) {
                throw new IllegalStateException("云端未返回写入后的同步对象。");
            }
            return result;
        }

}

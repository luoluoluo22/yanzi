package cc.luoluoluo.yanzi.mobile;

import android.util.Log;
import java.util.ArrayList;
import cc.luoluoluo.yanzi.mobile.MobileDiagnostics;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Locale;
import org.json.JSONArray;
import org.json.JSONObject;

import static cc.luoluoluo.yanzi.mobile.MobileApiClient.*;
import static cc.luoluoluo.yanzi.mobile.MobileObjectRepository.*;

final class MobileYanmController {
        static String buildYanmStateObjectId(String stateKey) throws Exception {
            String normalized = stateKey == null ? "" : stateKey.trim().toLowerCase(Locale.ROOT);
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(normalized.getBytes(StandardCharsets.UTF_8));
            StringBuilder builder = new StringBuilder("yanm.componentState.");
            for (byte value : hash) {
                builder.append(String.format(Locale.ROOT, "%02x", value & 0xff));
            }
            return builder.toString();
        }

        static JSONObject fetchYanmStateFromObjects(String baseUrl, String token) throws Exception {
            JSONObject capabilities = MobileApiClient.getJson(
                    baseUrl,
                    "/v1/sync/capabilities",
                    token,
                    "读取同步能力");
            if (!capabilities.optBoolean("objectSyncAvailable", false)) {
                throw new IllegalStateException("云端尚未启用对象同步。");
            }

            JSONObject response = MobileApiClient.getJson(
                    baseUrl,
                    "/v1/sync/objects",
                    token,
                    "读取燕幕对象");
            JSONArray objects = response.optJSONArray("objects");
            if (objects == null) {
                throw new IllegalStateException("云端对象列表为空。");
            }

            HashMap<String, JSONObject> objectMap = new HashMap<String, JSONObject>();
            for (int i = 0; i < objects.length(); ++i) {
                JSONObject item = objects.optJSONObject(i);
                if (item == null) continue;
                String objectId = item.optString("objectId", "").trim();
                if (!objectId.isEmpty()) {
                    objectMap.put(objectId, item);
                }
            }

            JSONObject layoutObject = objectMap.get("yanm.layout");
            if (layoutObject == null || layoutObject.optBoolean("deleted", false)) {
                throw new IllegalStateException("云端没有可用的燕幕布局对象。");
            }
            JSONObject layoutPayload = layoutObject.optJSONObject("payload");
            JSONObject settings = layoutPayload == null ? null : layoutPayload.optJSONObject("settings");
            if (settings == null) {
                throw new IllegalStateException("燕幕布局对象缺少 settings。");
            }

            JSONObject yanm = new JSONObject(settings.toString());
            JSONObject componentState = new JSONObject();
            JSONObject indexObject = objectMap.get("yanm.componentStateIndex");
            if (indexObject != null && !indexObject.optBoolean("deleted", false)) {
                JSONObject indexPayload = indexObject.optJSONObject("payload");
                JSONArray stateObjectIds = indexPayload == null ? null : indexPayload.optJSONArray("stateObjectIds");
                if (stateObjectIds != null) {
                    for (int i = 0; i < stateObjectIds.length(); ++i) {
                        String stateObjectId = stateObjectIds.optString(i, "").trim();
                        if (stateObjectId.isEmpty()) continue;
                        JSONObject stateObject = objectMap.get(stateObjectId);
                        if (stateObject == null || stateObject.optBoolean("deleted", false)) continue;
                        JSONObject statePayload = stateObject.optJSONObject("payload");
                        if (statePayload == null) continue;
                        String stateKey = statePayload.optString("stateKey", "").trim();
                        if (stateKey.isEmpty()) continue;
                        componentState.put(stateKey, statePayload.optString("value", ""));
                    }
                }
            }
            yanm.put("componentState", componentState);
            return yanm;
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            try {
                JSONObject yanm = MobileYanmController.fetchYanmStateFromObjects(baseUrl, token);
                if (MobileApplicationContext.get() != null) {
                    MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕读取使用统一对象同步协议。");
                }
                return yanm;
            }
            catch (Exception objectSyncError) {
                Log.w("ApiClient", "Object-based Yanm read failed, falling back to legacy snapshot: " + objectSyncError.getMessage());
                if (MobileApplicationContext.get() != null) {
                    MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕对象读取回退旧快照：" + objectSyncError.getMessage());
                }
            }

            JSONObject payload = MobileApiClient.getJson(baseUrl, "/v1/me/yanm-state", token, "\u8bfb\u53d6\u71d5\u5e55");
            JSONObject yanm = payload.optJSONObject("yanm");
            if (yanm == null) {
                throw new IllegalStateException("\u8d26\u53f7\u4e91\u7aef\u6ca1\u6709\u71d5\u5e55\u6570\u636e\u3002");
            }
            String viewUrl = payload.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && MobileApplicationContext.get() != null) {
                MobileApplicationContext.get().getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
            }
            return yanm;
        }

        static JSONObject fetchSettings(String baseUrl, String token) throws Exception {
            JSONObject payload = MobileApiClient.getJson(baseUrl, "/v1/settings", token, "\u8bfb\u53d6\u914d\u7f6e");
            JSONObject settings = payload.optJSONObject("settings");
            if (settings == null) {
                throw new IllegalStateException("\u672a\u80fd\u83b7\u53d6\u5230\u4e91\u7aef\u914d\u7f6e\u3002");
            }
            return settings;
        }

        static boolean putYanmStateToObjects(String baseUrl, String token, JSONObject yanm) throws Exception {
            if (!MobileApiClient.objectSyncAvailable(baseUrl, token)) {
                return false;
            }

            HashMap<String, JSONObject> objectMap = MobileApiClient.fetchSyncObjectMap(baseUrl, token);
            JSONObject layoutSettings = new JSONObject(yanm.toString());
            JSONObject componentState = MobileJson.firstObject(layoutSettings, "componentState", "ComponentState");
            if (componentState == null) {
                componentState = new JSONObject();
            }
            layoutSettings.put("componentState", new JSONObject());

            MobileApiClient.putSyncObject(
                    baseUrl,
                    token,
                    "yanm.layout",
                    MobileApiClient.syncObjectRevision(objectMap, "yanm.layout"),
                    false,
                    new JSONObject().put("settings", layoutSettings),
                    "同步燕幕布局对象");

            ArrayList<String> currentStateObjectIds = new ArrayList<String>();
            Iterator<String> stateKeys = componentState.keys();
            while (stateKeys.hasNext()) {
                String stateKey = stateKeys.next();
                if (stateKey == null || stateKey.trim().isEmpty()) continue;
                String objectId = MobileYanmController.buildYanmStateObjectId(stateKey);
                currentStateObjectIds.add(objectId);
                MobileApiClient.putSyncObject(
                        baseUrl,
                        token,
                        objectId,
                        MobileApiClient.syncObjectRevision(objectMap, objectId),
                        false,
                        new JSONObject()
                                .put("stateKey", stateKey.trim())
                                .put("value", componentState.optString(stateKey, "")),
                        "同步燕幕组件状态对象");
            }
            java.util.Collections.sort(currentStateObjectIds);

            JSONObject oldIndexObject = objectMap.get("yanm.componentStateIndex");
            JSONObject oldIndexPayload = oldIndexObject == null ? null : oldIndexObject.optJSONObject("payload");
            JSONArray oldIds = oldIndexPayload == null ? null : oldIndexPayload.optJSONArray("stateObjectIds");
            HashSet<String> currentIdSet = new HashSet<String>(currentStateObjectIds);
            if (oldIds != null) {
                for (int i = 0; i < oldIds.length(); ++i) {
                    String oldId = oldIds.optString(i, "").trim();
                    if (oldId.isEmpty() || currentIdSet.contains(oldId)) continue;
                    JSONObject oldStateObject = objectMap.get(oldId);
                    if (oldStateObject == null || oldStateObject.optBoolean("deleted", false)) continue;
                    MobileApiClient.putSyncObject(
                            baseUrl,
                            token,
                            oldId,
                            MobileApiClient.syncObjectRevision(objectMap, oldId),
                            true,
                            new JSONObject(),
                            "删除燕幕组件状态对象");
                }
            }

            JSONArray stateObjectIds = new JSONArray();
            for (String objectId : currentStateObjectIds) {
                stateObjectIds.put(objectId);
            }
            MobileApiClient.putSyncObject(
                    baseUrl,
                    token,
                    "yanm.componentStateIndex",
                    MobileApiClient.syncObjectRevision(objectMap, "yanm.componentStateIndex"),
                    false,
                    new JSONObject().put("stateObjectIds", stateObjectIds),
                    "同步燕幕组件状态索引");
            return true;
        }

        static boolean putYanmComponentStateToObjects(String baseUrl, String token, JSONObject componentState) throws Exception {
            if (!MobileApiClient.objectSyncAvailable(baseUrl, token)) {
                return false;
            }

            HashMap<String, JSONObject> objectMap = MobileApiClient.fetchSyncObjectMap(baseUrl, token);
            JSONObject indexObject = objectMap.get("yanm.componentStateIndex");
            JSONObject indexPayload = indexObject == null ? null : indexObject.optJSONObject("payload");
            JSONArray existingIds = indexPayload == null ? null : indexPayload.optJSONArray("stateObjectIds");
            HashSet<String> stateIds = new HashSet<String>();
            if (existingIds != null) {
                for (int i = 0; i < existingIds.length(); ++i) {
                    String id = existingIds.optString(i, "").trim();
                    if (!id.isEmpty()) stateIds.add(id);
                }
            }

            boolean indexChanged = false;
            Iterator<String> keys = componentState.keys();
            while (keys.hasNext()) {
                String stateKey = keys.next();
                if (stateKey == null || stateKey.trim().isEmpty()) continue;
                String objectId = MobileYanmController.buildYanmStateObjectId(stateKey);
                MobileApiClient.putSyncObject(
                        baseUrl,
                        token,
                        objectId,
                        MobileApiClient.syncObjectRevision(objectMap, objectId),
                        false,
                        new JSONObject()
                                .put("stateKey", stateKey.trim())
                                .put("value", componentState.optString(stateKey, "")),
                        "同步燕幕组件状态对象");
                if (stateIds.add(objectId)) {
                    indexChanged = true;
                }
            }

            if (indexChanged || indexObject == null || indexObject.optBoolean("deleted", false)) {
                ArrayList<String> sortedIds = new ArrayList<String>(stateIds);
                java.util.Collections.sort(sortedIds);
                JSONArray newIndex = new JSONArray();
                for (String id : sortedIds) {
                    newIndex.put(id);
                }
                MobileApiClient.putSyncObject(
                        baseUrl,
                        token,
                        "yanm.componentStateIndex",
                        MobileApiClient.syncObjectRevision(objectMap, "yanm.componentStateIndex"),
                        false,
                        new JSONObject().put("stateObjectIds", newIndex),
                        "同步燕幕组件状态索引");
            }
            return true;
        }

        static JSONObject putYanmState(String baseUrl, String token, JSONObject yanm) throws Exception {
            boolean objectWritten = MobileYanmController.putYanmStateToObjects(baseUrl, token, yanm);
            JSONObject payload = new JSONObject()
                    .put("updatedAtUtc", (Object)new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date()))
                    .put("yanm", (Object)yanm);

            if (objectWritten) {
                if (MobileApplicationContext.get() != null) {
                    MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕写入使用统一对象同步协议。");
                }
                try {
                    MobileApiClient.putJson(baseUrl, "/v1/me/yanm-state", payload, token, "兼容镜像燕幕");
                }
                catch (Exception legacyMirrorError) {
                    Log.w("ApiClient", "Legacy Yanm mirror failed after object write: " + legacyMirrorError.getMessage());
                    if (MobileApplicationContext.get() != null) {
                        MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕对象已写入，旧快照镜像失败：" + legacyMirrorError.getMessage());
                    }
                }
                return new JSONObject().put("ok", true).put("source", "object-sync");
            }

            JSONObject res = MobileApiClient.putJson(baseUrl, "/v1/me/yanm-state", payload, token, "\u540c\u6b65\u71d5\u5e55");
            String viewUrl = res.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && MobileApplicationContext.get() != null) {
                MobileApplicationContext.get().getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
            }
            return res;
        }

        static JSONObject putYanmComponentState(String baseUrl, String token, JSONObject componentState) throws Exception {
            boolean objectWritten = MobileYanmController.putYanmComponentStateToObjects(baseUrl, token, componentState);
            JSONObject payload = new JSONObject()
                    .put("updatedAtUtc", (Object)new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date()))
                    .put("componentState", (Object)componentState);

            if (objectWritten) {
                if (MobileApplicationContext.get() != null) {
                    MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕组件状态写入使用统一对象同步协议。");
                }
                try {
                    MobileApiClient.putJson(baseUrl, "/v1/me/yanm-state/component-state", payload, token, "兼容镜像燕幕组件状态");
                }
                catch (Exception legacyMirrorError) {
                    Log.w("ApiClient", "Legacy Yanm component-state mirror failed after object write: " + legacyMirrorError.getMessage());
                    if (MobileApplicationContext.get() != null) {
                        MobileDiagnostics.append(MobileApplicationContext.get(), "燕幕组件状态对象已写入，旧快照镜像失败：" + legacyMirrorError.getMessage());
                    }
                }
                return new JSONObject().put("ok", true).put("source", "object-sync");
            }

            JSONObject res = MobileApiClient.putJson(baseUrl, "/v1/me/yanm-state/component-state", payload, token, "\u540c\u6b65\u71d5\u5e55\u540e\u7aef\u6570\u636e");
            String viewUrl = res.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && MobileApplicationContext.get() != null) {
                MobileApplicationContext.get().getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
            }
            return res;
        }

}

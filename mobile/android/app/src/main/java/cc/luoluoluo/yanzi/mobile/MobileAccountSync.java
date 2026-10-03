package cc.luoluoluo.yanzi.mobile;

import android.content.*;
import org.json.*;

/** Account-scoped incremental cache; revision and data advance in one durable commit. */
final class MobileAccountSync {
    static android.content.SharedPreferences store(Context context) {
        String base = context.getSharedPreferences("yanzi-mobile",0).getString("baseUrl", "");
        try {
            String account = SecureLanConnection.currentAccount(context);
            byte[] hash = java.security.MessageDigest.getInstance("SHA-256").digest((base+"\n"+account).getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder key = new StringBuilder("deviceSync-"); for (byte value : hash) key.append(String.format(java.util.Locale.ROOT,"%02x",value & 255));
            return context.getSharedPreferences(key.toString(),0);
        } catch (Exception error) { throw new IllegalStateException(error); }
    }
    static boolean supported(String id) { return id.startsWith("yanm.") || id.startsWith("extensionData.v1.") || id.startsWith("mobileExtension.v1.") || id.equals("mobileExtensions.index.v1"); }
    static JSONObject status(Context context) throws Exception {
        android.content.SharedPreferences state = store(context);
        return new JSONObject().put("revision",state.getLong("cursor",0)).put("lastCheckedAt",state.getString("lastCheckedAt",""))
                .put("lastChangedAt",state.getString("lastChangedAt",""))
                .put("cachedObjects",new JSONObject(state.getString("objects","{}")).length())
                .put("error",state.getString("error",""));
    }
    static synchronized void synchronize(Context context, String base, String token, String device) throws Exception {
        android.content.SharedPreferences login = context.getSharedPreferences("yanzi-mobile",0), state = store(context);
        JSONObject objects = new JSONObject(state.getString("objects","{}"));
        long cursor = state.getLong("cursor",0); boolean changed = false, yanm = false, extensions = false;
        try {
            for (int page=0;page<100;page++) {
                JSONObject response = MobileMessageClient.requestWithoutQueue(base,"/v1/sync/objects?since="+cursor+"&limit=100",token,"GET",null);
                if (!base.equals(login.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$", "")) || !token.equals(login.getString("token","")) || !device.equals(login.getString("deviceId",""))) return;
                JSONArray changes = response.getJSONArray("objects");
                for (int i=0;i<changes.length();i++) {
                    JSONObject object = changes.getJSONObject(i); String id = object.getString("objectId");
                    if (!supported(id)) continue;
                    JSONObject old = objects.optJSONObject(id);
                    if (old != null && old.optLong("revision") >= object.getLong("revision")) continue;
                    objects.put(id,object); changed=true;
                    yanm |= id.startsWith("yanm."); extensions |= id.startsWith("mobileExtension") || id.equals("mobileExtensions.index.v1");
                }
                long next = response.getLong("cursorRevision");
                if (response.optBoolean("hasMore") && next <= cursor) throw new java.io.IOException("sync_cursor_not_advancing");
                cursor=next;
                if (!response.optBoolean("hasMore")) break;
                if (page==99) throw new java.io.IOException("sync_page_budget_exceeded");
            }
            if (objects.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8).length > 2000000) throw new java.io.IOException("sync_cache_quota_exceeded");
            JSONObject definitions = extensions ? MobileExtensionDefinitionSyncClient.fetchState(base,token) : null;
            JSONObject layout = null;
            if (yanm) {
                JSONObject layoutObject = objects.optJSONObject("yanm.layout");
                layout = layoutObject == null || layoutObject.optBoolean("deleted") ? new JSONObject() :
                        new JSONObject(layoutObject.getJSONObject("payload").getJSONObject("settings").toString());
                JSONObject componentState = new JSONObject(), index = objects.optJSONObject("yanm.componentStateIndex");
                JSONArray ids = index == null || index.optBoolean("deleted") ? null : index.getJSONObject("payload").optJSONArray("stateObjectIds");
                if (ids != null) for (int i=0;i<ids.length();i++) {
                    JSONObject object = objects.optJSONObject(ids.optString(i));
                    if (object != null && !object.optBoolean("deleted")) {
                        JSONObject value = object.getJSONObject("payload");
                        componentState.put(value.getString("stateKey"),value.optString("value"));
                    }
                }
                layout.put("componentState",componentState);
            }
            if (!base.equals(login.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$", "")) || !token.equals(login.getString("token","")) || !device.equals(login.getString("deviceId",""))) return;
            String now = java.time.Instant.now().toString();
            android.content.SharedPreferences.Editor edit = state.edit().putString("objects",objects.toString()).putLong("cursor",cursor).putString("lastCheckedAt",now).remove("error");
            if (changed) edit.putString("lastChangedAt",now);
            // Commit rendered cache first: a crash before cursor commit merely repeats an idempotent read.
            android.content.SharedPreferences.Editor cache = login.edit();
            if (definitions != null) cache.putString("mobileExtensions",definitions.getJSONArray("extensions").toString());
            if (layout != null) cache.putString("cacheYanmJson",layout.toString());
            if (!cache.commit() || !edit.commit()) throw new java.io.IOException("sync_commit_failed");
            if (changed) MainActivity.onAccountSnapshotChanged();
        } catch (Exception error) {
            state.edit().putString("error",error instanceof MobileMessageClient.HttpFailure ? "HTTP "+((MobileMessageClient.HttpFailure)error).status : error.getClass().getSimpleName()).commit();
            throw error;
        }
    }
}

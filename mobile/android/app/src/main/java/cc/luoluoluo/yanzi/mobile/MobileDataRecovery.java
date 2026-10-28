package cc.luoluoluo.yanzi.mobile;
import org.json.*;
import java.util.*;

final class MobileDataRecovery {
    interface Transport { JSONObject request(String method, String path, JSONObject body) throws Exception; }
    private final Transport transport;
    MobileDataRecovery(Transport transport) { this.transport = transport; }
    static MobileDataRecovery forSession(MobileSessionStore.Snapshot session) {
        return new MobileDataRecovery((method,path,body) -> {
            session.requireCurrent();
            if("POST".equals(method)) {
                try { MobileDeviceSession.requireRegistered(session.baseUrl,session.token,session.deviceId); }
                catch(MobileApiClient.MissingSourceDeviceException removed) { session.requireCurrent();MobileDeviceSession.clearRemovedLogin(MobileApplicationContext.get());throw removed; }
                session.requireCurrent();
            }
            JSONObject result = MobileMessageClient.requestWithoutQueue(session.baseUrl,path,session.token,method,body);
            session.requireCurrent(); return result;
        });
    }
    List<JSONObject> objects() throws Exception {
        List<JSONObject> result = new ArrayList<>(); long cursor = 0; String user = null;
        String path = "/v1/sync/objects";
        do {
            JSONObject response = transport.request("GET",path,null);
            if (!response.optBoolean("ok") || response.optString("userId").isEmpty()) throw new IllegalStateException("同步列表响应无效");
            if (user == null) user = response.getString("userId");
            if (!user.equals(response.getString("userId"))) throw new IllegalStateException("账号已改变");
            JSONArray rows = response.getJSONArray("objects"); for (int i=0;i<rows.length();i++) result.add(rows.getJSONObject(i));
            if (!response.optBoolean("hasMore")) break;
            long next = response.optLong("cursorRevision"); if (next <= cursor) throw new IllegalStateException("同步分页没有前进");
            cursor = next; path = "/v1/sync/changes?since=" + cursor + "&limit=500";
        } while(true);
        result.sort(Comparator.comparing(o -> o.optString("objectId"))); return result;
    }
    JSONObject current(String id) throws Exception {
        return transport.request("GET","/v1/sync/objects/" + MobileApiClient.encodePath(id),null).getJSONObject("object");
    }
    JSONObject history(String id, long before) throws Exception {
        return transport.request("GET","/v1/sync/history?objectId=" + MobileApiClient.encodePath(id) + "&limit=50&before=" + before,null);
    }
    JSONObject restore(String id, long expected, long historical, String deviceId) throws Exception {
        if (expected <= 0 || historical <= 0) throw new IllegalArgumentException("恢复版本无效");
        JSONObject body = new JSONObject().put("expectedRevision",expected).put("restoreRevision",historical)
            .put("updatedByDeviceId",deviceId).put("updatedByDeviceName",MobileDeviceIdentity.buildDeviceDisplayName());
        // A restore is a write: an ambiguous transport failure must not trigger an automatic retry.
        return transport.request("POST","/v1/sync/objects/" + MobileApiClient.encodePath(id) + "/restore",body).getJSONObject("object");
    }
}

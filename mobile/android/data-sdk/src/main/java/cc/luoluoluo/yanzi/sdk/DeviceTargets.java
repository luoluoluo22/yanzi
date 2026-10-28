package cc.luoluoluo.yanzi.sdk;

import org.json.JSONArray;
import org.json.JSONObject;

/** Auto-send only when exactly one distinct, active desktop belongs to the returned account list. */
public final class DeviceTargets {
    private DeviceTargets() {}
    public static JSONObject uniqueOnlineDesktop(JSONArray peers) {
        JSONObject selected = null;
        for (int n = 0; n < peers.length(); n++) {
            JSONObject peer = peers.optJSONObject(n);
            if (peer == null || !"desktop".equals(peer.optString("platform"))
                    || !peer.optBoolean("online") || peer.optString("deviceId").isEmpty()
                    || peer.optJSONObject("capabilities") != null && peer.optJSONObject("capabilities").optBoolean("disabled")) continue;
            if (selected != null && !selected.optString("deviceId").equals(peer.optString("deviceId"))) return null;
            selected = peer;
        }
        return selected;
    }
}

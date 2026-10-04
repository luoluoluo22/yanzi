package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.Intent;
import org.json.JSONArray;

/** A removed device needs an explicit account login, never background re-enrollment. */
final class MobileDeviceSession {
    static void requireRegistered(String base, String token, String source) throws Exception {
        JSONArray items = MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices", token, "GET", null).getJSONArray("items");
        for (int i = 0; i < items.length(); i++) {
            if (source.equals(items.getJSONObject(i).optString("deviceId"))) return;
        }
        throw new MobileApiClient.MissingSourceDeviceException();
    }
    static void clearRemovedLogin(Context context) {
        context.getSharedPreferences("yanzi-mobile", 0).edit().remove("token").remove("password").remove("username").putBoolean("deviceLoginRemoved", true).commit();
        context.stopService(new Intent(context, DeviceHeartbeatService.class));
        LanDiscoveryManager.clearLanBaseUrl(context);
    }
}

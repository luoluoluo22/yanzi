package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.*;

/** Account login is the consent. Keys stay inside native storage and never enter the UI. */
final class AccountLanConnections {
    private static final java.util.concurrent.atomic.AtomicBoolean busy = new java.util.concurrent.atomic.AtomicBoolean();
    static volatile String status = "等待获取同账号设备连接信息";
    static void refresh(Context context, String base, String token, String device) {
        if (!busy.compareAndSet(false, true)) return;
        status = "正在获取同账号设备连接信息";
        new Thread(() -> {
            try {
                JSONObject response = MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices/lan-links", token, "POST", new JSONObject().put("deviceId", device));
                android.content.SharedPreferences login = context.getSharedPreferences("yanzi-mobile", 0);
                if (!token.equals(login.getString("token", "")) || !device.equals(login.getString("deviceId", "")) ||
                    !response.getString("userId").equals(SecureLanConnection.currentAccount(context))) return;
                JSONArray items = response.getJSONArray("items");
                java.util.Set<String> enabled = new java.util.HashSet<>();
                for (int i=0;i<items.length();i++) {
                    JSONObject link = items.getJSONObject(i);
                    if (!link.getString("ownerAccount").equals(response.getString("userId")) || !"account".equals(link.optString("mode"))) throw new java.io.IOException("account_link_mismatch");
                    SecureLanConnection.importPair(context, link);
                    enabled.add(link.getString("desktopDeviceId"));
                }
                android.content.SharedPreferences peers = context.getSharedPreferences("YanziPrefs", 0);
                android.content.SharedPreferences.Editor editor = peers.edit();
                for (java.util.Map.Entry<String,?> saved : peers.getAll().entrySet()) {
                    if (!saved.getKey().startsWith("securePair.")) continue;
                    JSONObject old = new JSONObject(String.valueOf(saved.getValue()));
                    String peer = old.getString("desktopDeviceId");
                    if ("account".equals(old.optString("mode")) && response.getString("userId").equals(old.optString("ownerAccount")) && !enabled.contains(peer)) {
                        editor.remove(saved.getKey());
                        if (peer.equals(peers.getString("lanDeviceId", ""))) {
                            editor.remove("lanDeviceId"); LanDiscoveryManager.clearLanBaseUrl(context);
                        }
                    }
                }
                editor.commit();
                status = items.length() == 0 ? "账号下暂无可连接的其他设备" : "已取得同账号设备连接信息，正在检测直连";
                String lan = LanDiscoveryManager.discoverNow(context);
                if (items.length() > 0) status = lan == null ? "已取得连接信息，局域网直连尚未建立" : "同账号局域网直连已建立";
            } catch (Exception error) {
                if (error instanceof MobileMessageClient.HttpFailure) {
                    int code = ((MobileMessageClient.HttpFailure)error).status;
                    status = code == 404 ? "账号服务尚未提供自动连接接口（HTTP 404）" : "获取设备连接信息失败（HTTP " + code + "）";
                } else status = "获取设备连接信息失败，请检查网络及登录状态";
                android.util.Log.d("YanziAccountLan", "Account LAN refresh deferred: " + error.getClass().getSimpleName());
            }
            finally { busy.set(false); }
        }, "YanziAccountLan").start();
    }
}

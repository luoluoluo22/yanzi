package cc.luoluoluo.yanzi.mobile;
import android.app.Activity;
import android.os.Bundle;
import android.widget.*;
import org.json.JSONObject;
/** Account login grants connectivity; this screen only shows connection status. */
public final class LanPairingActivity extends Activity {
    private TextView status;
    private BusyButton refreshBusy;
    private boolean probing;
    private final android.os.Handler handler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable update = new Runnable() {
        @Override public void run() { showStatus(); handler.postDelayed(this, 1000); }
    };
    private volatile String verifiedLan = "";
    private volatile String probeStatus = "正在检测直连";
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int padding = YanziUiKit.dp(this, 20); root.setPadding(padding,padding,padding,padding);
        root.setBackgroundColor(YanziUiKit.BG);
        root.addView(YanziUiKit.text(this, "电脑连接状态", 21, YanziUiKit.TEXT, true));
        status = YanziUiKit.text(this, "正在检测…", 14, YanziUiKit.SECONDARY, false);
        status.setPadding(0,padding,0,padding); status.setLineSpacing(YanziUiKit.dp(this, 6),1f); root.addView(status);
        TextView help = YanziUiKit.text(this, "同账号设备自动连接，局域网可用时优先直连。\n连接通道表示电脑可达，不代表消息、文件或小程序数据已全部同步。", 13, YanziUiKit.SECONDARY, false); root.addView(help);
        Button refresh = new Button(this); refresh.setText("重新检测"); root.addView(refresh);
        refreshBusy = new BusyButton(refresh);
        refresh.setOnClickListener(v -> {
            android.content.SharedPreferences prefs = getSharedPreferences("yanzi-mobile", 0);
            String token = prefs.getString("token", ""), device = prefs.getString("deviceId", "");
            if (token.isEmpty() || device.isEmpty()) { Toast.makeText(this, "请先登录燕子账号", Toast.LENGTH_SHORT).show(); return; }
            AccountLanConnections.refresh(this, prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd"), token, device);
            probe(true);
        });
        Button back = new Button(this); back.setText("返回"); back.setOnClickListener(v -> finish()); root.addView(back);
        ScrollView scroll = new ScrollView(this); scroll.addView(root); setContentView(scroll); probe(false);
    }
    @Override protected void onResume() { super.onResume(); handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update); super.onPause(); }

    private void probe(boolean discover) {
        if (probing) return;
        probing = true;
        refreshBusy.begin("正在检测…");
        verifiedLan = ""; probeStatus = "正在检测直连";
        new Thread(() -> {
            try {
                String base = discover ? LanDiscoveryManager.discoverNow(this) : LanDiscoveryManager.getLanBaseUrl(this);
                if (base == null) { probeStatus = "尚未建立局域网直连"; return; }
                java.net.HttpURLConnection connection = MobileNetworkRouting.openLanConnection(new java.net.URL(base.replaceAll("/$", "") + "/v1/me/devices/protocol"));
                try {
                    connection.setConnectTimeout(2000); connection.setReadTimeout(2500);
                    if (connection.getResponseCode() == 200) { verifiedLan = base; probeStatus = "加密直连检测通过"; }
                    else probeStatus = "直连检测未通过";
                } finally { connection.disconnect(); }
            } catch (Exception error) { probeStatus = "直连暂不可用"; }
            finally { runOnUiThread(() -> { probing = false; refreshBusy.finish(); if (!isFinishing() && !isDestroyed()) showStatus(); }); }
        }, "YanziConnectionDetails").start();
    }

    private String wifiAddress() {
        android.net.ConnectivityManager manager = (android.net.ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
        if (manager != null) for (android.net.Network network : manager.getAllNetworks()) {
            android.net.NetworkCapabilities caps = manager.getNetworkCapabilities(network);
            if (caps == null || !caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI)
                    || !caps.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) continue;
            android.net.LinkProperties links = manager.getLinkProperties(network);
            if (links != null) for (android.net.LinkAddress address : links.getLinkAddresses())
                if (address.getAddress() instanceof java.net.Inet4Address) return address.toString();
            return "已连接，暂无 IPv4 地址";
        }
        return "未连接 Wi-Fi";
    }
    private void showStatus() {
        try {
            JSONObject sync = MobileAccountSync.status(this);
            int count=0;
            for (java.util.Map.Entry<String,?> entry : getSharedPreferences("YanziPrefs",0).getAll().entrySet())
                if (entry.getKey().startsWith("securePair.")) {
                    JSONObject pair = new JSONObject(String.valueOf(entry.getValue()));
                    try { SecureLanConnection.load(this, pair.getString("desktopDeviceId")); count++; } catch (Exception expired) { }
                }
            boolean cloud = getIntent().getBooleanExtra("connected", false) && "cloud".equals(getIntent().getStringExtra("connectionType"));
            String channel = !verifiedLan.isEmpty() ? "局域网" : cloud ? "云端（进入详情时检测）" : "尚未确认";
            String offline = getIntent().getBooleanExtra("connected", false) ? "" : "\n电脑状态：" + getIntent().getStringExtra("offlineReason");
            status.setText("连接通道：" + channel + offline
                    + "\n\n手机 Wi-Fi：" + wifiAddress()
                    + "\n局域网：" + probeStatus
                    + (verifiedLan.isEmpty() ? "" : "\n直连地址：" + verifiedLan)
                    + "\n已授权直连设备：" + count
                    + "\n自动连接：" + AccountLanConnections.status
                    + "\n账号服务器：" + getSharedPreferences("yanzi-mobile",0).getString("baseUrl", "https://sync.luoluoluo.cc.cd")
                    + "\n\n账号数据：燕幕、小程序定义与共享数据"
                    + "\n最近检查：" + displayTime(sync.optString("lastCheckedAt"))
                    + "\n最近更新：" + displayTime(sync.optString("lastChangedAt"))
                    + "\n同步频率：连接时检查，每 60 秒增量检查"
                    + (sync.optString("error").isEmpty() ? "" : "\n同步暂未完成：" + sync.optString("error")));
        } catch (Exception error) { status.setText("登录同一账号后自动连接。"); }
    }
    private String displayTime(String value) {
        if (value.isEmpty()) return "尚无记录";
        try { return java.time.Instant.parse(value).atZone(java.time.ZoneId.systemDefault()).format(java.time.format.DateTimeFormatter.ofPattern("MM-dd HH:mm:ss")); }
        catch (Exception error) { return "尚无记录"; }
    }
}

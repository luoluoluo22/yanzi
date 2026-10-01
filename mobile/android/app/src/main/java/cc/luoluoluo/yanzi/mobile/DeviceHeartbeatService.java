package cc.luoluoluo.yanzi.mobile;
import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
import android.util.Log;
import org.json.*;
import java.util.concurrent.*;

public class DeviceHeartbeatService extends Service {
    private final ScheduledExecutorService worker = Executors.newSingleThreadScheduledExecutor();
    private final ExecutorService receivers = Executors.newFixedThreadPool(2);
    private final java.util.Set<String> inflight = ConcurrentHashMap.newKeySet();
    private final Object deliveryLock = new Object();
    private final MobileRealtimeClient realtime = new MobileRealtimeClient();
    private SharedPreferences prefs;
    private long heartbeat, retryAt;
    private volatile long pollAt, reconnectAt;
    private volatile boolean connected, connecting;
    private volatile int generation;
    private String session = "";
    private int failures;
    public static void startIfLoggedIn(Context context) {
        if (!context.getSharedPreferences("yanzi-mobile", MODE_PRIVATE).getString("token", "").isEmpty()) {
            try { context.startForegroundService(new Intent(context, DeviceHeartbeatService.class)); }
            catch (IllegalStateException ex) { Log.w("YanziMessageBridge", "Service start deferred until foreground resume"); }
        }
    }
    @Override public void onCreate() {
        super.onCreate();
        prefs = getSharedPreferences("yanzi-mobile", MODE_PRIVATE);
        MainActivity.sContext = getApplicationContext();
        MobileNotificationManager.ensureChannels(this);
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification = new Notification.Builder(this, MobileNotificationManager.CHANNEL_CONNECTION)
                .setSmallIcon(android.R.drawable.ic_popup_sync).setContentTitle("燕子跨端消息")
                .setContentText("保持实时连接，接收电脑消息").setContentIntent(open).setOngoing(true).build();
        if (Build.VERSION.SDK_INT >= 34) startForeground(41001, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_REMOTE_MESSAGING);
        else startForeground(41001, notification);
        worker.scheduleWithFixedDelay(this::tick, 0, 1, TimeUnit.SECONDS);
    }
    private boolean valid(String token, String device) {
        return token.equals(prefs.getString("token", "")) && device.equals(prefs.getString("deviceId", ""));
    }
    private void tick() {
        if (SystemClock.elapsedRealtime() < retryAt) return;
        String token = prefs.getString("token", "");
        String base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd").replaceAll("/+$", "");
        String device = prefs.getString("deviceId", "");
        if (token.isEmpty() || device.isEmpty()) { stopSelf(); return; }
        String current = base + "\n" + token + "\n" + device;
        if (!current.equals(session)) {
            session = current; heartbeat = 0; pollAt = 0; reconnectAt = 0;
            generation++; connected = false; connecting = false; realtime.close();
        }
        try {
            long now = SystemClock.elapsedRealtime();
            if (heartbeat == 0 || now - heartbeat >= 30000) {
                JSONObject presence = DeviceStatusReporter.buildPresencePayload(device)
                        .put("displayName", Build.MANUFACTURER + " " + Build.MODEL)
                        .put("pushToken", prefs.getString("pushToken", ""))
                        .put("capabilities", new JSONObject().put("shareText", true).put("sendToDesktop", true)
                                .put("receiveMobileMessages", true).put("receiveAttachments", true)
                                .put("pushProvider", prefs.getString("pushProvider", ""))
                                .put("realtime", connected).put("notificationsEnabled", MobileEventNotifier.canNotify(this)));
                MobileMessageClient.request(base, "/v1/me/devices", token, "POST", presence);
                heartbeat = SystemClock.elapsedRealtime();
                Log.i("YanziMessageBridge", "Cloud heartbeat accepted");
            }
            if (!connected && !connecting && now >= reconnectAt) {
                connecting = true;
                final int stamp = ++generation;
                realtime.connect(base, token, device, new MobileRealtimeClient.Listener() {
                    public void connected() {
                        if (generation != stamp) return;
                        connected = true; connecting = false; pollAt = 0;
                        Log.i("YanziMessageBridge", "Realtime connected");
                    }
                    public void disconnected() {
                        if (generation != stamp) return;
                        connected = false; connecting = false;
                        reconnectAt = SystemClock.elapsedRealtime() + 5000;
                        Log.i("YanziMessageBridge", "Realtime reconnect scheduled");
                    }
                    public void event(JSONObject event) {
                        if (generation != stamp || !valid(token, device)) return;
                        String type = event.optString("type");
                        if ("message".equals(type)) dispatch(base, token, device, event.optJSONObject("message"));
                        else if ("ready".equals(type) || "messages-ready".equals(type)) pollAt = 0;
                    }
                });
            }
            if (SystemClock.elapsedRealtime() >= pollAt) {
                JSONArray items = MobileMessageClient.request(base, "/v1/me/mobile/messages?deviceId="
                        + java.net.URLEncoder.encode(device, "UTF-8") + "&limit=20", token, "GET", null).optJSONArray("items");
                if (valid(token, device) && items != null)
                    for (int i = 0; i < items.length(); i++) dispatch(base, token, device, items.getJSONObject(i));
                pollAt = SystemClock.elapsedRealtime() + (connected ? 30000 : 5000);
            }
            failures = 0; retryAt = 0;
        } catch (MobileMessageClient.HttpFailure ex) {
            if (ex.status == 401) {
                try {
                    String email = prefs.getString("email", ""), password = prefs.getString("password", "");
                    if (email.isEmpty() || password.isEmpty()) throw ex;
                    JSONObject login = MobileMessageClient.request(base, "/v1/auth/login", null, "POST",
                            new JSONObject().put("email", email).put("password", password));
                    if (valid(token, device)) prefs.edit().putString("token", login.getString("accessToken")).apply();
                } catch (Exception ignored) { backoff(); }
            } else backoff();
            Log.w("YanziMessageBridge", "Cloud request failed: HTTP " + ex.status);
        } catch (Exception ex) {
            connecting = false; reconnectAt = SystemClock.elapsedRealtime() + 5000; backoff();
            Log.w("YanziMessageBridge", "Transport unavailable: " + ex.getClass().getSimpleName());
        }
    }
    private void dispatch(String base, String token, String device, JSONObject message) {
        if (message == null) return;
        String key = base + device + message.optString("messageId");
        if (!inflight.add(key)) return;
        try { receivers.execute(() -> {
            try { deliver(base, token, device, message); }
            catch (Exception ex) { Log.w("YanziMessageBridge", "Delivery deferred: " + ex.getClass().getSimpleName()); pollAt = 0; }
            finally { inflight.remove(key); }
        }); } catch (RejectedExecutionException ex) { inflight.remove(key); }
    }
    private void deliver(String base, String token, String device, JSONObject message) throws Exception {
        if (!valid(token, device)) return;
        String id = message.getString("messageId");
        String receipt = "messageReceipt." + Integer.toHexString((base + device).hashCode()) + "." + id;
        JSONObject ack = new JSONObject().put("deviceId", device);
        String kind = message.optString("kind", "");
        boolean attachment = "file".equals(kind) || "photo".equals(kind);
        String content = message.optString("text", "");
        if (!prefs.contains(receipt) && attachment) {
            if (!MobileEventNotifier.canNotify(this)) return;
            JSONObject payload = message.optJSONObject("payload");
            if (payload == null || !payload.has("attachmentId")) {
                ack.put("success", false).put("result", "Attachment reference missing");
            } else content = MobileAttachmentClient.download(this, base, token, payload.getString("attachmentId")).getAbsolutePath();
        }
        synchronized (deliveryLock) {
            if (!valid(token, device)) return;
            if (!prefs.contains(receipt)) {
                if (!"notify".equals(kind) && !"text".equals(kind) && !attachment) {
                    ack.put("success", false).put("result", "Unsupported mobile message kind: " + kind);
                } else if (!ack.has("success")) {
                    if (!MobileEventNotifier.notifyMessage(this, id, "YanziChat".equals(message.optString("title")) ? "电脑消息" : message.optString("title", "电脑消息"), message.optString("text", ""))) return;
                    SharedPreferences.Editor edit = prefs.edit().putLong(receipt, System.currentTimeMillis());
                    if ("YanziChat".equals(message.optString("title")) || attachment) {
                        JSONArray history;
                        try { history = new JSONArray(prefs.getString("desktop_chat_history", "[]")); } catch (Exception ex) { history = new JSONArray(); }
                        boolean recorded = false;
                        for (int i = 0; i < history.length(); i++)
                            if (history.optJSONObject(i) != null && id.equals(history.optJSONObject(i).optString("messageId"))) recorded = true;
                        if (!recorded) history.put(new JSONObject().put("role", "desktop").put("kind", kind)
                                .put("content", content).put("time", System.currentTimeMillis()).put("messageId", id));
                        JSONArray bounded = new JSONArray();
                        for (int i = Math.max(0, history.length() - 50); i < history.length(); i++) bounded.put(history.get(i));
                        edit.putString("desktop_chat_history", bounded.toString());
                    }
                    java.util.List<java.util.Map.Entry<String, ?>> old = new java.util.ArrayList<>();
                    for (java.util.Map.Entry<String, ?> entry : prefs.getAll().entrySet()) if (entry.getKey().startsWith("messageReceipt.")) old.add(entry);
                    old.sort((a, b) -> Long.compare(a.getValue() instanceof Long ? (Long)a.getValue() : 0, b.getValue() instanceof Long ? (Long)b.getValue() : 0));
                    for (int i = 0; i < old.size() - 2047; i++) edit.remove(old.get(i).getKey());
                    if (!edit.commit()) return;
                    if ("YanziChat".equals(message.optString("title")) || attachment) MainActivity.onReceivedChatMessage(kind, content);
                    Log.i("YanziMessageBridge", attachment ? "Cloud attachment saved and verified" : "Cloud notification displayed");
                }
            }
        }
        MobileMessageClient.request(base, "/v1/me/mobile/messages/" + id + "/ack", token, "POST", ack);
        Log.i("YanziMessageBridge", "Cloud message acknowledged");
    }
    private void backoff() {
        failures = Math.min(failures + 1, 4);
        retryAt = SystemClock.elapsedRealtime() + Math.min(30000L, 1000L * (1L << failures));
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) { pollAt = 0; heartbeat = 0; return START_STICKY; }
    @Override public void onDestroy() {
        generation++; realtime.close(); worker.shutdownNow(); receivers.shutdownNow();
        stopForeground(STOP_FOREGROUND_REMOVE); super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}

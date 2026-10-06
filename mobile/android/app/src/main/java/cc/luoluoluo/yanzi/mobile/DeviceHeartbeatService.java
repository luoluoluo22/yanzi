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
    private final ExecutorService receivers = new ThreadPoolExecutor(2, 2, 30, TimeUnit.SECONDS, new ArrayBlockingQueue<Runnable>(64));
    private final ExecutorService outgoing = Executors.newSingleThreadExecutor();
    private final ExecutorService synchronizer = Executors.newSingleThreadExecutor();
    private final java.util.concurrent.atomic.AtomicBoolean syncBusy = new java.util.concurrent.atomic.AtomicBoolean();
    private volatile long syncAt;
    private long stateAt, stateRevision = -1;
    private final java.util.concurrent.atomic.AtomicBoolean outgoingBusy = new java.util.concurrent.atomic.AtomicBoolean();
    private final java.util.Set<String> inflight = ConcurrentHashMap.newKeySet();
    private final Object deliveryLock = new Object();
    private final MobileRealtimeClient realtime = new MobileRealtimeClient();
    private SharedPreferences prefs;
    private long heartbeat, retryAt, maintenanceAt;
    private volatile long pollAt, reconnectAt;
    private volatile boolean connected, connecting;
    private volatile int generation;
    private String session = "";
    private int failures;
    private int connectionFailures;
    private final java.util.concurrent.atomic.AtomicBoolean wakeQueued=new java.util.concurrent.atomic.AtomicBoolean();
    private static String messageCursorKey(String base, String device) {
        try {
            byte[] digest = java.security.MessageDigest.getInstance("SHA-256")
                    .digest((base + "\n" + device).getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder value = new StringBuilder("messageCursor.");
            for (byte b : digest) value.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
            return value.toString();
        } catch (Exception error) { throw new IllegalStateException(error); }
    }
    private long messageCursor(String base, String device) {
        return Math.max(0, prefs.getLong(messageCursorKey(base, device), 0));
    }
    private void saveMessageCursor(String base, String device, long cursor) {
        if (cursor >= 0) prefs.edit().putLong(messageCursorKey(base, device), cursor).commit();
    }
    private void wakeSoon() {
        if(worker.isShutdown() || !wakeQueued.compareAndSet(false,true))return;
        final MessageWakeLock wake = MessageWakeLock.acquire(this, "ready");
        try {worker.schedule(()->{wakeQueued.set(false);try {tick();} finally {wake.close();}},0,TimeUnit.SECONDS);}
        catch(RejectedExecutionException ignored){wakeQueued.set(false);wake.close();}
    }
    public static void startIfLoggedIn(Context context) {
        if (!context.getSharedPreferences("yanzi-mobile", MODE_PRIVATE).getString("token", "").isEmpty()) {
            try { context.startForegroundService(new Intent(context, DeviceHeartbeatService.class)); }
            catch (IllegalStateException ex) { Log.w("YanziMessageBridge", "Service start deferred until foreground resume"); }
        }
    }
    @Override public void onCreate() {
        super.onCreate();
        prefs = getSharedPreferences("yanzi-mobile", MODE_PRIVATE);
        MobileApplicationContext.initialize(getApplicationContext());
        MobileNotificationManager.ensureChannels(this);
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification = new Notification.Builder(this, MobileNotificationManager.CHANNEL_CONNECTION)
                .setSmallIcon(R.drawable.ic_notification_connection)
                .setContentTitle("燕子实时连接")
                .setContentText("保持与电脑连接，接收跨端消息")
                .setCategory(Notification.CATEGORY_SERVICE)
                .setShowWhen(false)
                .setContentIntent(open)
                .setOngoing(true)
                .build();
        if (Build.VERSION.SDK_INT >= 34) startForeground(41001, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_REMOTE_MESSAGING);
        else startForeground(41001, notification);
        worker.scheduleWithFixedDelay(this::tick, 0, 5, TimeUnit.SECONDS);
    }
    private boolean valid(String token, String device) {
        return token.equals(prefs.getString("token", "")) && device.equals(prefs.getString("deviceId", ""));
    }
    private void tick() {
        runStage("companion_resume", () -> CompanionTransferProvider.resumePending(this));
        runStage("external_access", () -> ExternalAccessManager.pollAsync(this));
        if (SystemClock.elapsedRealtime() < retryAt) return;
        String token = prefs.getString("token", "");
        String base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd").replaceAll("/+$", "");
        String device = prefs.getString("deviceId", "");
        if (token.isEmpty() || device.isEmpty()) { stopSelf(); return; }
        String current = base + "\n" + token + "\n" + device;
        if (!current.equals(session)) {
            session = current; heartbeat = 0; maintenanceAt = 0; pollAt = 0; reconnectAt = 0; syncAt = 0; stateRevision = -1;
            generation++; connected = false; connecting = false; realtime.close();
            runStage("environment_schedule", () -> MobileEnvironment.schedule(this));
        }
        // Run ready-triggered receive before heartbeat and maintenance network work.
        if (SystemClock.elapsedRealtime() >= pollAt)
            runStage("message_poll", () -> pollMessages(base, token, device));
        try {
            long now = SystemClock.elapsedRealtime();
            if (connected && realtime.stale(now)) {
                generation++; realtime.close(); connected = false; connecting = false;
                reconnectAt = 0; pollAt = 0;
                Log.w("YanziMessageBridge", "Realtime stale: no event/pong for 120s; polling and reconnecting");
            } else if (connected) realtime.ping(now);
            runStage("state_snapshot", () -> {
                if (now >= stateAt) {
                    long revision = MobileDeviceCapabilities.snapshot(this).getLong("revision");
                    if (revision != stateRevision) { stateRevision = revision; heartbeat = 0; }
                    stateAt = now + 15000;
                }
            });
            runStage("account_sync", () -> {
                if (now >= syncAt && syncBusy.compareAndSet(false, true)) {
                    syncAt = now + BackgroundCadence.syncInterval(connected);
                    synchronizer.execute(() -> {
                        try { MobileAccountSync.synchronize(this, base, token, device); }
                        catch (Exception error) { Log.w("YanziDeviceSync", "Incremental sync deferred: " + error.getClass().getSimpleName()); }
                        finally { syncBusy.set(false); }
                    });
                }
            });
            runStage("heartbeat", () -> {
                if (heartbeat == 0 || now - heartbeat >= BackgroundCadence.HEARTBEAT_MS) {
                    JSONObject presence = DeviceStatusReporter.buildPresencePayload(device)
                            .put("displayName", Build.MANUFACTURER + " " + Build.MODEL)
                            .put("pushToken", prefs.getString("pushToken", ""))
                            .put("capabilities", new JSONObject().put("stateSnapshot", MobileDeviceCapabilities.snapshot(this))
                                    .put("capabilityCatalog", MobileDeviceCapabilities.catalog()).put("mobileCapabilityProtocol", 1)
                                    .put("syncStatus", MobileAccountSync.status(this)).put("shareText", true).put("sendToDesktop", true)
                                    .put("receiveMobileMessages", true).put("receiveAttachments", true)
                                    .put("receiveAccountChat", true).put("deviceMessageProtocolVersions", new JSONArray().put(1))
                                    .put("autoAccountLan", true).put("lanPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981)
                                    .put("receiveLanAttachments", true).put("maxAttachmentBytes", MobileAttachmentClient.LIMIT)
                                    .put("appVersion", BuildConfig.VERSION_NAME).put("versionCode", BuildConfig.VERSION_CODE)
                                    .put("packageName", BuildConfig.APPLICATION_ID).put("messageProtocol", 2)
                                    .put("pushProvider", prefs.getString("pushProvider", ""))
                                    .put("realtime", connected).put("notificationsEnabled", MobileEventNotifier.canNotify(this)));
                    MobileMessageClient.request(base, "/v1/me/devices", token, "POST", presence);
                    heartbeat = SystemClock.elapsedRealtime();
                    Log.i("YanziMessageBridge", "Cloud heartbeat accepted");
                }
            });
            if (now >= maintenanceAt) {
                maintenanceAt = now + BackgroundCadence.HEARTBEAT_MS;
                runStage("lan_refresh", () -> AccountLanConnections.refresh(this, base, token, device, false));
                runStage("receipt_cleanup", () -> MobileDesktopTransfer.cleanupReceipts(this));
                runStage("outbox_replay", () -> {
                    if (outgoingBusy.compareAndSet(false, true)) outgoing.execute(() -> {
                        try { MobileMessageOutbox.replay(base, token); }
                        catch (Exception error) { Log.w("YanziMessageBridge", "Outbox retry deferred: " + error.getClass().getSimpleName()); }
                        finally { outgoingBusy.set(false); }
                    });
                });
            }
            if (!runStage("realtime_connect", () -> {
                if (!connected && !connecting && now >= reconnectAt) {
                    connecting = true;
                    final int stamp = ++generation;
                    realtime.connect(base, token, device, new MobileRealtimeClient.Listener() {
                        public void connected() {
                            if (generation != stamp) return;
                            connected = true; connecting = false; pollAt = 0;
                            connectionFailures=0;
                            syncAt = 0;
                            wakeSoon();
                            ExtensionStorageSignals.reconnected(DeviceHeartbeatService.this, token);
                            MobileEnvironment.retryPending(DeviceHeartbeatService.this);
                            Log.i("YanziMessageBridge", "Realtime connected");
                        }
                        public void disconnected() {
                            if (generation != stamp) return;
                            connected = false; connecting = false;
                            reconnectAt = SystemClock.elapsedRealtime() + BackgroundCadence.reconnectDelay(++connectionFailures);
                            Log.i("YanziMessageBridge", "Realtime reconnect scheduled");
                        }
                        public void event(JSONObject event) {
                            if (generation != stamp || !valid(token, device)) return;
                            String type = event.optString("type");
                            if ("sync-ready".equals(type)) {syncAt = Math.min(syncAt, SystemClock.elapsedRealtime() + 1000);wakeSoon();}
                            if ("external-access-ready".equals(type)) {ExternalAccessManager.invalidate(DeviceHeartbeatService.this);wakeSoon();}
                            if ("message".equals(type)) dispatch(base, token, device, event.optJSONObject("message"));
                            else if ("ready".equals(type) || "messages-ready".equals(type)) {pollAt = 0;wakeSoon();}
                        }
                    });
                }
            })) { connecting = false; reconnectAt = SystemClock.elapsedRealtime() + 5000; }
            if (SystemClock.elapsedRealtime() >= pollAt) {
                pollMessages(base, token, device);
            }
            failures = 0; retryAt = 0;
        } catch (MobileMessageClient.HttpFailure ex) {
            if (ex.status == 403 || ex.status == 404) {
                try {
                    MobileDeviceSession.requireRegistered(base, token, device);
                } catch (MobileApiClient.MissingSourceDeviceException removed) {
                    if (valid(token, device)) MobileDeviceSession.clearRemovedLogin(this);
                    return;
                } catch (Exception unavailable) { /* Only confirmed removal invalidates login. */ }
            }
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
    interface Stage { void run() throws Exception; }
    private void pollMessages(String base, String token, String device) throws Exception {
        try (MessageWakeLock wake = MessageWakeLock.acquire(this, "poll")) {
            long cursor = messageCursor(base, device);
            JSONObject page = MobileMessageClient.request(base, "/v1/me/mobile/messages?deviceId="
                    + java.net.URLEncoder.encode(device, "UTF-8") + "&limit=20&after=" + cursor, token, "GET", null);
            JSONArray items = page.optJSONArray("items");
            int count = items == null ? 0 : items.length();
            if (valid(token, device) && items != null)
                for (int i = 0; i < items.length(); i++) {
                    JSONObject item = items.optJSONObject(i);
                    if (item != null) { trace(item.optString("messageId"), "polled"); dispatch(base, token, device, item); }
                }
            // Delivery and ACK are asynchronous. Only checkpoint an empty page; non-empty pages
            // are replayed after the bounded retry interval and local receipts keep delivery idempotent.
            if (valid(token, device) && count == 0)
                saveMessageCursor(base, device, page.optLong("nextCursor", cursor));
            pollAt = SystemClock.elapsedRealtime() + (count > 0
                    ? BackgroundCadence.deliveryRetryInterval()
                    : BackgroundCadence.pollInterval(connected));
        }
    }
    static boolean runStage(String stage, Stage action) {
        try { action.run(); return true; }
        catch (Exception error) {
            Log.w("YanziMessageBridge", "stage=" + stage + " failed; continuing", error);
            return false;
        }
    }
    private static void trace(String id, String stage) {
        Log.i("YanziMessageBridge", "messageId=" + id + " stage=" + stage);
    }
    private void acknowledge(String base, String token, String id, JSONObject ack) throws Exception {
        trace(id, "ack_started");
        try {
            MobileMessageClient.requestWithoutQueue(base, "/v1/me/mobile/messages/" + id + "/ack", token, "POST", ack);
            trace(id, "ack_success");
        } catch (Exception error) {
            Log.w("YanziMessageBridge", "messageId=" + id + " stage=ack_failed", error);
            throw error;
        }
    }
    private void dispatch(String base, String token, String device, JSONObject message) {
        if (message == null) return;
        String key = base + device + message.optString("messageId");
        if (!inflight.add(key)) return;
        trace(message.optString("messageId"), "dispatched");
        final MessageWakeLock wake = MessageWakeLock.acquire(this, "delivery");
        try { receivers.execute(() -> {
            try { deliver(base, token, device, message); }
            catch (Exception ex) {
                Log.w("YanziMessageBridge", "messageId=" + message.optString("messageId") + " stage=delivery_failed", ex);
                pollAt = SystemClock.elapsedRealtime() + BackgroundCadence.deliveryRetryInterval();
            }
            finally { inflight.remove(key); wake.close(); }
        }); } catch (RejectedExecutionException ex) { inflight.remove(key); wake.close(); }
    }
    private void deliver(String base, String token, String device, JSONObject message) throws Exception {
        if (!valid(token, device)) return;
        String id = message.getString("messageId");
        String receipt = "messageReceipt." + Integer.toHexString((base + device).hashCode()) + "." + id;
        JSONObject ack = new JSONObject().put("deviceId", device);
        String kind = message.optString("kind", "");
        if (ExtensionStorageSignals.KIND.equals(kind)) {
            JSONObject change=message.optJSONObject("payload");
            if(change==null)throw new IllegalArgumentException("Missing storage invalidation");
            if(!prefs.contains(receipt)) {
                ExtensionStorageSignals.deliver(this,ExtensionStorageProvider.accountId(token),change.optString("extensionId"),change.optString("key"),change.optLong("revision",-1));
                prefs.edit().putLong(receipt,System.currentTimeMillis()).commit();
            }
            acknowledge(base, token, id, ack.put("success", true));
            return;
        }
        if (ExtensionHandoffSignals.KIND.equals(kind)) {
            if (!prefs.contains(receipt)) {
                JSONObject payload=message.optJSONObject("payload");
                try {
                    JSONObject handoff=ExtensionHandoffSignals.deliver(this,SecureLanConnection.currentAccount(this),payload);
                    if(!prefs.edit().putLong(receipt,System.currentTimeMillis()).commit())return;
                    ack.put("success",true).put("result",handoff);
                } catch(Exception error) {
                    ack.put("success",false).put("result",error.getMessage()==null?error.getClass().getSimpleName():error.getMessage());
                }
            } else ack.put("success",true).put("result",new JSONObject().put("opened",true));
            acknowledge(base, token, id, ack);
            return;
        }
        if ("capability.invoke".equals(kind)) {
            handleCapability(base, token, device, message);
            return;
        }
        if ("screenshot".equals(kind)) kind = "photo";
        boolean attachment = "file".equals(kind) || "photo".equals(kind);
        String content = message.optString("text", "");
        JSONObject transfer = message.optJSONObject("payload");
        String transferIdBeforeDownload = transfer == null ? "" : transfer.optString("clientTransferId");
        boolean alreadyOnLan = false;
        if (attachment && transferIdBeforeDownload.matches("[a-f0-9]{32}") && prefs.contains("lanTransfer." + transferIdBeforeDownload)) {
            JSONObject saved = new JSONObject(prefs.getString("lanTransfer." + transferIdBeforeDownload, "{}"));
            alreadyOnLan = saved.optString("sha256").equalsIgnoreCase(transfer.optString("sha256")) && new java.io.File(saved.optString("path")).isFile();
            if (alreadyOnLan) content = saved.optString("path");
        }
        if (!prefs.contains(receipt) && attachment && !alreadyOnLan) {
            trace(id, "download_started");
            JSONObject payload = message.optJSONObject("payload");
            if (payload != null && payload.has("screenshotDataUrl")) {
                content = MobileAttachmentClient.saveLegacyPhoto(this, id, payload.getString("screenshotDataUrl")).getAbsolutePath();
            } else if (payload == null || !payload.has("attachmentId")) {
                ack.put("success", false).put("result", "Attachment reference missing");
            } else {
                try { content = MobileAttachmentClient.download(this, base, token, payload.getString("attachmentId")).getAbsolutePath(); }
                catch (MobileMessageClient.HttpFailure error) {
                    if (error.status != 404 || !"attachment_not_found".equals(error.code)) throw error;
                    // The server has confirmed this attachment is absent or expired.
                    trace(id, "attachment_unavailable");
                    ack.put("success", false).put("result", "attachment_not_found");
                }
            }
        }
        if (!prefs.contains(receipt) && attachment && !ack.has("success")) trace(id, "download_done");
        synchronized (MobileDesktopTransfer.deliveryLock) {
            if (!valid(token, device)) return;
            JSONObject transferPayload = message.optJSONObject("payload");
            String transferId = transferPayload == null ? "" : transferPayload.optString("clientTransferId");
            String transferKey = "lanTransfer." + transferId;
            if (transferId.matches("[a-f0-9]{32}") && prefs.contains(transferKey)) {
                JSONObject saved = new JSONObject(prefs.getString(transferKey, "{}"));
                if (!attachment || saved.optString("sha256").equalsIgnoreCase(transferPayload.optString("sha256"))) {
                    if (saved.optString("state").equals("executing")) ack.put("success", false).put("resultState", "unknown").put("result", "execution_result_unknown");
                    if (!prefs.edit().putLong(receipt, System.currentTimeMillis()).commit()) throw new java.io.IOException("message_receipt_commit_failed");
                }
            }
            if (!prefs.contains(receipt)) {
                if (!"notify".equals(kind) && !"text".equals(kind) && !attachment) {
                    ack.put("success", false).put("result", "Unsupported mobile message kind: " + kind);
                } else if (!ack.has("success")) {
                    boolean accountChat = "YanziChat".equals(message.optString("title"))
                            || message.optJSONObject("payload") != null && message.optJSONObject("payload").optBoolean("accountChat")
                            || attachment;
                    String chatSourceId = message.optString("sourceDeviceId", "");
                    String chatSourceName = ChatHistoryStore.sourceDeviceName(
                            this, message, chatSourceId.startsWith("desktop-") ? "电脑" : chatSourceId);
                    long chatTime = ChatHistoryStore.messageTime(message);
                    String notificationTitle = accountChat ? chatSourceName : message.optString("title", "燕子");

                    SharedPreferences.Editor edit = prefs.edit().putLong(receipt, System.currentTimeMillis());
                    if (attachment && transferId.matches("[a-f0-9]{32}"))
                        edit.putString(transferKey, new JSONObject().put("sha256", transferPayload.optString("sha256")).put("path", content).put("savedAt", System.currentTimeMillis()).toString());
                    if (accountChat) {
                        if (!ChatHistoryStore.append(this, ChatHistoryStore.message(
                                "peer", kind, content, chatTime, id, chatSourceId, chatSourceName))
                                && !ChatHistoryStore.containsMessage(this, id))
                            throw new java.io.IOException("chat_save_failed");
                        trace(id, "chat_saved");
                    }
                    java.util.List<java.util.Map.Entry<String, ?>> old = new java.util.ArrayList<>();
                    for (java.util.Map.Entry<String, ?> entry : prefs.getAll().entrySet()) if (entry.getKey().startsWith("messageReceipt.")) old.add(entry);
                    for (java.util.Map.Entry<String, ?> entry : old)
                        if (entry.getValue() instanceof Long && (Long)entry.getValue() < System.currentTimeMillis() - 30L * 86400000) edit.remove(entry.getKey());
                    if (!edit.commit()) throw new java.io.IOException("message_receipt_commit_failed");
                    trace(id, "receipt_saved");
                    trace(id, "notification_attempted");
                    String preview = attachment ? ("photo".equals(kind) ? "[图片]" : "[文件]") : message.optString("text", "");
                    runStage("notification:" + id, () -> {
                        if (accountChat) MobileEventNotifier.notifyChatMessage(this, id, notificationTitle, preview);
                        else MobileEventNotifier.notifyMessage(this, id, notificationTitle, preview);
                    });
                    if (accountChat) MainActivity.onReceivedChatMessage(
                            kind, content, chatSourceId, chatSourceName, chatTime);
                    Log.i("YanziMessageBridge", "messageId=" + id + " stage=delivery_saved");
                }
            }
        }
        acknowledge(base, token, id, ack);
        Log.i("YanziMessageBridge", "Cloud message acknowledged");
    }
    private void handleCapability(String base, String token, String device, JSONObject message) throws Exception {
        String id = message.getString("messageId");
        android.content.SharedPreferences state = MobileAccountSync.store(this);
        String resultKey = "capResult." + id;
        synchronized (deliveryLock) {
            JSONObject ack;
            if (state.contains(resultKey)) ack = new JSONObject(state.getString(resultKey,"{}"));
            else {
                JSONObject claim = MobileMessageClient.requestWithoutQueue(base,"/v1/me/mobile/messages/"+id+"/claim",token,"POST",new JSONObject().put("deviceId",device));
                if (!claim.optBoolean("acquired")) return;
                ack = new JSONObject().put("deviceId",device).put("resultState","executed");
                try { ack.put("success",true).put("result",MobileDeviceCapabilities.execute(this,message).toString()); }
                catch (Exception error) { ack.put("success",false).put("result",new JSONObject().put("error",error.getMessage()==null?error.getClass().getSimpleName():error.getMessage()).toString()); }
                if (!valid(token,device)) return;
                android.content.SharedPreferences.Editor edit = state.edit();
                java.util.List<String> keys = new java.util.ArrayList<>();
                for (String key : state.getAll().keySet()) if (key.startsWith("capResult.")) keys.add(key);
                if (keys.size() >= 100) for (String key : keys.subList(0,keys.size()-99)) edit.remove(key);
                if (!edit.putString(resultKey,ack.toString()).commit()) throw new java.io.IOException("capability_result_commit_failed");
            }
            if (valid(token,device)) acknowledge(base, token, id, ack);
        }
    }
    private void backoff() {
        failures = Math.min(failures + 1, 4);
        retryAt = SystemClock.elapsedRealtime() + Math.min(30000L, 1000L * (1L << failures));
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        pollAt = 0; heartbeat = 0;
        if (intent != null && intent.getBooleanExtra("messagesReady", false)) retryAt = 0;
        wakeSoon();
        MobilePushSupport.releaseWakeup();
        return START_STICKY;
    }
    @Override public void onDestroy() {
        generation++; realtime.close(); worker.shutdownNow(); receivers.shutdownNow(); outgoing.shutdownNow(); synchronizer.shutdownNow();
        MobilePushSupport.releaseWakeup();
        stopForeground(STOP_FOREGROUND_REMOVE); super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}

package cc.luoluoluo.yanzi.mobile;
import org.json.JSONObject;

final class MobileDeviceMessageSender {
    // LAN fanout is strictly best-effort; it must not sit in front of cloud submission.
    // Bounded executor avoids queuing unlimited stale-device probes during outages.
    private static final java.util.concurrent.ThreadPoolExecutor lanFanout =
            new java.util.concurrent.ThreadPoolExecutor(1, 2, 30,
                    java.util.concurrent.TimeUnit.SECONDS,
                    new java.util.concurrent.ArrayBlockingQueue<Runnable>(16));
    static { lanFanout.allowCoreThreadTimeOut(true); }

    private static void fanoutLanChat(JSONObject envelope, long startedAt) {
        try {
            String lan = LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get());
            if (lan == null) return;
            JSONObject local = new JSONObject(envelope.toString());
            local.put("notificationPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981);
            java.net.HttpURLConnection connection = null;
            try {
                connection = MobileNetworkRouting.openLanConnection(new java.net.URL(lan + "/v1/me/mobile/messages"));
                connection.setRequestMethod("POST");
                connection.setDoOutput(true);
                connection.setConnectTimeout(1500);
                connection.setReadTimeout(2500);
                connection.setRequestProperty("Content-Type", "application/json");
                try (java.io.OutputStream output = connection.getOutputStream()) {
                    output.write(local.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
                }
                if (connection.getResponseCode() == 200) {
                    try (java.io.InputStream input = connection.getInputStream();
                         java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream()) {
                        byte[] buffer = new byte[4096];
                        int count;
                        while ((count = input.read(buffer)) > 0) {
                            bytes.write(buffer, 0, count);
                            if (bytes.size() > 65536) return;
                        }
                        JSONObject result = new JSONObject(bytes.toString("UTF-8"));
                        if (result.optBoolean("success") && !result.optString("messageId").isEmpty()) {
                            LanDiscoveryManager.noteSuccess();
                            android.util.Log.i("YanziMessageTiming", "stage=lan_delivered durationMs="
                                    + (android.os.SystemClock.elapsedRealtime() - startedAt)
                                    + " kind=" + envelope.optString("kind"));
                        }
                    }
                }
            } finally {
                if (connection != null) connection.disconnect();
            }
        } catch (Exception error) {
            android.util.Log.d("YanziMessageTiming", "LAN fanout deferred: " + error.getClass().getSimpleName());
        }
    }

    static JSONObject send(String base, String token, JSONObject original) throws Exception {
        long startedAt = android.os.SystemClock.elapsedRealtime();
        JSONObject envelope = new JSONObject(original.toString());
        if (!envelope.has("clientMessageId")) envelope.put("clientMessageId", java.util.UUID.randomUUID().toString());
        String kind = envelope.optString("kind", "text");
        boolean chat = java.util.Arrays.asList("text", "photo", "file", "screenshot").contains(kind);
        boolean execution = kind.startsWith("run-") || kind.startsWith("fs-") || kind.equals("capability.invoke");
        if (execution && !envelope.has("expiresAt")) envelope.put("expiresAt", java.time.Instant.now().plusSeconds(120).toString());
        if (!execution && !envelope.has("targetDeviceId")) envelope.put("routing", "account-chat");
        JSONObject payload = envelope.optJSONObject("payload");
        if (payload == null) { payload = new JSONObject(); envelope.put("payload", payload); }
        payload.put("clientOperationId", envelope.getString("clientMessageId"));
        if (!payload.has("clientSubmittedAt")) payload.put("clientSubmittedAt", System.currentTimeMillis());
        long started = android.os.SystemClock.elapsedRealtime();
        android.util.Log.i("YanziSend", "send_begin id=" + envelope.getString("clientMessageId"));
        java.io.File saved = MobileMessageOutbox.save(base, token, envelope);
        // Cloud-first, LAN-parallel: locally delivered messages and cloud replay share the
        // same clientOperationId, so the desktop receipt store suppresses duplicate effects.
        if (!execution && "text".equals(kind) &&
                (!envelope.has("targetDeviceId") || "desktop".equals(envelope.optString("targetPlatform")))) {
            final JSONObject localEnvelope = new JSONObject(envelope.toString());
            try { lanFanout.execute(() -> fanoutLanChat(localEnvelope, startedAt)); }
            catch (java.util.concurrent.RejectedExecutionException busy) {
                android.util.Log.d("YanziMessageTiming", "LAN fanout saturated; cloud remains authoritative");
            }
        }
        JSONObject delivered = null;
        // Only execution commands may wait for a LAN result before cloud delivery.
        // Ordinary chat can reach LAN concurrently, but its cloud submission is never blocked.
        String lan = execution ? LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get()) : null;
        if(lan!=null && envelope.has("targetDeviceId") && !envelope.optString("targetDeviceId")
            .equals(LanDiscoveryManager.getLanDeviceId(MobileApplicationContext.get()))) lan=null;
        if (lan != null) {
            java.net.HttpURLConnection connection = null;
            try {
                envelope.put("notificationPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981);
                connection = MobileNetworkRouting.openLanConnection(new java.net.URL(lan + "/v1/me/mobile/messages"));
                connection.setRequestMethod("POST"); connection.setDoOutput(true); connection.setConnectTimeout(1500); connection.setReadTimeout(execution ? 25000 : 8000);
                connection.setRequestProperty("Content-Type", "application/json");
                try (java.io.OutputStream output = connection.getOutputStream()) { output.write(envelope.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8)); }
                if (connection.getResponseCode() == 200) {
                    try (java.io.InputStream input = connection.getInputStream(); java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream()) {
                        byte[] buffer = new byte[4096]; int count; while ((count = input.read(buffer)) > 0) bytes.write(buffer, 0, count);
                        delivered = new JSONObject(bytes.toString("UTF-8"));
                    }
                }
            } catch (Exception ignored) { /* The durable cloud queue remains authoritative. */ }
            finally { if (connection != null) connection.disconnect(); }
            envelope.remove("notificationPort");
            if (delivered != null && !delivered.optString("messageId").isEmpty() && (execution || chat && delivered.optBoolean("success"))) {
                if (execution) MobileMessageOutbox.complete(saved);
                LanDiscoveryManager.noteSuccess();
                delivered.put("_transport", "lan");
                MobileTaskJournal.accepted(base, token, envelope, delivered);
                if (!execution) DeviceHeartbeatService.requestOutboxReplay(MobileApplicationContext.get());
                android.util.Log.i("YanziSend", "lan_delivered id=" + envelope.getString("clientMessageId") + " elapsed_ms=" + (android.os.SystemClock.elapsedRealtime() - started));
                return delivered;
            }
        }
        try {
            android.util.Log.i("YanziSend", "cloud_begin id=" + envelope.getString("clientMessageId"));
            JSONObject response = MobileMessageClient.requestWithoutQueue(base, "/v1/me/mobile/messages", token, "POST", envelope);
            MobileMessageOutbox.complete(saved);
            android.util.Log.i("YanziSend", "cloud_submitted id=" + envelope.getString("clientMessageId") + " elapsed_ms=" + (android.os.SystemClock.elapsedRealtime() - started));
            return response;
        } catch (Exception error) { if (delivered != null && delivered.optBoolean("success") && !delivered.optString("messageId").isEmpty()) return delivered; throw error; }
    }
}

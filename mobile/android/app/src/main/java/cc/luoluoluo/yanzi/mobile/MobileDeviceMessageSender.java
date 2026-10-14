package cc.luoluoluo.yanzi.mobile;
import org.json.JSONObject;

final class MobileDeviceMessageSender {
    static JSONObject send(String base, String token, JSONObject original) throws Exception {
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
        long started = android.os.SystemClock.elapsedRealtime();
        android.util.Log.i("YanziSend", "send_begin id=" + envelope.getString("clientMessageId"));
        java.io.File saved = MobileMessageOutbox.save(base, token, envelope);
        JSONObject delivered = null;
        String lan = LanDiscoveryManager.getLanBaseUrl(MobileApplicationContext.get());
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

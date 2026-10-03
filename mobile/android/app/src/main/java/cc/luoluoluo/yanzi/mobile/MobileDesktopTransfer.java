package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.util.UUID;

/** LAN delivers promptly; the durable outbox also replicates account chat through cloud. */
final class MobileDesktopTransfer {
    static final Object deliveryLock = new Object();
    interface Delivery { void accept(String kind, String path) throws Exception; }
    static int cleanupReceipts(Context context) throws Exception {
        synchronized (deliveryLock) {
            android.content.SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", 0);
            android.content.SharedPreferences.Editor edit = prefs.edit(); int count = 0; long now = System.currentTimeMillis();
            for (java.util.Map.Entry<String, ?> entry : prefs.getAll().entrySet()) if (entry.getKey().startsWith("lanTransfer.")) {
                count++;
                try {
                    JSONObject receipt = new JSONObject(String.valueOf(entry.getValue()));
                    long savedAt = receipt.optLong("savedAt", receipt.optLong("time"));
                    if (savedAt == 0) edit.putString(entry.getKey(), receipt.put("savedAt", now).toString());
                    else if (savedAt < now - 30L * 86400000) { edit.remove(entry.getKey()); count--; }
                } catch (org.json.JSONException invalid) { /* Keep uncertain legacy receipts until explicitly inspected. */ }
            }
            if (!edit.commit()) throw new IOException("inbox_cleanup_failed");
            return count;
        }
    }
    static JSONObject receiveFromDesktop(Context context, InputStream input, String requestPath, long length,
                                          String expectedHash, Delivery deliver) throws Exception {
        android.net.Uri uri = android.net.Uri.parse(requestPath);
        String id = uri.getLastPathSegment(), kind = uri.getQueryParameter("kind"), name = uri.getQueryParameter("name");
        if (id == null || !id.matches("[a-f0-9]{32}") || name == null || name.isEmpty() || name.length() > 200
                || name.matches("(?s).*[\\\\/\\x00-\\x1f].*") || !("photo".equals(kind) || "file".equals(kind))
                || length <= 0 || length > MobileAttachmentClient.LIMIT || !expectedHash.matches("[a-fA-F0-9]{64}"))
            throw new IOException("Invalid LAN transfer metadata");
        File folder = new File(context.getFilesDir(), "mobile-attachments"); folder.mkdirs();
        MobileAttachmentClient.preflight(folder, length);
        File temporary = File.createTempFile("lan-", ".part", folder);
        try {
            long received = 0;
            try (OutputStream output = new FileOutputStream(temporary)) {
                byte[] bytes = new byte[65536];
                while (received < length) {
                    int count = input.read(bytes, 0, (int)Math.min(bytes.length, length - received));
                    if (count < 0) throw new EOFException("Incomplete LAN transfer");
                    output.write(bytes, 0, count); received += count;
                }
            }
            if (!MobileAttachmentClient.hash(temporary).equalsIgnoreCase(expectedHash)) throw new IOException("LAN checksum mismatch");
            synchronized (deliveryLock) {
                int receiptCount = cleanupReceipts(context);
                android.content.SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
                String key = "lanTransfer." + id;
                if (prefs.contains(key)) {
                    JSONObject saved = new JSONObject(prefs.getString(key, "{}"));
                    if (!expectedHash.equalsIgnoreCase(saved.optString("sha256"))) throw new IOException("LAN transfer ID conflict");
                    return new JSONObject().put("success", !saved.optString("state").equals("executing")).put("deduplicated", true)
                        .put("messageId", id).put("output", saved.optString("state").equals("executing") ? "execution_result_unknown" : "");
                }
                if (receiptCount >= 10000) throw new IOException("inbox_quota_exceeded");
                File target = new File(folder, "lan-" + id + "-" + name);
                if (!temporary.renameTo(target)) throw new IOException("LAN file commit failed");
                JSONObject saved = new JSONObject().put("sha256", expectedHash).put("path", target.getAbsolutePath()).put("state", "executing").put("savedAt", System.currentTimeMillis());
                if (!prefs.edit().putString(key, saved.toString()).commit()) throw new IOException("inbox_commit_failed");
                deliver.accept(kind, target.getAbsolutePath());
                if (!prefs.edit().putString(key, saved.put("state", "completed").toString()).commit()) throw new IOException("inbox_result_commit_failed");
                MobileDiagnostics.append(context, "电脑文件局域网接收完成: kind=" + kind + ", bytes=" + length + ", id=" + id);
                return new JSONObject().put("success", true).put("messageId", id);
            }
        } finally { temporary.delete(); }
    }
    static String sendBytes(Context context, String cloudBase, String cloudToken, String source,
                            String kind, String name, String mime, byte[] bytes, String text) throws Exception {
        if (bytes.length == 0 || bytes.length > MobileAttachmentClient.LIMIT) throw new IOException("文件大小需为 1 字节至 30 MB");
        File file = File.createTempFile("desktop-transfer-", ".tmp", context.getCacheDir());
        try {
            try (OutputStream output = new FileOutputStream(file)) { output.write(bytes); }
            return sendFile(context, cloudBase, cloudToken, source, kind, name, mime, file, text);
        } finally { file.delete(); }
    }

    static String sendFile(Context context, String cloudBase, String cloudToken, String source,
                           String kind, String name, String mime, File file, String text) throws Exception {
        if (file.length() == 0 || file.length() > MobileAttachmentClient.LIMIT) throw new IOException("文件大小需为 1 字节至 30 MB");
        String id = UUID.randomUUID().toString().replace("-", "");
        String hash = MobileAttachmentClient.hash(file);
        JSONObject envelope = new JSONObject().put("sourceDeviceId", source).put("targetPlatform", "desktop").put("routing", "account-chat")
            .put("kind", kind).put("title", kind.equals("photo") ? "手机照片" : name).put("text", text).put("clientMessageId", id)
            .put("payload", new JSONObject().put("clientTransferId", id).put("sourceDeviceName", MainActivity.buildDeviceDisplayName()));
        File queued = MobileMessageOutbox.saveAttachment(cloudBase, cloudToken, envelope, file, name, mime);
        if (queued != null) { file = new File(queued.getParentFile(), envelope.getJSONObject("localAttachment").getString("file")); hash = MobileAttachmentClient.hash(file); }
        String lanMessageId = null;
        long start = System.currentTimeMillis();
        String lan = LanDiscoveryManager.getLanBaseUrl(context);
        if (lan != null) {
            HttpURLConnection connection = null;
            try {
                if (file.length() >= 2L * 1048576) {
                    lanMessageId = MobileLanChunks.send(lan, id, source, name, kind, file, hash);
                } else {
                String query = "?kind=" + kind + "&name=" + encode(name) + "&sourceDeviceId=" + encode(source)
                        + "&sourceDeviceName=" + encode(MainActivity.buildDeviceDisplayName())
                        + "&notificationPort=" + (BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981);
                connection = MobileNetworkRouting.openLanConnection(new URL(lan + "/v1/lan/transfers/" + id + query));
                connection.setConnectTimeout(1500); connection.setReadTimeout(90000);
                connection.setRequestMethod("POST"); connection.setDoOutput(true);
                connection.setRequestProperty("Authorization", "Bearer " + LanDiscoveryManager.getLanApiToken(context));
                connection.setRequestProperty("Content-Type", mime);
                connection.setRequestProperty("X-Content-Sha256", hash);
                connection.setFixedLengthStreamingMode(file.length());
                try (InputStream input = new FileInputStream(file); OutputStream output = connection.getOutputStream()) {
                    byte[] buffer = new byte[65536]; int count;
                    while ((count = input.read(buffer)) > 0) output.write(buffer, 0, count);
                }
                int status = connection.getResponseCode();
                if (status >= 200 && status < 300) {
                    JSONObject result = readJson(connection);
                    if (!result.optBoolean("success")) throw new TransferRejected(result.optString("output", "电脑处理失败"));
                    MobileDiagnostics.append(context, "局域网文件直传完成: kind=" + kind + ", bytes=" + file.length()
                            + ", durationMs=" + (System.currentTimeMillis() - start) + ", id=" + id);
                    lanMessageId = result.getString("messageId");
                }
                if (status >= 300 && (status == 400 || status == 403 || status == 409 || status == 413)) throw new TransferRejected("局域网文件校验失败，HTTP " + status);
                if (status >= 300) throw new IOException("LAN HTTP " + status);
                }
            } catch (MobileLanChunks.Rejected rejected) { MobileMessageOutbox.fail(queued); throw rejected; }
            catch (TransferRejected rejected) { MobileMessageOutbox.fail(queued); throw rejected; }
            catch (Exception error) {
                android.util.Log.w("YanziSecureLan", "LAN upload failed", error);
                MobileDiagnostics.append(context, "局域网文件直传失败，回退云端: " + error.getClass().getSimpleName()
                        + ", durationMs=" + (System.currentTimeMillis() - start) + ", id=" + id);
                LanDiscoveryManager.noteTransportFailure(error);
            } finally { if (connection != null) connection.disconnect(); }
        }
        if (queued == null && lanMessageId != null) return lanMessageId;
        try {
            MainActivity.YanziApiClient.registerDevice(cloudBase, cloudToken, source, MainActivity.buildDeviceDisplayName());
            if (queued != null) envelope = MobileMessageOutbox.prepare(queued, cloudBase, cloudToken);
            else {
                JSONObject attachment = MobileAttachmentClient.upload(cloudBase, cloudToken, name, mime, file);
                attachment.put("sourceDeviceName", MainActivity.buildDeviceDisplayName()).put("clientTransferId", id);
                envelope.put("payload", attachment);
            }
            JSONObject message = MobileMessageClient.request(cloudBase, "/v1/me/mobile/messages", cloudToken, "POST", envelope);
            MobileMessageOutbox.complete(queued);
            MobileDiagnostics.append(context, "云端文件已入队: kind=" + kind + ", bytes=" + file.length() + ", id=" + id);
            return message.getString("messageId");
        } catch (Exception error) {
            if (lanMessageId != null) { MobileDiagnostics.append(context, "局域网已接收，账号文件同步已保存在发件队列: " + id); return lanMessageId; }
            throw error;
        }
    }
    private static String encode(String value) throws Exception { return URLEncoder.encode(value, "UTF-8"); }
    private static JSONObject readJson(HttpURLConnection connection) throws Exception {
        try (InputStream input = connection.getInputStream(); ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
            byte[] bytes = new byte[4096]; int count;
            while ((count = input.read(bytes)) > 0) {
                buffer.write(bytes, 0, count);
                if (buffer.size() > 2000000) throw new IOException("Response too large");
            }
            return new JSONObject(buffer.toString("UTF-8"));
        }
    }
    private static final class TransferRejected extends IOException { TransferRejected(String message) { super(message); } }
}

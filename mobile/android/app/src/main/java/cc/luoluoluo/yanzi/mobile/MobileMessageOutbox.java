package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Arrays;

/** Durable queue is scoped to the authenticated account and server, never to a bearer token. */
final class MobileMessageOutbox {
    private static final Object gate = new Object();
    private static final java.util.concurrent.atomic.AtomicBoolean replaying = new java.util.concurrent.atomic.AtomicBoolean();
    private static File root(String base, String token) throws Exception {
        Context context = MobileApplicationContext.get();
        if (context == null) throw new IOException("outbox_context_unavailable");
        JSONObject claims = new JSONObject(new String(android.util.Base64.decode(token.split("\\.")[1],
            android.util.Base64.URL_SAFE | android.util.Base64.NO_WRAP | android.util.Base64.NO_PADDING), StandardCharsets.UTF_8));
        String account = claims.getString("sub");
        byte[] hash = MessageDigest.getInstance("SHA-256").digest((base + "\n" + account).getBytes(StandardCharsets.UTF_8));
        StringBuilder name = new StringBuilder(); for (byte b : hash) name.append(String.format("%02x", b));
        File root = new File(context.getFilesDir(), "device-outbox/" + name); root.mkdirs(); clean(root); return root;
    }
    static File save(String base, String token, JSONObject envelope) throws Exception {
        synchronized (gate) {
            String id = envelope.getString("clientMessageId");
            if (!id.matches("[a-zA-Z0-9_-]{8,100}")) throw new IOException("invalid_client_message_id");
            File root = root(base, token), target = new File(root, id + ".json");
            if (!envelope.has("expiresAt")) envelope.put("expiresAt", java.time.Instant.ofEpochMilli(System.currentTimeMillis() + 30L * 86400000).toString());
            String body = envelope.toString();
            if (target.exists()) {
                if (!read(target).toString().equals(body)) throw new IOException("message_id_reused");
                return target;
            }
            File[] existing = root.listFiles((dir, name) -> name.endsWith(".json"));
            if (existing != null && existing.length >= 1000) throw new IOException("outbox_quota_exceeded");
            File temporary = new File(root, id + ".tmp");
            try (FileOutputStream output = new FileOutputStream(temporary)) {
                output.write(body.getBytes(StandardCharsets.UTF_8)); output.getFD().sync();
            }
            if (!temporary.renameTo(target)) throw new IOException("outbox_commit_failed");
            return target;
        }
    }
    static JSONObject read(File path) throws Exception {
        try (InputStream input = new FileInputStream(path); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] bytes = new byte[4096]; int count;
            while ((count = input.read(bytes)) > 0) { output.write(bytes, 0, count); if (output.size() > 2000000) throw new IOException("outbox_record_too_large"); }
            return new JSONObject(output.toString("UTF-8"));
        }
    }
    static File saveAttachment(String base, String token, JSONObject envelope, File source, String name, String mime) throws Exception {
        if (BuildConfig.DEBUG && token.startsWith("unused-lan-verification")) return null;
        if (BuildConfig.DEBUG && token.equals("disposable-cloud-transfer-test")) return null;
        synchronized (gate) {
            File folder = root(base, token);
            File blob = new File(folder, envelope.getString("clientMessageId") + ".bin");
            long occupied = 0; File[] files = folder.listFiles(); if (files != null) for (File file : files) occupied += file.length();
            if (occupied + source.length() > 300L * 1024 * 1024 || folder.getUsableSpace() < source.length() + 10L * 1024 * 1024)
                throw new IOException("outbox_storage_exhausted");
            try (InputStream input = new FileInputStream(source); FileOutputStream output = new FileOutputStream(blob)) {
                byte[] bytes = new byte[65536]; int count; while ((count = input.read(bytes)) > 0) output.write(bytes, 0, count); output.getFD().sync();
            }
            envelope.put("localAttachment", new JSONObject().put("file", blob.getName()).put("name", name).put("mime", mime));
            try { return save(base, token, envelope); } catch (Exception error) { blob.delete(); throw error; }
        }
    }
    static JSONObject prepare(File record, String base, String token) throws Exception {
        synchronized (gate) {
            JSONObject envelope = read(record);
            JSONObject local = envelope.optJSONObject("localAttachment");
            if (local == null) return envelope;
            File blob = new File(record.getParentFile(), local.getString("file"));
            if (!blob.getCanonicalFile().getParentFile().equals(record.getParentFile().getCanonicalFile())) throw new IOException("invalid_outbox_file");
            JSONObject attachment = MobileAttachmentClient.upload(base, token, local.getString("name"), local.getString("mime"), blob);
            JSONObject payload = envelope.getJSONObject("payload");
            for (java.util.Iterator<String> keys = attachment.keys(); keys.hasNext();) { String key = keys.next(); payload.put(key, attachment.get(key)); }
            envelope.remove("localAttachment");
            replace(record, envelope);
            return envelope;
        }
    }
    private static void replace(File target, JSONObject body) throws Exception {
        File temporary = new File(target.getPath() + ".tmp");
        try (FileOutputStream output = new FileOutputStream(temporary)) { output.write(body.toString().getBytes(StandardCharsets.UTF_8)); output.getFD().sync(); }
        if (!temporary.renameTo(target)) throw new IOException("outbox_commit_failed");
    }
    static void complete(File record) {
        if (record == null) return;
        File blob = new File(record.getParentFile(), record.getName().replace(".json", ".bin")); blob.delete(); record.delete();
    }
    static void fail(File record) {
        if (record == null) return;
        new File(record.getParentFile(), record.getName().replace(".json", ".bin")).delete();
        record.renameTo(new File(record.getPath() + ".failed"));
    }
    private static void clean(File root) {
        long now = System.currentTimeMillis(); File[] files = root.listFiles(); if (files == null) return;
        for (File file : files) {
            String name = file.getName(); long age = now - file.lastModified();
            if ((name.endsWith(".failed") || name.endsWith(".tmp")) && age > 86400000) file.delete();
            if (name.endsWith(".bin") && !new File(root, name.replace(".bin", ".json")).exists() && age > 86400000) file.delete();
            if (name.endsWith(".json") && age > 30L * 86400000) complete(file);
        }
    }
    static void replay(String base, String token) throws Exception {
        if (!replaying.compareAndSet(false, true)) return;
        try {
            File[] pending = root(base, token).listFiles((dir, name) -> name.endsWith(".json"));
            if (pending == null) return;
            Arrays.sort(pending, java.util.Comparator.comparingLong(File::lastModified));
            for (int i = 0; i < Math.min(20, pending.length); i++) {
                JSONObject envelope = read(pending[i]);
                String expiry = envelope.optString("expiresAt");
                if (!expiry.isEmpty() && java.time.Instant.parse(expiry).toEpochMilli() <= System.currentTimeMillis()) {
                    complete(pending[i]); continue;
                }
                try {
                    envelope = prepare(pending[i], base, token);
                    MobileMessageClient.requestWithoutQueue(base, "/v1/me/mobile/messages", token, "POST", envelope);
                    complete(pending[i]);
                } catch (MobileMessageClient.HttpFailure error) {
                    if (error.status == 400 || error.status == 403 || error.status == 404 || error.status == 409 || error.status == 410 || error.status == 413 || error.status == 426) {
                        fail(pending[i]);
                    } else throw error;
                }
            }
        } finally { replaying.set(false); }
    }
}

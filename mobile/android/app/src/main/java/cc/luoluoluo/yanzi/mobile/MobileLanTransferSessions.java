package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Durable, bounded staging; only a verified complete file reaches the existing inbox. */
final class MobileLanTransferSessions {
    static final Object gate = new Object();
    static final class Reply {
        final int status; final JSONObject body;
        Reply(int status, JSONObject body) { this.status = status; this.body = body; }
    }
    static Reply handle(Context context, String method, String path, String source, InputStream input, int length,
                        MobileDesktopTransfer.Delivery deliver) throws Exception {
        synchronized (gate) {
            String[] parts = path.substring("/v1/lan/transfer-sessions/".length()).split("/");
            if (!parts[0].matches("[a-f0-9]{32}")) return error(400, "invalid_transfer_id");
            File root = new File(context.getFilesDir(), "mobile-attachments/sessions"); root.mkdirs();
            clean(root);
            File directory = new File(root, parts[0]), record = new File(directory, "manifest.json");
            JSONObject manifest = record.exists() ? read(record) : null;
            if (manifest != null && !source.equals(manifest.optString("sourceDeviceId"))) return error(403, "transfer_owner_mismatch");
            if (parts.length == 1 && method.equals("POST")) {
                if (length < 1 || length > 32000) return error(413, "manifest_too_large");
                JSONObject proposed = new JSONObject(new String(bytes(input, length), StandardCharsets.UTF_8));
                long size = proposed.optLong("size"); String name = proposed.optString("name"), kind = proposed.optString("kind");
                JSONArray hashes = proposed.optJSONArray("blockHashes");
                if (!parts[0].equals(proposed.optString("id")) || !source.equals(proposed.optString("sourceDeviceId")) ||
                    size < 1 || size > MobileAttachmentClient.LIMIT || name.isEmpty() || name.length() > 200 ||
                    name.matches("(?s).*[\\\\/\\x00-\\x1f].*") || !(kind.equals("file") || kind.equals("photo")) ||
                    !proposed.optString("sha256").matches("[a-f0-9]{64}") || hashes == null || hashes.length() != (size + MobileLanChunks.BLOCK - 1) / MobileLanChunks.BLOCK ||
                    !proposed.optString("state", "active").equals("active") || proposed.has("result")) return error(400, "invalid_transfer_manifest");
                for (int i = 0; i < hashes.length(); i++) if (!hashes.optString(i).matches("[a-f0-9]{64}")) return error(400, "invalid_block_hash");
                if (manifest != null && (!manifest.getString("name").equals(name) || !manifest.getString("kind").equals(kind) ||
                    manifest.getLong("size") != size || !manifest.getString("sha256").equals(proposed.getString("sha256")) ||
                    !manifest.getJSONArray("blockHashes").toString().equals(hashes.toString()))) return error(409, "transfer_id_reused");
                if (manifest == null) {
                    if (activeCount(root) >= 128 || used(root) + size > 300L * 1048576) return error(507, "transfer_quota_exceeded");
                    if (root.getUsableSpace() < size * 2 + 10L * 1048576) return error(507, "storage_exhausted");
                    directory.mkdirs(); manifest = proposed.put("state", "active"); save(record, manifest);
                }
            }
            if (manifest == null) return error(404, "transfer_not_found");
            String state = manifest.optString("state");
            if (state.equals("cancelled")) return error(409, "transfer_cancelled");
            if (parts.length == 1 && method.equals("DELETE")) {
                if (state.equals("completed")) return error(409, "transfer_already_completed");
                save(record, manifest.put("state", "cancelled")); clearBlocks(directory);
                return new Reply(200, new JSONObject().put("cancelled", true));
            }
            JSONArray missing = new JSONArray(), hashes = manifest.getJSONArray("blockHashes");
            if (!state.equals("completed")) for (int i = 0; i < hashes.length(); i++) {
                File block = new File(directory, i + ".block");
                if (!block.exists() || !MobileAttachmentClient.hash(block).equals(hashes.getString(i))) { block.delete(); missing.put(i); }
            }
            if (parts.length == 1 && (method.equals("GET") || method.equals("POST")))
                return new Reply(200, new JSONObject().put("id", parts[0]).put("state", state).put("missingBlocks", missing).put("result", manifest.optString("result", "")));
            if (parts.length == 3 && parts[1].equals("blocks") && method.equals("PUT")) {
                if (!state.equals("active")) return error(409, "transfer_already_completed");
                int index; try { index = Integer.parseInt(parts[2]); } catch (NumberFormatException e) { return error(400, "invalid_block_index"); }
                if (index < 0 || index >= hashes.length()) return error(400, "invalid_block_index");
                long expected = Math.min(MobileLanChunks.BLOCK, manifest.getLong("size") - (long)index * MobileLanChunks.BLOCK);
                if (length != expected) return error(400, "invalid_block_size");
                byte[] data = bytes(input, length);
                if (!MobileLanChunks.hex(java.security.MessageDigest.getInstance("SHA-256").digest(data)).equals(hashes.getString(index))) return error(400, "block_checksum_mismatch");
                write(new File(directory, index + ".block"), data);
                record.setLastModified(System.currentTimeMillis());
                return new Reply(200, new JSONObject().put("saved", true).put("index", index));
            }
            if (parts.length == 2 && parts[1].equals("commit") && method.equals("POST")) {
                if (state.equals("completed")) return new Reply(200, new JSONObject(manifest.getString("result")));
                if (missing.length() > 0) return error(409, "blocks_missing");
                File merged = new File(directory, "merged.part");
                try {
                    try (OutputStream output = new FileOutputStream(merged)) {
                        byte[] buffer = new byte[65536]; int count;
                        for (int i = 0; i < hashes.length(); i++) try (InputStream block = new FileInputStream(new File(directory, i + ".block"))) {
                            while ((count = block.read(buffer)) > 0) output.write(buffer, 0, count);
                        }
                    }
                    if (merged.length() != manifest.getLong("size") || !MobileAttachmentClient.hash(merged).equals(manifest.getString("sha256"))) return error(400, "transfer_checksum_mismatch");
                    JSONObject result;
                    String route = "/v1/lan/transfers/" + parts[0] + "?kind=" + manifest.getString("kind") + "&name=" + java.net.URLEncoder.encode(manifest.getString("name"), "UTF-8");
                    try (InputStream file = new FileInputStream(merged)) { result = MobileDesktopTransfer.receiveFromDesktop(context, file, route, merged.length(), manifest.getString("sha256"), deliver); }
                    result.put("messageId", parts[0]); save(record, manifest.put("state", "completed").put("result", result.toString())); clearBlocks(directory);
                    return new Reply(200, result);
                } finally { merged.delete(); }
            }
            return error(405, "transfer_method_not_allowed");
        }
    }
    static byte[] bytes(InputStream input, int length) throws IOException {
        byte[] data = new byte[length]; int read = 0;
        while (read < length) { int count = input.read(data, read, length - read); if (count < 0) throw new EOFException(); read += count; } return data;
    }
    static Reply error(int code, String message) throws JSONException { return new Reply(code, new JSONObject().put("error", message)); }
    static JSONObject read(File file) throws Exception { try (InputStream input = new FileInputStream(file)) { return new JSONObject(new String(bytes(input, (int)file.length()), StandardCharsets.UTF_8)); } }
    static void save(File file, JSONObject data) throws IOException { write(file, data.toString().getBytes(StandardCharsets.UTF_8)); }
    static void write(File file, byte[] data) throws IOException {
        File temporary = new File(file.getParentFile(), file.getName() + ".tmp");
        try (FileOutputStream output = new FileOutputStream(temporary)) { output.write(data); output.getFD().sync(); }
        if (!temporary.renameTo(file)) { temporary.delete(); throw new IOException("transfer_commit_failed"); }
    }
    static long used(File root) { long total = 0; File[] files = root.listFiles(); if (files != null) for (File file : files) total += file.isDirectory() ? used(file) : file.length(); return total; }
    static int activeCount(File root) throws Exception { int count = 0; File[] files = root.listFiles(); if (files != null) for (File directory : files) { File record = new File(directory, "manifest.json"); if (record.exists() && read(record).optString("state").equals("active")) count++; } return count; }
    static void clearBlocks(File directory) { File[] files = directory.listFiles(); if (files != null) for (File file : files) if (!file.getName().equals("manifest.json")) file.delete(); }
    static void clean(File root) throws Exception {
        File[] directories = root.listFiles(); if (directories == null) return;
        long now = System.currentTimeMillis();
        for (File directory : directories) {
            File record = new File(directory, "manifest.json"); if (!record.exists()) { clearBlocks(directory); directory.delete(); continue; }
            long age = now - record.lastModified();
            if (age > 30L * 86400000) { clearBlocks(directory); record.delete(); directory.delete(); }
            else if (age > 86400000 && read(record).optString("state").equals("active")) { save(record, read(record).put("state", "cancelled")); clearBlocks(directory); }
        }
    }
}

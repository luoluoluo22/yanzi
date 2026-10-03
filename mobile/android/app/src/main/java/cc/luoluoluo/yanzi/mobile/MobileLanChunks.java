package cc.luoluoluo.yanzi.mobile;
import java.io.*;
import java.net.*;
import org.json.*;

final class MobileLanChunks {
    static final int BLOCK = 1048576;
    static String send(String base, String id, String source, String name, String kind, File file, String hash) throws Exception {
        for (int attempt = 0; ; attempt++) {
            try { return sendOnce(base, id, source, name, kind, file, hash); }
            catch (Rejected rejected) { throw rejected; }
            catch (IOException error) {
                if (attempt >= 2 || Thread.currentThread().isInterrupted()) throw error;
                Thread.sleep(250L * (attempt + 1));
            }
        }
    }
    private static String sendOnce(String base, String id, String source, String name, String kind, File file, String hash) throws Exception {
        JSONArray hashes = new JSONArray();
        try (InputStream input = new FileInputStream(file)) {
            byte[] bytes = new byte[BLOCK]; int count;
            while ((count = readBlock(input, bytes)) > 0) hashes.put(hex(java.security.MessageDigest.getInstance("SHA-256").digest(java.util.Arrays.copyOf(bytes, count))));
        }
        String path = (kind.equals("companion")?"/v1/companion/transfer-sessions/":"/v1/lan/transfer-sessions/") + id;
        JSONObject manifest = new JSONObject().put("id", id).put("sourceDeviceId", source).put("name", name).put("kind", kind.equals("companion")?"file":kind)
            .put("size", file.length()).put("sha256", hash).put("blockHashes", hashes)
            .put("notificationPort", BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981);
        JSONObject state = request(base, path, "POST", manifest.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8), "application/json");
        if (state.optString("state").equals("completed")) {
            JSONObject completed = new JSONObject(state.getString("result"));
            if (!completed.optBoolean("success",completed.optBoolean("ok"))) throw new Rejected("transfer_processing_failed");
            return completed.optString("messageId",id);
        }
        JSONArray missing = state.getJSONArray("missingBlocks");
        long uploaded = 0;
        try (RandomAccessFile input = new RandomAccessFile(file, "r")) {
            for (int i = 0; i < missing.length(); i++) {
                int index = missing.getInt(i), size = (int)Math.min(BLOCK, file.length() - (long)index * BLOCK);
                byte[] bytes = new byte[size]; input.seek((long)index * BLOCK); input.readFully(bytes);
                request(base, path + "/blocks/" + index, "PUT", bytes, "application/octet-stream"); uploaded += size;
            }
        }
        JSONObject result = request(base, path + "/commit", "POST", new byte[]{123,125}, "application/json");
        if (!result.optBoolean("success",result.optBoolean("ok"))) throw new Rejected("transfer_processing_failed");
        MobileDiagnostics.append(MainActivity.sContext, "局域网分块传输确认: id=" + id + ", uploadedBytes=" + uploaded + ", blocks=" + missing.length());
        return result.optString("messageId",id);
    }
    static JSONObject request(String base, String path, String method, byte[] bytes, String mime) throws Exception {
        HttpURLConnection connection = MobileNetworkRouting.openLanConnection(new URL(base + path));
        try {
            connection.setRequestMethod(method); connection.setConnectTimeout(1500); connection.setReadTimeout(90000); connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", mime); connection.setFixedLengthStreamingMode(bytes.length);
            try (OutputStream output = connection.getOutputStream()) { output.write(bytes); }
            int status = connection.getResponseCode();
            if (status == 400 || status == 403 || status == 409 || status == 413 || status == 507) throw new Rejected("chunk_transfer_rejected:" + status);
            if (status != 200) throw new IOException("chunk_http_" + status);
            try (InputStream input = connection.getInputStream(); ByteArrayOutputStream result = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096]; int count;
                while ((count = input.read(buffer)) > 0) { result.write(buffer, 0, count); if (result.size() > 2000000) throw new IOException("chunk_response_too_large"); }
                return new JSONObject(result.toString("UTF-8"));
            }
        } finally { connection.disconnect(); }
    }
    static int readBlock(InputStream input, byte[] bytes) throws IOException {
        int count = 0, read; while (count < bytes.length && (read = input.read(bytes, count, bytes.length - count)) > 0) count += read; return count;
    }
    static String hex(byte[] bytes) { StringBuilder text = new StringBuilder(); for (byte b : bytes) text.append(String.format("%02x", b)); return text.toString(); }
    static class Rejected extends IOException { Rejected(String message) { super(message); } }
}

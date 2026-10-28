package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import javax.crypto.Cipher;

final class SecureLanReceiver {
    private static final java.util.concurrent.ConcurrentHashMap<String, Long> replay = new java.util.concurrent.ConcurrentHashMap<>();
    static boolean handle(Context context, Socket client, String path, String[] headers, InputStream input, int length, String localToken) throws Exception {
        if (!path.equals("/v1/lan/secure")) return false;
        if (length < 16 || length > 31458000) throw new IOException("invalid_encrypted_frame");
        String metadata = new String(android.util.Base64.decode(header(headers, "x-yanzi-frame"), 0), StandardCharsets.UTF_8);
        JSONObject meta = new JSONObject(metadata);
        JSONObject pair = SecureLanConnection.load(context, meta.getString("source"));
        if (!pair.getString("pairId").equals(header(headers, "x-yanzi-pair")) || !pair.getString("deviceId").equals(meta.getString("target")) ||
            Math.abs(System.currentTimeMillis() - meta.getLong("timestamp")) > 120000) throw new IOException("invalid_frame_identity");
        String route = meta.getString("path"), method = meta.getString("method"), requestId = meta.getString("requestId");
        java.util.UUID.fromString(requestId);
        boolean file = route.matches("/v1/lan/transfers/[a-f0-9]{32}(\\?.*)?") && !route.contains("#");
        boolean chunks = route.matches("/v1/lan/transfer-sessions/[a-f0-9]{32}(/blocks/[0-9]+|/commit)?");
        boolean chat = route.equals("/");
        boolean capability = route.equals("/v1/mobile/capabilities/invoke") && method.equals("POST") &&
                pair.getJSONArray("scopes").toString().contains("\"account.owner\"");
        if (!(chunks && java.util.Arrays.asList("POST", "PUT", "GET", "DELETE").contains(method) && pair.getJSONArray("scopes").toString().contains("\"attachments\"") ||
            capability || method.equals("POST") && (file && pair.getJSONArray("scopes").toString().contains("\"attachments\"") ||
            chat && pair.getJSONArray("scopes").toString().contains("\"chat\"")))) throw new IOException("pair_scope_denied");
        byte[] ciphertext = new byte[length]; int read = 0;
        while (read < length) { int count = input.read(ciphertext, read, length - read); if (count < 0) throw new EOFException(); read += count; }
        byte[] key = SecureLanConnection.key(pair);
        byte[] plaintext = SecureLanConnection.cipher(Cipher.DECRYPT_MODE, key, android.util.Base64.decode(header(headers, "x-yanzi-nonce"), 0), metadata).doFinal(ciphertext);
        long now = System.currentTimeMillis();
        for (java.util.Map.Entry<String, Long> old : replay.entrySet()) if (old.getValue() < now - 120000) replay.remove(old.getKey(), old.getValue());
        synchronized (replay) {
            android.content.SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", 0);
            String replayKey = "lanFrame." + pair.getString("pairId") + "." + requestId;
            if (prefs.contains(replayKey)) throw new IOException("frame_replayed");
            android.content.SharedPreferences.Editor edit = prefs.edit(); int pending = 0;
            for (java.util.Map.Entry<String, ?> entry : prefs.getAll().entrySet()) if (entry.getKey().startsWith("lanFrame.")) {
                if (entry.getValue() instanceof Long && (Long)entry.getValue() < now - 240000) edit.remove(entry.getKey()); else pending++;
            }
            if (pending >= 2000 || !edit.putLong(replayKey, now).commit()) throw new IOException("lan_replay_quota");
        }
        int port = BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981;
        HttpURLConnection forward = (HttpURLConnection)new URL("http://127.0.0.1:" + port + route).openConnection(Proxy.NO_PROXY);
        try {
            forward.setConnectTimeout(1500); forward.setReadTimeout(90000); forward.setRequestMethod(method); forward.setDoOutput(!method.equals("GET"));
            forward.setRequestProperty("Authorization", "Bearer " + localToken);
            forward.setRequestProperty("X-Yanzi-Peer-Device", meta.getString("source"));
            forward.setRequestProperty("Content-Type", meta.getString("contentType"));
            forward.setRequestProperty("X-Content-Sha256", meta.optString("sha256"));
            if (!method.equals("GET")) {
                forward.setFixedLengthStreamingMode(plaintext.length);
                try (OutputStream output = forward.getOutputStream()) { output.write(plaintext); }
            }
            int status = forward.getResponseCode();
            ByteArrayOutputStream body = new ByteArrayOutputStream();
            try (InputStream result = status >= 400 ? forward.getErrorStream() : forward.getInputStream()) {
                if (result != null) { byte[] bytes = new byte[4096]; int count;
                    while ((count = result.read(bytes)) > 0) { body.write(bytes, 0, count); if (body.size() > 2000000) throw new IOException("response_too_large"); }
                }
            }
            byte[] nonce = SecureLanConnection.randomNonce();
            byte[] encrypted = SecureLanConnection.cipher(Cipher.ENCRYPT_MODE, key, nonce, "response\n" + status + "\n" + metadata).doFinal(body.toByteArray());
            OutputStream output = client.getOutputStream();
            output.write(("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nX-Yanzi-Status: " + status +
                "\r\nX-Yanzi-Nonce: " + SecureLanConnection.b64(nonce) + "\r\nContent-Length: " + encrypted.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
            output.write(encrypted); output.flush(); return true;
        } finally { forward.disconnect(); }
    }
    private static String header(String[] headers, String name) throws IOException {
        for (String line : headers) if (line.toLowerCase(java.util.Locale.ROOT).startsWith(name + ":")) return line.substring(name.length() + 1).trim();
        throw new IOException("missing_encrypted_header");
    }
}

package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.security.SecureRandom;
import javax.crypto.*;
import javax.crypto.spec.*;

final class SecureLanConnection extends HttpURLConnection {
    static final String PROTOCOL = "yanzi.lan.aead.v1";
    private HttpURLConnection delegate;
    private final JSONObject pair;
    private File staged;
    private OutputStream stagedOutput;
    private String metadata;
    private byte[] nonce;
    private byte[] responseBody;
    private int status = -1;
    SecureLanConnection(URL url, JSONObject pair) { super(url); this.pair = pair; }
    static JSONObject load(Context context, String desktop) throws Exception {
        if (context == null || desktop == null || desktop.isEmpty()) throw new IOException("pair_required");
        String saved = context.getSharedPreferences("YanziPrefs", 0).getString("securePair." + desktop, "");
        if (saved.isEmpty()) throw new IOException("pair_required");
        JSONObject pair = new JSONObject(saved);
        if (!pair.optString("ownerAccount", "local").equals(currentAccount(context))) throw new IOException("pair_account_mismatch");
        if (java.time.OffsetDateTime.parse(pair.getString("expiresAt")).toInstant().toEpochMilli() <= System.currentTimeMillis()) throw new IOException("pair_expired");
        return pair;
    }
    static String currentAccount(Context context) throws Exception {
        String token = context.getSharedPreferences("yanzi-mobile", 0).getString("token", "");
        if (token.isEmpty()) return "local";
        String[] pieces = token.split("\\.");
        if (pieces.length != 3) throw new IOException("invalid_pair_account_session");
        JSONObject claims = new JSONObject(new String(android.util.Base64.decode(pieces[1], android.util.Base64.URL_SAFE | android.util.Base64.NO_WRAP | android.util.Base64.NO_PADDING), StandardCharsets.UTF_8));
        return claims.getString("sub");
    }
    private static javax.crypto.SecretKey localKey() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null);
        String alias = "yanzi.lan.pair-storage.v1";
        if (!store.containsAlias(alias)) {
            KeyGenerator generator = KeyGenerator.getInstance("AES", "AndroidKeyStore");
            generator.init(new android.security.keystore.KeyGenParameterSpec.Builder(alias,
                android.security.keystore.KeyProperties.PURPOSE_ENCRYPT | android.security.keystore.KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(android.security.keystore.KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(android.security.keystore.KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());
            generator.generateKey();
        }
        return (javax.crypto.SecretKey)store.getKey(alias, null);
    }
    static void importPair(Context context, JSONObject input) throws Exception {
        JSONObject pair = new JSONObject(input.toString());
        pair.put("ownerAccount", currentAccount(context));
        if (!PROTOCOL.equals(pair.getString("protocol"))) throw new IOException("unsupported_secure_protocol");
        String own = context.getSharedPreferences("yanzi-mobile", 0).getString("deviceId", "");
        if (!own.equals(pair.getString("deviceId"))) throw new IOException("pair_device_mismatch");
        byte[] key = android.util.Base64.decode(pair.getString("key"), android.util.Base64.DEFAULT);
        if (key.length != 32 || !pair.getString("pairId").matches("[a-f0-9]{32}")) throw new IOException("invalid_pair_key");
        Cipher protect = Cipher.getInstance("AES/GCM/NoPadding"); protect.init(Cipher.ENCRYPT_MODE, localKey());
        pair.put("protectedKey", b64(protect.doFinal(key))).put("storageNonce", b64(protect.getIV())); pair.remove("key");
        if (!context.getSharedPreferences("YanziPrefs", 0).edit().putString("securePair." + pair.getString("desktopDeviceId"), pair.toString())
            .commit()) throw new IOException("pair_commit_failed");
        android.content.SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", 0);
        if (!"account".equals(pair.optString("mode")) || "desktop".equals(pair.optString("peerPlatform")) && prefs.getString("lanDeviceId", "").isEmpty())
            prefs.edit().putString("lanDeviceId", pair.getString("desktopDeviceId")).commit();
    }
    static byte[] key(JSONObject pair) throws Exception {
        Cipher protect = Cipher.getInstance("AES/GCM/NoPadding");
        protect.init(Cipher.DECRYPT_MODE, localKey(), new GCMParameterSpec(128, android.util.Base64.decode(pair.getString("storageNonce"), 0)));
        return protect.doFinal(android.util.Base64.decode(pair.getString("protectedKey"), 0));
    }
    static String b64(byte[] bytes) { return android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP); }
    static byte[] randomNonce() { byte[] bytes = new byte[12]; new SecureRandom().nextBytes(bytes); return bytes; }
    static Cipher cipher(int mode, byte[] key, byte[] nonce, String aad) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(mode, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, nonce));
        cipher.updateAAD(aad.getBytes(StandardCharsets.UTF_8)); return cipher;
    }
    private void stage() throws Exception {
        if (staged != null) return;
        String contentType = getRequestProperty("Content-Type");
        JSONObject meta = new JSONObject().put("source", pair.getString("deviceId")).put("target", pair.getString("desktopDeviceId"))
            .put("requestId", java.util.UUID.randomUUID().toString()).put("timestamp", System.currentTimeMillis())
            .put("path", url.getFile()).put("method", method).put("contentType", contentType == null ? "application/json" : contentType);
        String hash = getRequestProperty("X-Content-Sha256"); if (hash != null) meta.put("sha256", hash);
        metadata = meta.toString(); nonce = randomNonce();
        staged = File.createTempFile("lan-encrypted-", ".part", MainActivity.sContext.getCacheDir());
        stagedOutput = new CipherOutputStream(new FileOutputStream(staged), cipher(Cipher.ENCRYPT_MODE, key(pair), nonce, metadata));
    }
    @Override public OutputStream getOutputStream() throws IOException {
        try { stage(); return stagedOutput; } catch (Exception error) { throw new IOException("secure_lan_stage_failed", error); }
    }
    private void send() throws IOException {
        if (status >= 0) return;
        try {
            stage(); stagedOutput.close();
            if (staged.length() > 31458000) throw new IOException("encrypted_frame_too_large");
            URL endpoint = new URL(url.getProtocol(), url.getHost(), url.getPort(), "/v1/lan/secure");
            delegate = MobileNetworkRouting.openLanPlainConnection(endpoint);
            delegate.setRequestMethod("POST"); delegate.setDoOutput(true);
            delegate.setConnectTimeout(getConnectTimeout()); delegate.setReadTimeout(getReadTimeout());
            delegate.setRequestProperty("X-Yanzi-Pair", pair.getString("pairId"));
            delegate.setRequestProperty("X-Yanzi-Frame", b64(metadata.getBytes(StandardCharsets.UTF_8)));
            delegate.setRequestProperty("X-Yanzi-Nonce", b64(nonce));
            delegate.setRequestProperty("Content-Type", "application/octet-stream");
            delegate.setFixedLengthStreamingMode(staged.length());
            try (InputStream input = new FileInputStream(staged); OutputStream output = delegate.getOutputStream()) {
                byte[] bytes = new byte[65536]; int count; while ((count = input.read(bytes)) > 0) output.write(bytes, 0, count);
            }
            int outer = delegate.getResponseCode();
            if (outer != 200) { status = outer; responseBody = new byte[0]; return; }
            int inner = Integer.parseInt(delegate.getHeaderField("X-Yanzi-Status"));
            byte[] responseNonce = android.util.Base64.decode(delegate.getHeaderField("X-Yanzi-Nonce"), 0);
            try (InputStream input = delegate.getInputStream(); ByteArrayOutputStream body = new ByteArrayOutputStream()) {
                byte[] bytes = new byte[65536]; int count;
                while ((count = input.read(bytes)) > 0) { body.write(bytes, 0, count); if (body.size() > 16000000) throw new IOException("secure_response_too_large"); }
                responseBody = cipher(Cipher.DECRYPT_MODE, key(pair), responseNonce, "response\n" + inner + "\n" + metadata).doFinal(body.toByteArray());
                status = inner;
            }
        } catch (Exception error) { android.util.Log.w("YanziSecureLan", "Encrypted transport failed: " + error.getClass().getSimpleName() + ": " + error.getMessage()); throw new IOException("secure_lan_failed", error); }
        finally { if (staged != null) staged.delete(); }
    }
    @Override public int getResponseCode() throws IOException { send(); return status; }
    @Override public InputStream getInputStream() throws IOException { send(); if (status >= 400) throw new IOException("HTTP " + status); return new ByteArrayInputStream(responseBody); }
    @Override public InputStream getErrorStream() { try { send(); return new ByteArrayInputStream(responseBody); } catch (IOException ignored) { return null; } }
    @Override public String getHeaderField(String name) { return delegate == null ? null : delegate.getHeaderField(name); }
    @Override public void connect() throws IOException { send(); }
    @Override public boolean usingProxy() { return false; }
    @Override public void disconnect() { if (delegate != null) delegate.disconnect(); if (staged != null) staged.delete(); }
}

package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import java.io.*;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;
import javax.crypto.Cipher;

final class DeviceProtocolVectors {
    static void verify(Context context) throws Exception {
        ByteArrayOutputStream data = new ByteArrayOutputStream();
        try (InputStream input = context.getAssets().open("lan-aead-v1.json")) { byte[] bytes = new byte[4096]; int count; while ((count = input.read(bytes)) > 0) data.write(bytes, 0, count); }
        JSONObject vector = new JSONObject(data.toString("UTF-8"));
        byte[] key = android.util.Base64.decode(vector.getString("key"), 0), nonce = android.util.Base64.decode(vector.getString("nonce"), 0);
        byte[] plaintext = vector.getString("plaintext").getBytes(StandardCharsets.UTF_8);
        byte[] encrypted = SecureLanConnection.cipher(Cipher.ENCRYPT_MODE, key, nonce, vector.getString("aad")).doFinal(plaintext);
        if (!java.util.Arrays.equals(encrypted, android.util.Base64.decode(vector.getString("ciphertext"), 0))) throw new IOException("protocol_vector_encryption_mismatch");
        if (!MobileLanChunks.hex(java.security.MessageDigest.getInstance("SHA-256").digest(plaintext)).equals(vector.getString("sha256"))) throw new IOException("protocol_vector_hash_mismatch");
        try { SecureLanConnection.cipher(Cipher.DECRYPT_MODE, key, nonce, vector.getString("aad") + " ").doFinal(encrypted); throw new IOException("protocol_vector_tamper_accepted"); }
        catch (javax.crypto.AEADBadTagException expected) { }
        MobileDiagnostics.append(context, "设备协议黄金向量 AES-GCM / UTF-8 / SHA-256 / 篡改拒绝=PASSED");
    }
}

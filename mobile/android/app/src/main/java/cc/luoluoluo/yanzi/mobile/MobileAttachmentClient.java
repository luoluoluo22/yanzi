package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;

final class MobileAttachmentClient {
    static final long LIMIT = 30L * 1024 * 1024;
    static String hashString(String value) throws Exception {byte[] hash=MessageDigest.getInstance("SHA-256").digest(value.getBytes("UTF-8"));StringBuilder result=new StringBuilder();for(byte b:hash)result.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return result.toString();}
    static File saveLegacyPhoto(Context context, String messageId, String dataUrl) throws Exception {
        if (dataUrl.length() > LIMIT * 4 / 3 + 256 || !dataUrl.matches("(?s)^data:image/(png|jpeg);base64,.*"))
            throw new IOException("Invalid legacy image");
        byte[] bytes = android.util.Base64.decode(dataUrl.substring(dataUrl.indexOf(',') + 1), android.util.Base64.DEFAULT);
        if (bytes.length == 0 || bytes.length > LIMIT) throw new IOException("Legacy image too large");
        File folder = new File(context.getFilesDir(), "mobile-attachments");
        if (!folder.isDirectory() && !folder.mkdirs()) throw new IOException("Cannot create attachment folder");
        preflight(folder, bytes.length);
        File file = new File(folder, "legacy-" + Integer.toHexString(messageId.hashCode()) + (dataUrl.startsWith("data:image/png") ? ".png" : ".jpg"));
        try (OutputStream output = new FileOutputStream(file)) { output.write(bytes); }
        return file;
    }
    static String hash(File file) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream input = new FileInputStream(file)) {
            byte[] buffer = new byte[65536]; int count;
            while ((count = input.read(buffer)) > 0) digest.update(buffer, 0, count);
        }
        StringBuilder result = new StringBuilder();
        for (byte value : digest.digest()) result.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
        return result.toString();
    }
    static JSONObject uploadBytes(Context context, String base, String token, String name, String mime, byte[] bytes) throws Exception {
        if (bytes.length == 0 || bytes.length > LIMIT) throw new IOException("附件大小需为 1 字节至 30 MB");
        File file = File.createTempFile("attachment-upload-", ".tmp", context.getCacheDir());
        try {
            try (OutputStream output = new FileOutputStream(file)) { output.write(bytes); }
            return upload(base, token, name, mime, file);
        } finally { file.delete(); }
    }
    static JSONObject upload(String base, String token, String name, String mime, File file) throws Exception {
        if (file.length() <= 0 || file.length() > LIMIT) throw new IOException("附件大小超限");
        HttpURLConnection connection = MobileNetworkRouting.openCloudConnection(new URL(base + "/v1/me/mobile/attachments?name=" + URLEncoder.encode(name, "UTF-8")));
        try {
            configure(connection, token); connection.setRequestMethod("POST"); connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", mime);
            connection.setRequestProperty("X-Content-Sha256", hash(file));
            connection.setFixedLengthStreamingMode(file.length());
            try (InputStream input = new FileInputStream(file); OutputStream output = connection.getOutputStream()) {
                byte[] buffer = new byte[65536]; int count;
                while ((count = input.read(buffer)) > 0) output.write(buffer, 0, count);
            }
            int status = connection.getResponseCode();
            if (status < 200 || status >= 300) throw new MobileMessageClient.HttpFailure(status);
            try (InputStream input = connection.getInputStream(); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[4096]; int count;
                while ((count = input.read(buffer)) > 0) { if (output.size() + count > 16384) throw new IOException("附件响应过大"); output.write(buffer, 0, count); }
                return new JSONObject(output.toString("UTF-8"));
            }
        } finally { connection.disconnect(); }
    }
    static void configure(HttpURLConnection connection, String token) {
        connection.setConnectTimeout(10000); connection.setReadTimeout(60000);
        connection.setRequestProperty("Authorization", "Bearer " + token);
        connection.setRequestProperty("User-Agent", "YanziClient-Mobile/" + BuildConfig.VERSION_NAME);
        connection.setRequestProperty("X-Yanzi-Client", "mobile");
    }
    static File download(Context context, String base, String token, String id) throws Exception {
        if (!id.matches("att_[a-f0-9]{32}")) throw new IOException("无效附件编号");
        JSONObject metadata = MobileMessageClient.request(base, "/v1/me/mobile/attachments/" + id, token, "GET", null);
        long size = metadata.getLong("size");
        if (size <= 0 || size > LIMIT) throw new IOException("附件大小超限");
        String name = metadata.getString("fileName").replaceAll("[\\\\/\\x00-\\x1f]", "_");
        File directory = new File(context.getFilesDir(), "mobile-attachments"); directory.mkdirs();
        File target = new File(directory, id + "-" + name);
        String expected = metadata.getString("sha256");
        if (target.exists() && target.length() == size && hash(target).equals(expected)) return target;
        File partial = new File(target.getPath() + ".part");
        long offset = partial.exists() ? partial.length() : 0;
        if (offset >= size) { partial.delete(); offset = 0; }
        preflight(directory, size - offset);
        HttpURLConnection connection = MobileNetworkRouting.openCloudConnection(new URL(base + "/v1/me/mobile/attachments/" + id + "/content"));
        try {
            configure(connection, token);
            if (offset > 0) connection.setRequestProperty("Range", "bytes=" + offset + "-");
            int status = connection.getResponseCode();
            if (status != 200 && status != 206) throw new MobileMessageClient.HttpFailure(status);
            if (status == 200) offset = 0;
            try (InputStream input = connection.getInputStream(); OutputStream output = new FileOutputStream(partial, offset > 0)) {
                byte[] buffer = new byte[65536]; int count; long total = offset;
                while ((count = input.read(buffer)) > 0) {
                    total += count; if (total > size) throw new IOException("附件响应超过声明大小"); output.write(buffer, 0, count);
                }
            }
            if (partial.length() < size) throw new IOException("transfer_incomplete_resume_retained");
            if (partial.length() != size || !hash(partial).equals(expected)) { partial.delete(); throw new IOException("附件校验失败"); }
            if (target.exists()) target.delete();
            if (!partial.renameTo(target)) throw new IOException("附件保存失败");
            return target;
        } finally { connection.disconnect(); }
    }
    static synchronized void preflight(File folder, long additional) throws IOException {
        long occupied = 0, now = System.currentTimeMillis(); File[] files = folder.listFiles();
        if (files != null) for (File file : files) if (file.isFile()) {
            String name = file.getName();
            if ((name.startsWith("att_") || name.startsWith("lan-") || name.startsWith("legacy-")) &&
                file.lastModified() < now - (name.endsWith(".part") ? 86400000L : 30L * 86400000)) file.delete();
            occupied += file.length();
        }
        if (occupied + additional > 300L * 1048576) throw new IOException("transfer_quota_exceeded");
        if (folder.getUsableSpace() < additional + 10L * 1048576) throw new IOException("storage_exhausted");
    }
}

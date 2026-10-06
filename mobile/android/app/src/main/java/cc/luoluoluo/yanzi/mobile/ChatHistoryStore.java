package cc.luoluoluo.yanzi.mobile;

import android.content.ContentValues;
import android.content.Context;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.net.Uri;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.security.MessageDigest;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.UUID;

/**
 * Durable per-account chat history. Legacy SharedPreferences history is migrated once,
 * but new messages live in SQLite so history is no longer truncated to 50 entries.
 */
final class ChatHistoryStore extends SQLiteOpenHelper {
    private static final String DB_NAME = "yanzi-chat-history.db";
    private static final int DB_VERSION = 1;
    private static final String LEGACY_KEY = "desktop_chat_history";
    private static final String MIGRATION_KEY = "chat_history_sqlite_migrated_v1";
    private static final Object LOCK = new Object();
    private static ChatHistoryStore instance;

    private ChatHistoryStore(Context context) {
        super(context.getApplicationContext(), DB_NAME, null, DB_VERSION);
    }

    private static ChatHistoryStore get(Context context) {
        synchronized (LOCK) {
            if (instance == null) instance = new ChatHistoryStore(context);
            return instance;
        }
    }

    @Override public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE chat_messages (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "account_id TEXT NOT NULL," +
                "message_id TEXT," +
                "role TEXT NOT NULL," +
                "kind TEXT NOT NULL," +
                "content TEXT NOT NULL," +
                "time_ms INTEGER NOT NULL," +
                "source_device_id TEXT," +
                "source_device_name TEXT," +
                "UNIQUE(account_id, message_id) ON CONFLICT IGNORE)");
        db.execSQL("CREATE INDEX chat_messages_account_time ON chat_messages(account_id, time_ms, id)");
    }

    @Override public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        // Version 1 is the initial durable store.
    }

    static JSONObject message(String role, String kind, String content, long time,
                              String messageId, String sourceDeviceId, String sourceDeviceName) {
        JSONObject value = new JSONObject();
        try {
            value.put("role", role == null || role.isEmpty() ? "peer" : role);
            value.put("kind", kind == null || kind.isEmpty() ? "text" : kind);
            value.put("content", content == null ? "" : content);
            value.put("time", time > 0 ? time : System.currentTimeMillis());
            if (messageId != null && !messageId.isEmpty()) value.put("messageId", messageId);
            if (sourceDeviceId != null && !sourceDeviceId.isEmpty()) value.put("sourceDeviceId", sourceDeviceId);
            if (sourceDeviceName != null && !sourceDeviceName.isEmpty()) value.put("sourceDeviceName", sourceDeviceName);
        } catch (Exception ignored) {}
        return value;
    }

    static boolean append(Context context, JSONObject value) {
        synchronized (LOCK) {
            try {
                migrateLegacyLocked(context);
                String account = currentAccount(context);
                ContentValues row = toValues(account, value);
                long result = get(context).getWritableDatabase().insertWithOnConflict(
                        "chat_messages", null, row, SQLiteDatabase.CONFLICT_IGNORE);
                return result != -1;
            } catch (Exception error) {
                android.util.Log.e("YanziChatHistory", "append failed", error);
                return false;
            }
        }
    }

    static boolean containsMessage(Context context, String messageId) {
        synchronized (LOCK) {
            try (Cursor cursor = get(context).getReadableDatabase().rawQuery(
                    "SELECT 1 FROM chat_messages WHERE account_id=? AND message_id=? LIMIT 1",
                    new String[]{currentAccount(context), messageId})) {
                return cursor.moveToFirst();
            }
        }
    }

    static JSONArray load(Context context) {
        synchronized (LOCK) {
            JSONArray result = new JSONArray();
            try {
                migrateLegacyLocked(context);
                String account = currentAccount(context);
                try (Cursor cursor = get(context).getReadableDatabase().rawQuery(
                        "SELECT message_id,role,kind,content,time_ms,source_device_id,source_device_name " +
                                "FROM chat_messages WHERE account_id=? ORDER BY time_ms ASC,id ASC",
                        new String[]{account})) {
                    while (cursor.moveToNext()) {
                        JSONObject value = new JSONObject()
                                .put("role", cursor.getString(1))
                                .put("kind", cursor.getString(2))
                                .put("content", cursor.getString(3))
                                .put("time", cursor.getLong(4));
                        String messageId = cursor.getString(0);
                        String sourceId = cursor.getString(5);
                        String sourceName = cursor.getString(6);
                        if (messageId != null && !messageId.isEmpty()) value.put("messageId", messageId);
                        if (sourceId != null && !sourceId.isEmpty()) value.put("sourceDeviceId", sourceId);
                        if (sourceName != null && !sourceName.isEmpty()) value.put("sourceDeviceName", sourceName);
                        result.put(value);
                    }
                }
            } catch (Exception error) {
                android.util.Log.e("YanziChatHistory", "load failed", error);
            }
            return result;
        }
    }

    static void clear(Context context) {
        synchronized (LOCK) {
            try {
                String account = currentAccount(context);
                get(context).getWritableDatabase().delete("chat_messages", "account_id=?", new String[]{account});
                deleteRecursively(mediaDirectory(context, account));
                context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE).edit()
                        .putString(LEGACY_KEY, "[]")
                        .putBoolean(MIGRATION_KEY, true)
                        .apply();
            } catch (Exception error) {
                android.util.Log.e("YanziChatHistory", "clear failed", error);
            }
        }
    }

    static File persistPhoto(Context context, byte[] jpegBytes) throws Exception {
        if (jpegBytes == null || jpegBytes.length == 0) throw new java.io.IOException("EMPTY_PHOTO");
        if (jpegBytes.length > MobileAttachmentClient.LIMIT) throw new java.io.IOException("PHOTO_TOO_LARGE");
        String account = currentAccount(context);
        File folder = mediaDirectory(context, account);
        if (!folder.exists() && !folder.mkdirs()) throw new java.io.IOException("CHAT_MEDIA_DIR_FAILED");
        File target = new File(folder, "photo-" + System.currentTimeMillis() + "-" +
                UUID.randomUUID().toString().replace("-", "") + ".jpg");
        File temp = new File(target.getAbsolutePath() + ".part");
        try (FileOutputStream output = new FileOutputStream(temp)) {
            output.write(jpegBytes);
            output.getFD().sync();
        }
        if (!temp.renameTo(target)) {
            temp.delete();
            throw new java.io.IOException("CHAT_MEDIA_COMMIT_FAILED");
        }
        return target;
    }

    static long messageTime(JSONObject message) {
        if (message == null) return System.currentTimeMillis();
        JSONObject payload = message.optJSONObject("payload");
        long payloadTime = payload == null ? 0 : payload.optLong("createdAt", 0);
        if (payloadTime > 0) return payloadTime;
        long direct = message.optLong("createdAt", 0);
        if (direct > 0) return direct;
        String iso = message.optString("createdAt", "");
        if (!iso.isEmpty()) {
            try { return Instant.parse(iso).toEpochMilli(); } catch (Exception ignored) {}
            try { return OffsetDateTime.parse(iso).toInstant().toEpochMilli(); } catch (Exception ignored) {}
        }
        return System.currentTimeMillis();
    }

    static String sourceDeviceName(JSONObject message, String fallback) {
        if (message == null) return fallback == null ? "" : fallback;
        JSONObject payload = message.optJSONObject("payload");
        String value = firstNonEmpty(
                message.optString("sourceDeviceDisplayName", ""),
                message.optString("sourceDeviceName", ""),
                payload == null ? "" : payload.optString("sourceDeviceDisplayName", ""),
                payload == null ? "" : payload.optString("sourceDeviceName", ""),
                payload == null ? "" : payload.optString("deviceName", ""),
                fallback);
        return value;
    }

    static String resolveDeviceName(Context context, String sourceDeviceId, String preferred) {
        String cleanId = sourceDeviceId == null ? "" : sourceDeviceId.trim();
        String cleanPreferred = preferred == null ? "" : preferred.trim();
        if (!cleanPreferred.isEmpty() && !cleanPreferred.equals(cleanId)) return cleanPreferred;
        if (context != null && !cleanId.isEmpty()) {
            try {
                JSONObject pair = SecureLanConnection.load(context, cleanId);
                String paired = firstNonEmpty(
                        pair.optString("desktopName", ""),
                        pair.optString("displayName", ""),
                        pair.optString("peerName", ""));
                if (!paired.isEmpty() && !paired.equals(cleanId)) return paired;
            } catch (Exception ignored) {}
        }
        if (cleanId.startsWith("desktop-")) return "电脑";
        if (cleanId.startsWith("android-")) {
            String suffix = cleanId.length() > 8 ? cleanId.substring(cleanId.length() - 4) : cleanId;
            return "其他手机 · " + suffix;
        }
        return cleanPreferred.isEmpty() ? "其他设备" : cleanPreferred;
    }

    static String sourceDeviceName(Context context, JSONObject message, String fallback) {
        String id = message == null ? "" : message.optString("sourceDeviceId", "");
        return resolveDeviceName(context, id, sourceDeviceName(message, fallback));
    }

    private static ContentValues toValues(String account, JSONObject value) {
        ContentValues row = new ContentValues();
        row.put("account_id", account);
        String messageId = value.optString("messageId", "");
        row.put("message_id", messageId.isEmpty() ? "local-" + UUID.randomUUID().toString() : messageId);
        row.put("role", value.optString("role", "peer"));
        row.put("kind", value.optString("kind", "text"));
        row.put("content", value.optString("content", ""));
        row.put("time_ms", value.optLong("time", System.currentTimeMillis()));
        row.put("source_device_id", value.optString("sourceDeviceId", ""));
        row.put("source_device_name", value.optString("sourceDeviceName", ""));
        return row;
    }

    private static void migrateLegacyLocked(Context context) throws Exception {
        SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        if (prefs.getBoolean(MIGRATION_KEY, false)) return;
        JSONArray legacy;
        try { legacy = new JSONArray(prefs.getString(LEGACY_KEY, "[]")); }
        catch (Exception invalid) { legacy = new JSONArray(); }
        String account = currentAccount(context);
        if ("local".equals(account) && prefs.getString("token", "").isEmpty() && legacy.length() > 0) return;
        SQLiteDatabase db = get(context).getWritableDatabase();
        db.beginTransaction();
        try {
            for (int i = 0; i < legacy.length(); i++) {
                JSONObject item = legacy.optJSONObject(i);
                if (item == null) continue;
                JSONObject migrated = new JSONObject(item.toString());
                if ("photo".equals(migrated.optString("kind"))) {
                    String stable = stabilizeLegacyPhoto(context, migrated.optString("content", ""));
                    if (!stable.isEmpty()) migrated.put("content", stable);
                }
                if (migrated.optString("messageId").isEmpty())
                    migrated.put("messageId", "legacy-" + i + "-" + migrated.optLong("time", 0) + "-" + UUID.randomUUID());
                db.insertWithOnConflict("chat_messages", null, toValues(account, migrated), SQLiteDatabase.CONFLICT_IGNORE);
            }
            db.setTransactionSuccessful();
        } finally {
            db.endTransaction();
        }
        prefs.edit().putBoolean(MIGRATION_KEY, true).apply();
    }

    private static String stabilizeLegacyPhoto(Context context, String content) {
        if (content == null || !content.startsWith("content://")) return content == null ? "" : content;
        try {
            File folder = mediaDirectory(context, currentAccount(context));
            if (!folder.exists() && !folder.mkdirs()) return content;
            File target = new File(folder, "legacy-" + UUID.randomUUID().toString().replace("-", "") + ".jpg");
            long total = 0;
            try (InputStream input = context.getContentResolver().openInputStream(Uri.parse(content));
                 FileOutputStream output = new FileOutputStream(target)) {
                if (input == null) return content;
                byte[] buffer = new byte[65536];
                int count;
                while ((count = input.read(buffer)) > 0) {
                    total += count;
                    if (total > MobileAttachmentClient.LIMIT) throw new java.io.IOException("legacy_photo_too_large");
                    output.write(buffer, 0, count);
                }
                output.getFD().sync();
            }
            if (total <= 0) { target.delete(); return content; }
            return target.getAbsolutePath();
        } catch (Exception ignored) {
            return content;
        }
    }

    private static String currentAccount(Context context) {
        try { return SecureLanConnection.currentAccount(context); }
        catch (Exception ignored) { return "local"; }
    }

    private static File mediaDirectory(Context context, String account) throws Exception {
        return new File(context.getFilesDir(), "chat-media/" + hash(account));
    }

    private static String hash(String value) throws Exception {
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(value.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < 12; i++) out.append(String.format(java.util.Locale.ROOT, "%02x", digest[i]));
        return out.toString();
    }

    private static String firstNonEmpty(String... values) {
        if (values != null) for (String value : values) if (value != null && !value.trim().isEmpty()) return value.trim();
        return "";
    }

    private static void deleteRecursively(File file) {
        if (file == null || !file.exists()) return;
        if (file.isDirectory()) {
            File[] children = file.listFiles();
            if (children != null) for (File child : children) deleteRecursively(child);
        }
        file.delete();
    }
}
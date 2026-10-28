package cc.luoluoluo.yanzi.mobile;

import android.app.KeyguardManager;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.os.Build;
import android.os.Environment;
import android.util.Base64;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Comparator;
import java.util.Locale;

/**
 * Remote access to user-visible shared storage only, with TWO explicit opt-ins:
 * Android's "Allow access to manage all files" special permission, and Yanzi's
 * own in-app controls. No shell, app-private storage or destructive operations.
 */
final class MobileSharedStorage {
    private static final String READ_ENABLED = "remoteSharedFilesReadEnabled";
    private static final String WRITE_ENABLED = "remoteSharedFilesWriteEnabled";
    private static final int READ_CHUNK_LIMIT = 48 * 1024;
    private static final int WRITE_LIMIT = 32 * 1024;
    private static final int MAX_LISTED = 100;

    static boolean hasAndroidPermission() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && Environment.isExternalStorageManager();
    }

    static JSONObject status(Context context) throws Exception {
        boolean readEnabled = context.getSharedPreferences("yanzi-mobile", 0).getBoolean(READ_ENABLED, false);
        boolean writeEnabled = context.getSharedPreferences("yanzi-mobile", 0).getBoolean(WRITE_ENABLED, false);
        boolean permission = hasAndroidPermission();
        boolean locked = locked(context);
        return new JSONObject()
                .put("androidAllFilesGranted", permission)
                .put("readEnabledByUser", readEnabled)
                .put("writeEnabledByUser", writeEnabled)
                .put("deviceLocked", locked)
                .put("readReady", readEnabled && permission && !locked)
                .put("writeReady", readEnabled && writeEnabled && permission && !locked)
                .put("accessRoot", "shared-storage")
                .put("maxReadChunkBytes", READ_CHUNK_LIMIT)
                .put("maxWriteBytes", WRITE_LIMIT)
                .put("requiresSystemAllFilesPermission", true);
    }

    private static boolean locked(Context context) {
        KeyguardManager manager = (KeyguardManager) context.getSystemService(Context.KEYGUARD_SERVICE);
        return manager != null && manager.isDeviceLocked();
    }

    private static void requireAccess(Context context, boolean write) {
        boolean readEnabled = context.getSharedPreferences("yanzi-mobile", 0)
                .getBoolean(READ_ENABLED, false);
        if (!readEnabled) throw new SecurityException("remote_file_read_disabled_open_yanzi_my");
        if (write && !context.getSharedPreferences("yanzi-mobile", 0)
                .getBoolean(WRITE_ENABLED, false))
            throw new SecurityException("remote_file_write_disabled_open_yanzi_my");
        if (!hasAndroidPermission())
            throw new SecurityException("android_all_files_permission_required");
        if (locked(context)) throw new SecurityException("device_locked");
    }

    private static File root() throws IOException {
        return Environment.getExternalStorageDirectory().getCanonicalFile();
    }

    private static File resolve(String relative, boolean mustExist) throws IOException {
        if (relative == null || relative.indexOf('\0') >= 0 || relative.indexOf('\\') >= 0 ||
                relative.startsWith("/") || relative.indexOf(':') >= 0) throw new IOException("invalid_relative_path");
        if (relative.length() > 600) throw new IOException("path_too_long");
        File root = root();
        File file = relative.isEmpty() ? root : new File(root, relative).getCanonicalFile();
        String base = root.getPath();
        String actual = file.getPath();
        if (!actual.equals(base) && !actual.startsWith(base + File.separator))
            throw new SecurityException("path_outside_shared_storage");
        // Android/data, Android/obb and Android/media contain application-managed data.
        String rel = actual.equals(base) ? "" : actual.substring(base.length() + 1);
        if (rel.equals("Android") || rel.startsWith("Android" + File.separator))
            throw new SecurityException("android_app_data_scope_denied");
        if (mustExist && !file.exists()) throw new IOException("file_not_found");
        return file;
    }

    static JSONObject invoke(Context context, String name, JSONObject args) throws Exception {
        if ("mobile.storage.status".equals(name)) return status(context);
        if (args == null) args = new JSONObject();
        requireAccess(context, "mobile.storage.write".equals(name));
        switch (name) {
            case "mobile.storage.list": return list(args);
            case "mobile.storage.read": return read(args);
            case "mobile.storage.write": return write(args);
            case "mobile.photos.list": return photos(args);
            case "mobile.photos.preview": return preview(args);
            default: throw new IOException("unsupported_storage_capability");
        }
    }

    private static JSONObject list(JSONObject args) throws Exception {
        String path = args.optString("path", "");
        File dir = resolve(path, true);
        if (!dir.isDirectory()) throw new IOException("not_a_directory");
        File[] children = dir.listFiles();
        if (children == null) throw new IOException("directory_not_readable");
        Arrays.sort(children, Comparator.comparing(File::getName, String.CASE_INSENSITIVE_ORDER));
        JSONArray items = new JSONArray();
        int count = 0;
        for (File child : children) {
            if (count++ >= MAX_LISTED) break;
            try {
                File checked = resolve(path.isEmpty() ? child.getName() : path + "/" + child.getName(), true);
                if (checked.isDirectory() || checked.isFile()) {
                    items.put(new JSONObject().put("name", checked.getName())
                            .put("type", checked.isDirectory() ? "directory" : "file")
                            .put("size", checked.isFile() ? checked.length() : JSONObject.NULL)
                            .put("modifiedAt", checked.lastModified()));
                }
            } catch (SecurityException | IOException ignored) { /* Hide disallowed entries. */ }
        }
        return new JSONObject().put("scope", "shared-storage").put("path", path)
                .put("items", items).put("truncated", count > MAX_LISTED);
    }

    private static JSONObject read(JSONObject args) throws Exception {
        String path = args.getString("path");
        File file = resolve(path, true);
        if (!file.isFile()) throw new IOException("not_a_file");
        long offset = args.optLong("offset", 0L);
        int length = args.optInt("length", 16384);
        if (offset < 0 || offset > file.length() || length < 1 || length > READ_CHUNK_LIMIT)
            throw new IOException("invalid_read_range");
        int actual = (int)Math.min(length, file.length() - offset);
        byte[] chunk = new byte[actual];
        try (FileInputStream in = new FileInputStream(file)) {
            in.getChannel().position(offset);
            int done = 0;
            while (done < actual) {
                int n = in.read(chunk, done, actual - done);
                if (n < 0) throw new IOException("short_file_read");
                done += n;
            }
        }
        return new JSONObject().put("path", path).put("offset", offset)
                .put("size", file.length()).put("chunkBytes", actual)
                .put("encoding", "base64")
                .put("content", Base64.encodeToString(chunk, Base64.NO_WRAP))
                .put("eof", offset + actual >= file.length());
    }

    private static JSONObject write(JSONObject args) throws Exception {
        String path = args.getString("path");
        String encoded = args.getString("content");
        if (encoded.length() > WRITE_LIMIT * 4 / 3 + 8) throw new IOException("write_too_large");
        byte[] bytes;
        try { bytes = Base64.decode(encoded, Base64.DEFAULT); }
        catch (IllegalArgumentException e) { throw new IOException("invalid_base64_content"); }
        if (bytes.length > WRITE_LIMIT) throw new IOException("write_too_large");
        File destination = resolve(path, false);
        if (destination.equals(root())) throw new SecurityException("cannot_write_storage_root");
        File parent = destination.getParentFile();
        if (!parent.isDirectory()) throw new IOException("parent_directory_missing");
        if (destination.exists()) throw new IOException("file_exists_refusing_to_overwrite");

        // CREATE_NEW ensures an existing user file is never replaced, even in a race.
        try (java.io.OutputStream out = Files.newOutputStream(destination.toPath(),
                java.nio.file.StandardOpenOption.CREATE_NEW,
                java.nio.file.StandardOpenOption.WRITE)) {
            out.write(bytes);
        }
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(bytes);
        StringBuilder sha = new StringBuilder();
        for (byte octet : digest) sha.append(String.format(Locale.ROOT, "%02x", octet & 255));
        return new JSONObject().put("created", true).put("path", path)
                .put("size", bytes.length).put("sha256", sha.toString());
    }

    private static boolean isPhoto(File file) {
        String n = file.getName().toLowerCase(Locale.ROOT);
        return n.endsWith(".jpg") || n.endsWith(".jpeg") || n.endsWith(".png")
                || n.endsWith(".webp") || n.endsWith(".heic") || n.endsWith(".heif");
    }

    private static void walkPhotos(File folder, String relative, JSONArray output, int depth) throws Exception {
        if (depth > 3 || output.length() >= MAX_LISTED || !folder.isDirectory()) return;
        File[] files = folder.listFiles();
        if (files == null) return;
        Arrays.sort(files, (a,b) -> Long.compare(b.lastModified(), a.lastModified()));
        for (File file : files) {
            if (output.length() >= MAX_LISTED) break;
            String path = relative + "/" + file.getName();
            try {
                File checked = resolve(path, true);
                if (checked.isDirectory()) walkPhotos(checked, path, output, depth + 1);
                else if (checked.isFile() && isPhoto(checked)) output.put(
                        new JSONObject().put("path", path)
                                .put("name", checked.getName())
                                .put("size", checked.length())
                                .put("modifiedAt", checked.lastModified()));
            } catch (SecurityException | IOException ignored) {}
        }
    }

    private static JSONObject photos(JSONObject args) throws Exception {
        JSONArray items = new JSONArray();
        for (String folder : new String[]{"DCIM", "Pictures"}) {
            File target = resolve(folder, false);
            walkPhotos(target, folder, items, 0);
        }
        return new JSONObject().put("items", items).put("count", items.length())
                .put("scope", "DCIM,Pictures").put("truncated", items.length() >= MAX_LISTED);
    }

    private static JSONObject preview(JSONObject args) throws Exception {
        String path = args.getString("path");
        File file = resolve(path, true);
        if (!file.isFile() || !isPhoto(file)) throw new IOException("not_supported_image");
        if (file.length() > 50L * 1024 * 1024) throw new IOException("image_too_large");
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeFile(file.getPath(), bounds);
        if (bounds.outWidth < 1 || bounds.outHeight < 1) throw new IOException("invalid_image");
        BitmapFactory.Options opts = new BitmapFactory.Options();
        int sampling = 1;
        while (bounds.outWidth / sampling > 640 || bounds.outHeight / sampling > 640) sampling *= 2;
        opts.inSampleSize = sampling;
        Bitmap image = BitmapFactory.decodeFile(file.getPath(), opts);
        if (image == null) throw new IOException("image_decoder_unavailable");
        try {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            if (!image.compress(Bitmap.CompressFormat.JPEG, 60, out)) throw new IOException("image_encode_failed");
            if (out.size() > 200000) {
                out.reset();
                Bitmap smaller = Bitmap.createScaledBitmap(image,
                        Math.max(1,image.getWidth()/2), Math.max(1,image.getHeight()/2),true);
                try { smaller.compress(Bitmap.CompressFormat.JPEG, 48, out); }
                finally { smaller.recycle(); }
            }
            if (out.size() > 200000) throw new IOException("image_preview_too_large");
            return new JSONObject().put("path", path).put("width", image.getWidth())
                    .put("height", image.getHeight()).put("mimeType", "image/jpeg")
                    .put("encoding", "base64")
                    .put("content", Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP));
        } finally { image.recycle(); }
    }
}

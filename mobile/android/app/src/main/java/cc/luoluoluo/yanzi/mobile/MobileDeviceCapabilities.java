package cc.luoluoluo.yanzi.mobile;

import android.content.*;
import android.os.*;
import android.net.*;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

/** Native, read-only endpoint capabilities. No Activity or WebView is required. */
final class MobileDeviceCapabilities {
    static JSONArray catalog() throws Exception {
        JSONArray items = new JSONArray();
        for (String name : new String[]{"mobile.status.get", "mobile.capabilities.list", "mobile.files.list", "mobile.files.read", "mobile.extensions.list", "mobile.sync.status", "mobile.data.read"})
            items.put(new JSONObject().put("name", name).put("version", 1).put("available", true)
                    .put("readOnly", true).put("scope", "capability.invoke:" + name)
                    .put("parameters", name.equals("mobile.files.read") ? new JSONArray().put("name") : name.equals("mobile.data.read") ? new JSONArray().put("objectId") : new JSONArray()));
        return items;
    }

    static synchronized JSONObject snapshot(Context context) throws Exception {
        android.content.SharedPreferences login = context.getSharedPreferences("yanzi-mobile", 0);
        Intent battery = context.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        int level = battery == null ? -1 : battery.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
        int scale = battery == null ? -1 : battery.getIntExtra(BatteryManager.EXTRA_SCALE, -1);
        int percent = level < 0 || scale <= 0 ? -1 : level * 100 / scale;
        JSONObject network = new JSONObject().put("type", "none");
        ConnectivityManager manager = (ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (manager != null) for (Network candidate : manager.getAllNetworks()) {
            NetworkCapabilities caps = manager.getNetworkCapabilities(candidate);
            if (caps == null || !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) continue;
            String type = caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ? "wifi" : caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) ? "cellular" : "other";
            if (network.optString("type").equals("wifi")) continue;
            network = new JSONObject().put("type", type);
            LinkProperties links = manager.getLinkProperties(candidate);
            if (links != null) for (LinkAddress address : links.getLinkAddresses())
                if (address.getAddress() instanceof java.net.Inet4Address) network.put("ipv4", address.getAddress().getHostAddress());
        }
        JSONObject value = new JSONObject().put("manufacturer", Build.MANUFACTURER).put("model", Build.MODEL)
                .put("osVersion", Build.VERSION.RELEASE).put("appVersion", BuildConfig.VERSION_NAME)
                .put("battery", new JSONObject().put("available", percent >= 0).put("percent", percent)
                        .put("charging", battery != null && battery.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0))
                .put("network", network).put("notificationsEnabled", MobileEventNotifier.canNotify(context))
                .put("freeStorageBytes", context.getFilesDir().getUsableSpace());
        android.content.SharedPreferences cache = MobileAccountSync.store(context);
        // Storage free space fluctuates: fingerprint only externally meaningful status fields.
        JSONObject fingerprint = new JSONObject(value.toString()); fingerprint.remove("freeStorageBytes");
        long revision = cache.getLong("stateRevision", 0);
        if (!fingerprint.toString().equals(cache.getString("stateFingerprint", ""))) {
            revision++;
            if (!cache.edit().putString("stateFingerprint", fingerprint.toString()).putLong("stateRevision", revision).commit()) throw new IOException("state_commit_failed");
        }
        return new JSONObject().put("schemaVersion", 1).put("deviceId", login.getString("deviceId", ""))
                .put("collectedAt", java.time.Instant.now().toString()).put("revision", revision).put("value", value);
    }

    static File root(Context context) throws Exception {
        File root = context.getExternalFilesDir(Environment.DIRECTORY_DOCUMENTS);
        if (root == null) root = new File(context.getFilesDir(), "mobile-script-files");
        if (!root.exists() && !root.mkdirs()) throw new IOException("file_directory_unavailable");
        return root.getCanonicalFile();
    }

    static JSONObject invoke(Context context, String name, JSONObject arguments) throws Exception {
        if (arguments == null) arguments = new JSONObject();
        switch (name) {
            case "mobile.status.get": return snapshot(context);
            case "mobile.capabilities.list": return new JSONObject().put("items", catalog());
            case "mobile.sync.status": return MobileAccountSync.status(context);
            case "mobile.data.read": {
                String id = arguments.getString("objectId");
                if (!MobileAccountSync.supported(id)) throw new IOException("object_scope_denied");
                JSONObject object = new JSONObject(MobileAccountSync.store(context).getString("objects", "{}")).optJSONObject(id);
                return new JSONObject().put("exists", object != null && !object.optBoolean("deleted"))
                        .put("object", object == null ? JSONObject.NULL : object).put("sync", MobileAccountSync.status(context));
            }
            case "mobile.extensions.list": {
                JSONArray saved = new JSONArray(context.getSharedPreferences("yanzi-mobile",0).getString("mobileExtensions", "[]"));
                JSONArray items = new JSONArray();
                for (int i=0;i<saved.length();i++) {
                    JSONObject extension = saved.getJSONObject(i);
                    items.put(new JSONObject().put("id",extension.optString("id")).put("name",extension.optString("name"))
                            .put("version",extension.optString("version")).put("runtime",extension.optString("runtime")));
                }
                return new JSONObject().put("items",items);
            }
            case "mobile.files.list": {
                File[] files = root(context).listFiles(); JSONArray items = new JSONArray();
                if (files != null) for (File file : files) {
                    if (items.length() >= 200) break;
                    if (file.isFile() && file.getCanonicalFile().getParentFile().equals(root(context)))
                        items.put(new JSONObject().put("name",file.getName()).put("size",file.length()).put("modifiedAt",java.time.Instant.ofEpochMilli(file.lastModified()).toString()));
                }
                return new JSONObject().put("scope","yanzi-documents").put("items",items);
            }
            case "mobile.files.read": {
                String fileName = arguments.getString("name");
                if (fileName.isEmpty() || fileName.contains("/") || fileName.contains("\\") || fileName.equals(".") || fileName.equals("..")) throw new IOException("file_scope_denied");
                File root = root(context), file = new File(root,fileName).getCanonicalFile();
                if (!file.getParentFile().equals(root)) throw new IOException("file_scope_denied");
                if (!file.isFile()) throw new IOException("file_not_found");
                ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                try (InputStream input = new FileInputStream(file)) {
                    byte[] buffer = new byte[4096]; int count;
                    while ((count=input.read(buffer)) != -1) { if (bytes.size()+count>65536) throw new IOException("use_attachment_transfer_for_large_file"); bytes.write(buffer,0,count); }
                }
                return new JSONObject().put("name",fileName).put("encoding","base64")
                        .put("content",android.util.Base64.encodeToString(bytes.toByteArray(),android.util.Base64.NO_WRAP)).put("size",bytes.size());
            }
            default: throw new IOException("unsupported_mobile_capability");
        }
    }

    static JSONObject execute(Context context, JSONObject message) throws Exception {
        android.content.SharedPreferences login = context.getSharedPreferences("yanzi-mobile",0);
        if (!login.getString("deviceId", "").equals(message.optString("targetDeviceId"))) throw new IOException("target_device_mismatch");
        if (java.time.Instant.parse(message.getString("expiresAt")).toEpochMilli() <= System.currentTimeMillis()) throw new IOException("command_expired");
        JSONObject payload = message.getJSONObject("payload"), auth = payload.getJSONObject("authorization");
        String name = payload.getString("name"), type = auth.optString("type");
        JSONArray scopes = auth.optJSONArray("scopes"); boolean allowed = type.equals("account-owner");
        if (type.equals("device-grant") && scopes != null)
            for (int i=0;i<scopes.length();i++) if (scopes.optString(i).equals("capability.invoke:"+name)) allowed=true;
        if (!allowed) throw new IOException("capability_scope_denied");
        return invoke(context,name,payload.optJSONObject("arguments"));
    }
}

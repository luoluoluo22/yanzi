package cc.luoluoluo.yanzi.mobile;

import android.content.*;
import android.content.pm.*;
import android.database.Cursor;
import android.net.Uri;
import android.os.*;
import org.json.JSONObject;

/** Scoped companion-app bridge. Credentials never leave the Yanzi process. */
public final class ExtensionStorageProvider extends ContentProvider {
    @Override public boolean onCreate() {
        MobileNetworkRouting.initialize(getContext());
        return true;
    }
    @Override public Bundle call(String method, String extensionId, Bundle args) {
        Context context = getContext();
        context.enforceCallingOrSelfPermission(context.getPackageName()
                + ".permission.EXTENSION_STORAGE", "Yanzi companion permission required");
        String caller = getCallingPackage();
        try {
            if (caller == null || context.getPackageManager().checkSignatures(
                    caller, context.getPackageName()) != PackageManager.SIGNATURE_MATCH)
                throw new SecurityException("Untrusted companion");
            ApplicationInfo app = context.getPackageManager().getApplicationInfo(caller,
                    PackageManager.GET_META_DATA);
            String scopes = app.metaData == null ? "" : app.metaData.getString("yanzi.extensionScopes", "");
            if (extensionId == null || !java.util.Arrays.asList(scopes.split(",")).contains(extensionId))
                throw new SecurityException("Extension scope denied");
            if (args == null || !("read".equals(method) || "write".equals(method)))
                return result(new JSONObject().put("ok", false).put("error", "INVALID_REQUEST"));
            String key = args.getString("key", "");
            String content = args.getString("content", "");
            if (content.getBytes(java.nio.charset.StandardCharsets.UTF_8).length > 262144)
                return result(new JSONObject().put("ok", false).put("error", "TOO_LARGE"));
            if ("write".equals(method) && (!args.containsKey("expectedRevision")
                    || args.getLong("expectedRevision", -1) < 0))
                return result(new JSONObject().put("ok", false).put("error", "REVISION_REQUIRED"));
            SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
            String token = prefs.getString("token", "");
            if (token.isEmpty()) return result(new JSONObject().put("ok", false).put("error", "LOGIN_REQUIRED"));
            if ("write".equals(method) && !accountId(token).equals(args.getString("accountId", "")))
                return result(new JSONObject().put("ok", false).put("error", "ACCOUNT_CHANGED"));
            String base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd").replaceAll("/+$", "");
            JSONObject value;
            synchronized (ExtensionStorageProvider.class) {
                try { value = request(method, base, token, prefs, extensionId, key, content, args); }
                catch (Exception failure) {
                    // The shared storage client reports HTTP errors as ordinary exceptions.
                    if (failure.getMessage() == null || !failure.getMessage().contains("401")) throw failure;
                    String email = prefs.getString("email", ""), password = prefs.getString("password", "");
                    if (email.isEmpty() || password.isEmpty()) throw failure;
                    JSONObject login = MobileMessageClient.request(base, "/v1/auth/login", null, "POST",
                            new JSONObject().put("email", email).put("password", password));
                    String refreshed = login.getString("accessToken");
                    if (!prefs.getString("token", "").equals(token))
                        return result(new JSONObject().put("ok", false).put("error", "ACCOUNT_CHANGED"));
                    prefs.edit().putString("token", refreshed).commit();
                    token = refreshed;
                    value = request(method, base, token, prefs, extensionId, key, content, args);
                }
            }
            if (!token.equals(prefs.getString("token", "")))
                return result(new JSONObject().put("ok", false).put("error", "ACCOUNT_CHANGED"));
            // Opaque account identity prevents cached data crossing accounts; it is not a credential.
            value.put("accountId", accountId(token));
            return result(value);
        } catch (SecurityException denied) { throw denied; }
        catch (Exception failure) {
            android.util.Log.w("YanziCompanion", "Storage unavailable: " + failure.getClass().getSimpleName());
            Bundle out = new Bundle();
            out.putString("result", "{\"ok\":false,\"error\":\"SYNC_UNAVAILABLE\"}");
            return out;
        }
    }
    static String accountId(String token) throws Exception {
        JSONObject claims = new JSONObject(new String(android.util.Base64.decode(token.split("\\.")[1],
                android.util.Base64.URL_SAFE | android.util.Base64.NO_WRAP | android.util.Base64.NO_PADDING),
                java.nio.charset.StandardCharsets.UTF_8));
        return claims.getString("sub");
    }
    private static JSONObject request(String method, String base, String token, SharedPreferences prefs,
            String ext, String key, String content, Bundle args) throws Exception {
        if ("read".equals(method)) return MobileExtensionStorageClient.read(base, token, ext, key);
        return MobileExtensionStorageClient.write(base, token, prefs.getString("deviceId", ""),
                android.os.Build.MODEL, ext, key, content, args.getLong("expectedRevision"));
    }
    private static Bundle result(JSONObject value) { Bundle out = new Bundle(); out.putString("result", value.toString()); return out; }
    @Override public Cursor query(Uri u, String[] p, String s, String[] a, String o) { throw new UnsupportedOperationException(); }
    @Override public String getType(Uri u) { return null; }
    @Override public Uri insert(Uri u, ContentValues v) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri u, String s, String[] a) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri u, ContentValues v, String s, String[] a) { throw new UnsupportedOperationException(); }
}

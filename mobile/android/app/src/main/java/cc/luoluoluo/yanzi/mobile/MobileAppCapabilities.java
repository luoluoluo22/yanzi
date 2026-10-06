package cc.luoluoluo.yanzi.mobile;

import android.content.*;
import android.content.pm.*;
import android.net.Uri;
import android.provider.Settings;
import org.json.*;
import java.util.*;

/** Generic wrapper around user-visible Android apps. */
final class MobileAppCapabilities {
    private static JSONArray launchable(Context context) throws Exception {
        PackageManager pm = context.getPackageManager();
        Intent launcher = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<ResolveInfo> matches = pm.queryIntentActivities(launcher, 0);
        TreeMap<String, JSONObject> apps = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
        for (ResolveInfo info : matches) {
            if (info.activityInfo == null || info.activityInfo.packageName == null) continue;
            String pkg = info.activityInfo.packageName;
            if (apps.containsKey(pkg)) continue;
            String label = String.valueOf(info.loadLabel(pm));
            PackageInfo pi;
            try { pi = pm.getPackageInfo(pkg, 0); } catch (Exception ignored) { pi = null; }
            JSONObject app = new JSONObject()
                    .put("name", label)
                    .put("packageName", pkg)
                    .put("activity", info.activityInfo.name)
                    .put("enabled", info.activityInfo.enabled)
                    .put("system", info.activityInfo.applicationInfo != null &&
                            (info.activityInfo.applicationInfo.flags & ApplicationInfo.FLAG_SYSTEM) != 0);
            if (pi != null) app.put("versionName", pi.versionName == null ? "" : pi.versionName)
                    .put("versionCode", androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(pi));
            apps.put(pkg, app);
        }
        JSONArray out = new JSONArray();
        ArrayList<JSONObject> sorted = new ArrayList<>(apps.values());
        sorted.sort((a,b)->a.optString("name").compareToIgnoreCase(b.optString("name")));
        for (JSONObject app : sorted) out.put(app);
        return out;
    }

    private static JSONObject resolve(Context context, String query) throws Exception {
        if (query == null || query.trim().isEmpty()) throw new IllegalArgumentException("app_required");
        String wanted = query.trim();
        JSONArray items = launchable(context);
        ArrayList<JSONObject> exact = new ArrayList<>(), partial = new ArrayList<>();
        for (int i=0;i<items.length();i++) {
            JSONObject app = items.getJSONObject(i);
            String name = app.optString("name"), pkg = app.optString("packageName");
            if (pkg.equalsIgnoreCase(wanted) || name.equalsIgnoreCase(wanted)) exact.add(app);
            else if (name.toLowerCase(Locale.ROOT).contains(wanted.toLowerCase(Locale.ROOT)) ||
                    pkg.toLowerCase(Locale.ROOT).contains(wanted.toLowerCase(Locale.ROOT))) partial.add(app);
        }
        if (exact.size() == 1) return exact.get(0);
        if (exact.size() > 1) throw new IllegalArgumentException("app_ambiguous");
        if (partial.size() == 1) return partial.get(0);
        if (partial.size() > 1) throw new IllegalArgumentException("app_ambiguous");
        throw new PackageManager.NameNotFoundException("app_not_found:" + wanted);
    }

    private static Intent newTask(Intent intent) {
        return intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
    }

    static JSONObject invoke(Context context, String name, JSONObject args) throws Exception {
        PackageManager pm = context.getPackageManager();
        if (args == null) args = new JSONObject();
        switch (name) {
            case "mobile.apps.list": {
                String query = args.optString("query", "").trim().toLowerCase(Locale.ROOT);
                int limit = Math.max(1, Math.min(500, args.optInt("limit", 200)));
                JSONArray all = launchable(context), items = new JSONArray();
                for (int i=0;i<all.length() && items.length()<limit;i++) {
                    JSONObject app = all.getJSONObject(i);
                    if (query.isEmpty() || app.optString("name").toLowerCase(Locale.ROOT).contains(query) ||
                            app.optString("packageName").toLowerCase(Locale.ROOT).contains(query)) items.put(app);
                }
                return new JSONObject().put("items", items).put("count", items.length());
            }
            case "mobile.apps.info":
                return resolve(context, args.getString("app"));
            case "mobile.apps.open": {
                JSONObject app = resolve(context, args.getString("app"));
                Intent launch = pm.getLaunchIntentForPackage(app.getString("packageName"));
                if (launch == null) throw new IllegalStateException("app_not_launchable");
                context.startActivity(newTask(launch));
                return new JSONObject().put("opened", true).put("app", app);
            }
            case "mobile.apps.openUri": {
                String uri = args.getString("uri");
                if (uri.trim().isEmpty()) throw new IllegalArgumentException("uri_required");
                Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(uri));
                String appQuery = args.optString("app", "").trim();
                JSONObject app = null;
                if (!appQuery.isEmpty()) {
                    app = resolve(context, appQuery);
                    intent.setPackage(app.getString("packageName"));
                }
                if (intent.resolveActivity(pm) == null) throw new IllegalStateException("uri_handler_not_found");
                context.startActivity(newTask(intent));
                return new JSONObject().put("opened", true).put("uri", uri)
                        .put("packageName", app == null ? JSONObject.NULL : app.getString("packageName"));
            }
            case "mobile.apps.shareText": {
                String text = args.getString("text");
                if (text.isEmpty()) throw new IllegalArgumentException("text_required");
                Intent intent = new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text);
                String appQuery = args.optString("app", "").trim();
                JSONObject app = null;
                if (!appQuery.isEmpty()) {
                    app = resolve(context, appQuery);
                    intent.setPackage(app.getString("packageName"));
                    if (intent.resolveActivity(pm) == null) throw new IllegalStateException("share_target_not_supported");
                    context.startActivity(newTask(intent));
                } else {
                    context.startActivity(newTask(Intent.createChooser(intent, "选择分享应用")));
                }
                return new JSONObject().put("opened", true)
                        .put("packageName", app == null ? JSONObject.NULL : app.getString("packageName"));
            }
            case "mobile.apps.settings": {
                JSONObject app = resolve(context, args.getString("app"));
                Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                        Uri.parse("package:" + app.getString("packageName")));
                context.startActivity(newTask(intent));
                return new JSONObject().put("opened", true).put("app", app);
            }
            default:
                throw new IllegalArgumentException("unsupported_mobile_app_capability");
        }
    }
}

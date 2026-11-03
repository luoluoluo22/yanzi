package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.net.URI;
import java.util.ArrayList;
import java.util.List;

/**
 * Account-scoped, read-only model service catalog.
 * This is not an API credential store: secrets and localhost destinations are
 * deliberately never applied as Android networking settings.
 */
final class MobileModelServiceCatalog {
    static final String OBJECT_ID = "settings.ai";
    private MobileModelServiceCatalog() {}

    private static String text(JSONObject json, String camel, String pascal) {
        if (json == null) return "";
        return json.optString(camel, json.optString(pascal, "")).trim();
    }

    private static JSONArray array(JSONObject json, String camel, String pascal) {
        if (json == null) return null;
        JSONArray value = json.optJSONArray(camel);
        return value != null ? value : json.optJSONArray(pascal);
    }

    /** Strip authentication and non-model settings before writing to phone storage. */
    static JSONObject sanitizeObject(JSONObject remote) throws Exception {
        JSONObject source = remote.optJSONObject("payload");
        JSONObject safePayload = new JSONObject();
        JSONArray safe = new JSONArray();
        JSONArray incoming = array(source, "aiServiceProviders", "AiServiceProviders");
        if (incoming != null) for (int i = 0; i < Math.min(incoming.length(), 200); i++) {
            JSONObject provider = incoming.optJSONObject(i);
            if (provider == null) continue;
            String id = text(provider, "id", "Id");
            String name = text(provider, "name", "Name");
            if (id.isEmpty() || id.length() > 160 || name.length() > 200) continue;
            JSONArray modelArray = array(provider, "models", "Models");
            JSONArray models = new JSONArray();
            if (modelArray != null) for (int j = 0; j < Math.min(100, modelArray.length()); j++) {
                String model = modelArray.optString(j, "").trim();
                if (!model.isEmpty() && model.length() <= 160) models.put(model);
            }
            String endpoint = text(provider, "baseUrl", "BaseUrl");
            String host = "";
            boolean localOnly = false;
            try {
                URI parsed = URI.create(endpoint);
                host = parsed.getHost() == null ? "" : parsed.getHost().toLowerCase(java.util.Locale.ROOT);
                localOnly = "localhost".equals(host) || "127.0.0.1".equals(host) ||
                    "::1".equals(host) || host.startsWith("192.168.") || host.startsWith("10.") ||
                    host.startsWith("172.16.") || host.startsWith("172.17.") ||
                    host.startsWith("172.18.") || host.startsWith("172.19.") ||
                    host.startsWith("172.20.") || host.startsWith("172.21.") ||
                    host.startsWith("172.22.") || host.startsWith("172.23.") ||
                    host.startsWith("172.24.") || host.startsWith("172.25.") ||
                    host.startsWith("172.26.") || host.startsWith("172.27.") ||
                    host.startsWith("172.28.") || host.startsWith("172.29.") ||
                    host.startsWith("172.30.") || host.startsWith("172.31.");
            } catch (RuntimeException ignored) {
                localOnly = true;
            }
            JSONObject item = new JSONObject()
                .put("id", id)
                .put("name", name)
                .put("providerType", text(provider, "providerType", "ProviderType"))
                .put("selectedModel", text(provider, "selectedModel", "SelectedModel"))
                .put("ownerExtensionId", text(provider, "ownerExtensionId", "OwnerExtensionId"))
                .put("enabled", provider.optBoolean("isEnabled", provider.optBoolean("IsEnabled", true)))
                .put("localOnly", localOnly)
                .put("models", models);
            safe.put(item);
        }
        safePayload.put("aiServiceProviders", safe)
            .put("activeServiceProviderId", text(source, "activeServiceProviderId", "ActiveServiceProviderId"));
        return new JSONObject()
            .put("objectId", OBJECT_ID)
            .put("revision", remote.optLong("revision", 0))
            .put("deleted", remote.optBoolean("deleted", false))
            .put("payload", safePayload);
    }

    static JSONArray read(Context context) {
        try {
            String cache = MobileAccountSync.store(context).getString("objects", "{}");
            JSONObject envelope = new JSONObject(cache).optJSONObject(OBJECT_ID);
            if (envelope == null || envelope.optBoolean("deleted")) return new JSONArray();
            JSONObject data = envelope.optJSONObject("payload");
            JSONArray providers = data == null ? null : data.optJSONArray("aiServiceProviders");
            return providers == null ? new JSONArray() : providers;
        } catch (Exception ignored) {
            return new JSONArray();
        }
    }

    static String activeProviderId(Context context) {
        try {
            JSONObject object = new JSONObject(MobileAccountSync.store(context).getString("objects", "{}"))
                .optJSONObject(OBJECT_ID);
            JSONObject payload = object == null ? null : object.optJSONObject("payload");
            return payload == null ? "" : payload.optString("activeServiceProviderId", "");
        } catch (Exception ignored) {
            return "";
        }
    }

    static String[] displayItems(Context context) {
        JSONArray providers = read(context);
        List<String> labels = new ArrayList<>();
        String active = activeProviderId(context);
        for (int i = 0; i < providers.length(); i++) {
            JSONObject p = providers.optJSONObject(i);
            if (p == null) continue;
            String model = p.optString("selectedModel", "");
            if (model.isEmpty()) {
                JSONArray all = p.optJSONArray("models");
                model = all != null && all.length() > 0 ? all.optString(0) : "尚无模型";
            }
            String label = (active.equals(p.optString("id")) ? "★ " : "") +
                p.optString("name", "模型服务") + " · " + model;
            if (!p.optBoolean("enabled", true)) label += "（已停用）";
            if (p.optBoolean("localOnly")) label += "（需连接电脑）";
            labels.add(label);
        }
        return labels.toArray(new String[0]);
    }
}

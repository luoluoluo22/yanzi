package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.SharedPreferences;
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;

/** AI-tab model and desktop chooser. Selection never stores provider API keys. */
final class MobileAiModelPicker {
    private MobileAiModelPicker() {}
    private static final String KEY_PROVIDER = "aiSelectedProviderId";
    private static final String KEY_MODEL = "aiSelectedModel";
    private static final String KEY_DESKTOP = "aiSelectedDesktopDeviceId";

    static String selectedModelId(Activity activity, SharedPreferences preferences) {
        if (preferences.getBoolean("aiUseLegacy", false)) return "";
        JSONArray providers = MobileModelServiceCatalog.read(activity);
        String id = preferences.getString(KEY_PROVIDER, "");
        if (id.isEmpty()) id = MobileModelServiceCatalog.activeProviderId(activity);
        for (int i = 0; i < providers.length(); i++) {
            JSONObject p = providers.optJSONObject(i);
            if (p == null || !p.optBoolean("enabled", true) ||
                !id.equals(p.optString("id", ""))) continue;
            JSONArray models = p.optJSONArray("models");
            String model = preferences.getString(KEY_MODEL, "");
            if (model.isEmpty()) model = p.optString("selectedModel", "");
            if (models == null) continue;
            for (int j = 0; j < models.length(); j++)
                if (model.equals(models.optString(j, "")))
                    return id + "/" + model;
        }
        return "";
    }

    static String title(Activity activity, SharedPreferences preferences) {
        if (preferences.getBoolean("aiUseLegacy", false))
            return "旧版模型 · " + preferences.getString("aiModel", "未配置");
        String full = selectedModelId(activity, preferences);
        JSONArray providers = MobileModelServiceCatalog.read(activity);
        for (int i = 0; i < providers.length(); i++) {
            JSONObject p = providers.optJSONObject(i);
            if (p != null && full.startsWith(p.optString("id", "") + "/"))
                return p.optString("name", "模型服务") + " · " +
                    full.substring(p.optString("id").length() + 1);
        }
        return providers.length() == 0 ? "模型尚未同步" : "选择模型";
    }

    static void show(Activity activity, SharedPreferences preferences, ExecutorService executor,
                     String base, String token, Runnable changed) {
        JSONArray providers = MobileModelServiceCatalog.read(activity);
        List<JSONObject> eligible = new ArrayList<>();
        List<String> labels = new ArrayList<>();
        String configuredDesktop = preferences.getString(KEY_DESKTOP, "");
        labels.add("运行电脑：" + (configuredDesktop.isEmpty() ? "自动选择在线电脑" : configuredDesktop));
        labels.add("使用旧版单模型配置");
        for (int i = 0; i < providers.length(); i++) {
            JSONObject p = providers.optJSONObject(i);
            if (p == null || !p.optBoolean("enabled", true)) continue;
            JSONArray models = p.optJSONArray("models");
            if (models == null || models.length() == 0) continue;
            eligible.add(p);
            String status = (p.optString("id").equals(preferences.getString(KEY_PROVIDER, "")) ? "✓ " : "");
            labels.add(status + p.optString("name", "提供商") + " · " + models.length() + " 个模型");
        }
        if (providers.length() == 0) labels.add("模型服务尚未同步，请确认账号云同步正常");
        new AlertDialog.Builder(activity)
            .setTitle("AI 对话设置 · " + title(activity, preferences))
            .setItems(labels.toArray(new String[0]), (dialog, index) -> {
                if (index == 0) {
                    chooseDesktop(activity, preferences, executor, base, token, changed);
                } else if (index == 1) {
                    preferences.edit().putBoolean("aiUseLegacy", true).apply();
                    changed.run();
                } else if (index - 2 < eligible.size()) {
                    chooseModel(activity, preferences, eligible.get(index - 2), changed);
                }
            })
            .setNegativeButton("关闭", null)
            .show();
    }

    private static void chooseModel(Activity activity, SharedPreferences preferences,
                                    JSONObject provider, Runnable changed) {
        JSONArray models = provider.optJSONArray("models");
        if (models == null || models.length() == 0) return;
        String[] options = new String[models.length()];
        String selected = preferences.getString(KEY_MODEL, "");
        for (int i = 0; i < models.length(); i++) {
            String model = models.optString(i, "");
            options[i] = (provider.optString("id").equals(preferences.getString(KEY_PROVIDER, ""))
                && model.equals(selected) ? "✓ " : "") + model;
        }
        new AlertDialog.Builder(activity)
            .setTitle(provider.optString("name", "选择模型"))
            .setItems(options, (dialog, which) -> {
                String model = models.optString(which, "");
                if (model.isEmpty()) return;
                preferences.edit()
                    .putString(KEY_PROVIDER, provider.optString("id"))
                    .putString(KEY_MODEL, model)
                    .putBoolean("aiUseLegacy", false)
                    .apply();
                changed.run();
            })
            .setNegativeButton("返回", null).show();
    }

    private static void chooseDesktop(Activity activity, SharedPreferences preferences,
                                      ExecutorService executor, String base, String token, Runnable changed) {
        executor.execute(() -> {
            try {
                JSONArray devices = MobileAccountModelChat.onlineDesktops(base, token);
                List<String> labels = new ArrayList<>();
                labels.add("自动选择（仅一台在线时）");
                for (int i = 0; i < devices.length(); i++) {
                    JSONObject item = devices.optJSONObject(i);
                    labels.add(item == null ? "未知电脑" :
                        item.optString("displayName", item.optString("deviceId", "电脑")) +
                        " · " + item.optString("deviceId", ""));
                }
                activity.runOnUiThread(() -> new AlertDialog.Builder(activity)
                    .setTitle("运行模型的电脑")
                    .setItems(labels.toArray(new String[0]), (dialog, which) -> {
                        String id = which == 0 ? "" :
                            devices.optJSONObject(which - 1).optString("deviceId", "");
                        preferences.edit().putString(KEY_DESKTOP, id).apply();
                        changed.run();
                    })
                    .setNegativeButton("返回", null)
                    .show());
            } catch (Exception error) {
                activity.runOnUiThread(() -> Toast.makeText(activity,
                    "无法读取在线电脑：" + error.getClass().getSimpleName(), Toast.LENGTH_LONG).show());
            }
        });
    }
}

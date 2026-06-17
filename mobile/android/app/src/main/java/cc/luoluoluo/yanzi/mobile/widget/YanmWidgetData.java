package cc.luoluoluo.yanzi.mobile.widget;

import android.appwidget.AppWidgetManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class YanmWidgetData {
    public static final String PREFS_NAME = "yanzi-mobile";
    public static final String CACHE_YANM = "cacheYanmJson";
    public static final String COMPONENT_WIDGET_PREFIX = "yanmComponentWidget.";

    private YanmWidgetData() {
    }

    public static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }

    public static JSONObject readYanm(Context context) {
        String yanmJson = prefs(context).getString(CACHE_YANM, "");
        if (yanmJson == null || yanmJson.trim().isEmpty() || "{}".equals(yanmJson.trim())) {
            return null;
        }
        try {
            return new JSONObject(yanmJson);
        } catch (Exception ignored) {
            return null;
        }
    }

    public static List<ComponentInfo> readComponents(Context context) {
        List<ComponentInfo> result = new ArrayList<>();
        JSONObject yanm = readYanm(context);
        JSONArray components = yanm == null ? null : firstArray(yanm, "components", "Components");
        if (components == null) {
            return result;
        }
        for (int i = 0; i < components.length(); i++) {
            JSONObject component = components.optJSONObject(i);
            if (component == null) {
                continue;
            }
            String title = firstNonEmpty(
                    component.optString("title"),
                    component.optString("Title"),
                    component.optString("name"),
                    component.optString("Name"),
                    "组件 " + (i + 1));
            String id = firstNonEmpty(component.optString("id"), component.optString("Id"), title);
            String stateKey = resolveStateKey(component, id);
            result.add(new ComponentInfo(id, title, stateKey, component));
        }
        return result;
    }

    public static ComponentInfo findComponent(Context context, String componentId) {
        if (componentId == null || componentId.trim().isEmpty()) {
            return null;
        }
        for (ComponentInfo component : readComponents(context)) {
            if (componentId.equalsIgnoreCase(component.id)) {
                return component;
            }
        }
        return null;
    }

    public static JSONObject readComponentState(Context context) {
        JSONObject yanm = readYanm(context);
        JSONObject state = firstObject(yanm, "componentState", "ComponentState");
        return state == null ? new JSONObject() : state;
    }

    public static String readComponentValue(Context context, ComponentInfo component) {
        if (component == null) {
            return "";
        }
        JSONObject state = readComponentState(context);
        String value = state.optString(component.stateKey, "");
        if (!value.trim().isEmpty()) {
            return value;
        }
        return summarizeStaticComponent(component.component);
    }

    public static void saveComponentWidget(Context context, int appWidgetId, String componentId, String stateKey) {
        prefs(context).edit()
                .putString(COMPONENT_WIDGET_PREFIX + appWidgetId + ".componentId", componentId == null ? "" : componentId)
                .putString(COMPONENT_WIDGET_PREFIX + appWidgetId + ".stateKey", stateKey == null ? "" : stateKey)
                .apply();
    }

    public static void deleteComponentWidget(Context context, int appWidgetId) {
        prefs(context).edit()
                .remove(COMPONENT_WIDGET_PREFIX + appWidgetId + ".componentId")
                .remove(COMPONENT_WIDGET_PREFIX + appWidgetId + ".stateKey")
                .apply();
    }

    public static String getWidgetComponentId(Context context, int appWidgetId) {
        return prefs(context).getString(COMPONENT_WIDGET_PREFIX + appWidgetId + ".componentId", "");
    }

    public static String getWidgetStateKey(Context context, int appWidgetId) {
        return prefs(context).getString(COMPONENT_WIDGET_PREFIX + appWidgetId + ".stateKey", "");
    }

    public static void refreshComponentWidgets(Context context) {
        AppWidgetManager manager = AppWidgetManager.getInstance(context);
        int[] ids = manager.getAppWidgetIds(new ComponentName(context, YanmComponentWidgetProvider.class));
        if (ids.length == 0) {
            return;
        }
        for (int id : ids) {
            YanmComponentWidgetProvider.updateWidget(context, manager, id);
        }
    }

    public static String resolveStateKey(JSONObject component, String componentId) {
        String explicit = firstNonEmpty(
                component.optString("widgetStateKey"),
                component.optString("WidgetStateKey"),
                component.optString("stateKey"),
                component.optString("StateKey"),
                component.optString("key"),
                component.optString("Key"));
        if (!explicit.isEmpty()) {
            return explicit;
        }

        // 智能自动键名推断
        String title = firstNonEmpty(
                component.optString("title"),
                component.optString("Title"),
                component.optString("name"),
                component.optString("Name")
        ).toLowerCase(Locale.ROOT);

        String id = (componentId != null ? componentId : "").toLowerCase(Locale.ROOT);

        if (title.contains("便签") || title.contains("note") || id.contains("note")) {
            return "yanm.sticky.note.v1";
        }
        if (title.contains("待办") || title.contains("todo") || id.contains("todo")) {
            return "yanm.todo.items." + (componentId != null ? componentId : "default");
        }
        if (title.contains("喝水") || title.contains("心情") || title.contains("mood") || title.contains("water")
                || id.contains("mood") || id.contains("water")) {
            return "yanm.mood.water.v1";
        }
        if (title.contains("习惯") || title.contains("打卡") || title.contains("habit") || id.contains("habit")) {
            return "yanm.habits.v1";
        }
        if (title.contains("书签") || title.contains("bookmark") || id.contains("bookmark")) {
            return "yanm.bookmarks.items." + (componentId != null ? componentId : "default");
        }

        return "note";
    }

    public static String summarize(String value) {
        String text = stripHtml(value == null ? "" : value);
        text = text.replaceAll("\\s+", " ").trim();
        return text.length() > 500 ? text.substring(0, 500) + "..." : text;
    }

    private static String summarizeStaticComponent(JSONObject component) {
        if (component == null) {
            return "";
        }
        String text = firstNonEmpty(
                component.optString("text"),
                component.optString("Text"),
                component.optString("content"),
                component.optString("Content"),
                component.optString("note"),
                component.optString("Note"),
                component.optString("description"),
                component.optString("Description"),
                component.optString("html"),
                component.optString("Html"),
                component.optString("contentHtml"),
                component.optString("ContentHtml"));
        return summarize(text);
    }

    private static String stripHtml(String text) {
        String value = text == null ? "" : text;
        value = value.replaceAll("(?i)<br\\s*/?>", "\n");
        value = value.replaceAll("(?i)</?(p|div|li|h[1-6]|tr)[^>]*>", "\n");
        value = value.replaceAll("<[^>]*>", "");
        value = value.replace("&nbsp;", " ")
                .replace("&amp;", "&")
                .replace("&quot;", "\"")
                .replace("&#39;", "'");
        return value.trim();
    }

    public static JSONArray firstArray(JSONObject obj, String... keys) {
        if (obj == null) {
            return null;
        }
        for (String key : keys) {
            JSONArray arr = obj.optJSONArray(key);
            if (arr != null) {
                return arr;
            }
        }
        return null;
    }

    public static JSONObject firstObject(JSONObject obj, String... keys) {
        if (obj == null) {
            return null;
        }
        for (String key : keys) {
            JSONObject value = obj.optJSONObject(key);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    public static String firstNonEmpty(String... values) {
        for (String value : values) {
            if (value != null && !value.trim().isEmpty()) {
                return value.trim();
            }
        }
        return "";
    }

    public static final class ComponentInfo {
        public final String id;
        public final String title;
        public final String stateKey;
        public final JSONObject component;

        ComponentInfo(String id, String title, String stateKey, JSONObject component) {
            this.id = id;
            this.title = title;
            this.stateKey = stateKey;
            this.component = component;
        }
    }
}

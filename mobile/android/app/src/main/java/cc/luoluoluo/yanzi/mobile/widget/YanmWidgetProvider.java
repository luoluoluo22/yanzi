package cc.luoluoluo.yanzi.mobile.widget;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.view.View;
import android.widget.RemoteViews;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

import cc.luoluoluo.yanzi.mobile.MainActivity;
import cc.luoluoluo.yanzi.mobile.R;

public final class YanmWidgetProvider extends AppWidgetProvider {

    public static final String ACTION_REFRESH_YANM = "cc.luoluoluo.yanzi.mobile.widget.ACTION_REFRESH_YANM";

    @Override
    public void onUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds) {
        SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        String yanmJson = prefs.getString("cacheYanmJson", "");
        String sortedIdsJson = prefs.getString("sortedComponentIds", "[]");

        List<String> sortedComponentIds = new ArrayList<>();
        try {
            JSONArray arr = new JSONArray(sortedIdsJson);
            for (int i = 0; i < arr.length(); i++) {
                sortedComponentIds.add(arr.getString(i));
            }
        } catch (Exception ignored) {}

        List<JSONObject> finalComponents = new ArrayList<>();
        try {
            if (yanmJson != null && !yanmJson.trim().isEmpty() && !yanmJson.equals("{}")) {
                JSONObject yanmObj = new JSONObject(yanmJson);
                JSONArray components = firstArray(yanmObj, "components", "Components");
                if (components != null && components.length() > 0) {
                    List<JSONObject> sortedList = new ArrayList<>();
                    List<JSONObject> remainingList = new ArrayList<>();
                    for (int i = 0; i < components.length(); i++) {
                        JSONObject comp = components.optJSONObject(i);
                        if (comp != null) {
                            String compId = firstNonEmpty(comp.optString("id"), comp.optString("Id"),
                                    comp.optString("title"), comp.optString("Title"), comp.optString("name"), comp.optString("Name"), "comp_" + i);
                            int sortedIndex = sortedComponentIds.indexOf(compId);
                            if (sortedIndex >= 0) {
                                sortedList.add(comp);
                            } else {
                                remainingList.add(comp);
                            }
                        }
                    }

                    // 排序
                    if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
                        sortedList.sort((c1, c2) -> {
                            String id1 = firstNonEmpty(c1.optString("id"), c1.optString("Id"),
                                    c1.optString("title"), c1.optString("Title"), c1.optString("name"), c1.optString("Name"));
                            String id2 = firstNonEmpty(c2.optString("id"), c2.optString("Id"),
                                    c2.optString("title"), c2.optString("Title"), c2.optString("name"), c2.optString("Name"));
                            return Integer.compare(sortedComponentIds.indexOf(id1), sortedComponentIds.indexOf(id2));
                        });
                    }

                    finalComponents.addAll(sortedList);
                    finalComponents.addAll(remainingList);
                }
            }
        } catch (Exception ignored) {}

        boolean hasData = !finalComponents.isEmpty();

        for (int appWidgetId : appWidgetIds) {
            RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm);

            // 绑定一键刷新 PendingIntent
            Intent refreshIntent = new Intent(context, YanmWidgetProvider.class);
            refreshIntent.setAction(ACTION_REFRESH_YANM);
            PendingIntent refreshPI = PendingIntent.getBroadcast(
                    context, appWidgetId, refreshIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            views.setOnClickPendingIntent(R.id.widget_yanm_refresh, refreshPI);

            if (!hasData) {
                views.setViewVisibility(R.id.widget_yanm_empty_text, View.VISIBLE);
                views.setViewVisibility(R.id.widget_yanm_list_container, View.GONE);
            } else {
                views.setViewVisibility(R.id.widget_yanm_empty_text, View.GONE);
                views.setViewVisibility(R.id.widget_yanm_list_container, View.VISIBLE);

                int[] itemLayouts = {
                        R.id.widget_yanm_item_1,
                        R.id.widget_yanm_item_2,
                        R.id.widget_yanm_item_3
                };
                int[] titleLayouts = {
                        R.id.widget_item_title_1,
                        R.id.widget_item_title_2,
                        R.id.widget_item_title_3
                };
                int[] typeLayouts = {
                        R.id.widget_item_type_1,
                        R.id.widget_item_type_2,
                        R.id.widget_item_type_3
                };
                int[] summaryLayouts = {
                        R.id.widget_item_summary_1,
                        R.id.widget_item_summary_2,
                        R.id.widget_item_summary_3
                };
                int[] dividerLayouts = {
                        R.id.widget_yanm_divider_1,
                        R.id.widget_yanm_divider_2
                };

                for (int i = 0; i < 3; i++) {
                    int itemLayoutId = itemLayouts[i];
                    int titleLayoutId = titleLayouts[i];
                    int typeLayoutId = typeLayouts[i];
                    int summaryLayoutId = summaryLayouts[i];

                    if (i < finalComponents.size()) {
                        JSONObject component = finalComponents.get(i);
                        String title = firstNonEmpty(
                                component.optString("title"),
                                component.optString("Title"),
                                component.optString("name"),
                                component.optString("Name"),
                                "组件 " + (i + 1));
                        String type = firstNonEmpty(
                                component.optString("type"),
                                component.optString("Type"),
                                component.optString("kind"),
                                component.optString("Kind"),
                                "component");
                        String componentId = firstNonEmpty(component.optString("id"), component.optString("Id"), title);
                        String summary = summarizeYanmComponent(component);

                        views.setViewVisibility(itemLayoutId, View.VISIBLE);
                        views.setTextViewText(titleLayoutId, title);
                        views.setTextViewText(typeLayoutId, type.toUpperCase());
                        views.setTextViewText(summaryLayoutId, summary);

                        // 关联点击事件唤醒 App 切换到 yanm Tab 页面
                        Intent clickIntent = new Intent(context, MainActivity.class);
                        clickIntent.setAction("cc.luoluoluo.yanzi.mobile.yanm");
                        clickIntent.putExtra("target_component_id", componentId);

                        // 使用唯一的 requestCode (100 + i)
                        PendingIntent runPI = PendingIntent.getActivity(
                                context, 100 + i, clickIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
                        views.setOnClickPendingIntent(itemLayoutId, runPI);
                        views.setOnClickPendingIntent(titleLayoutId, runPI);
                        views.setOnClickPendingIntent(summaryLayoutId, runPI);

                        // 填充分割线的显示
                        if (i < 2) {
                            views.setViewVisibility(dividerLayouts[i], View.VISIBLE);
                        }
                    } else {
                        views.setViewVisibility(itemLayoutId, View.GONE);
                        if (i < 2) {
                            views.setViewVisibility(dividerLayouts[i], View.GONE);
                        }
                    }
                }
            }

            appWidgetManager.updateAppWidget(appWidgetId, views);
        }
    }

    @Override
    public void onReceive(Context context, Intent intent) {
        super.onReceive(context, intent);
        String action = intent.getAction();

        if (ACTION_REFRESH_YANM.equals(action)) {
            AppWidgetManager appWidgetManager = AppWidgetManager.getInstance(context);
            ComponentName thisWidget = new ComponentName(context, YanmWidgetProvider.class);
            int[] appWidgetIds = appWidgetManager.getAppWidgetIds(thisWidget);

            // 重新绘制小部件卡片外层状态
            onUpdate(context, appWidgetManager, appWidgetIds);

            Toast.makeText(context, "燕幕组件列表已刷新。", Toast.LENGTH_SHORT).show();
        }
    }

    private static String summarizeYanmComponent(JSONObject component) {
        String text = firstNonEmpty(
                component.optString("text"),
                component.optString("Text"),
                component.optString("content"),
                component.optString("Content"),
                component.optString("note"),
                component.optString("Note"),
                component.optString("description"),
                component.optString("Description"));
        if (text.isEmpty()) {
            text = component.toString();
        }
        text = text.replaceAll("\\s+", " ").trim();
        return text.length() > 140 ? text.substring(0, 140) + "..." : text;
    }

    private static JSONArray firstArray(JSONObject obj, String... keys) {
        for (String key : keys) {
            JSONArray arr = obj.optJSONArray(key);
            if (arr != null) return arr;
        }
        return null;
    }

    private static String firstNonEmpty(String... vals) {
        for (String val : vals) {
            if (val != null && !val.trim().isEmpty()) {
                return val;
            }
        }
        return "";
    }
}

package cc.luoluoluo.yanzi.mobile.widget;

import android.appwidget.AppWidgetManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.widget.RemoteViews;
import android.widget.RemoteViewsService;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

import cc.luoluoluo.yanzi.mobile.R;

public final class YanmWidgetService extends RemoteViewsService {
    @Override
    public RemoteViewsFactory onGetViewFactory(Intent intent) {
        return new YanmRemoteViewsFactory(this.getApplicationContext(), intent);
    }

    private static class YanmRemoteViewsFactory implements RemoteViewsFactory {
        private final Context context;
        private final int appWidgetId;
        private final List<YanmComponentItem> items = new ArrayList<>();

        YanmRemoteViewsFactory(Context context, Intent intent) {
            this.context = context;
            this.appWidgetId = intent.getIntExtra(
                    AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID);
        }

        @Override
        public void onCreate() {
            loadData();
        }

        @Override
        public void onDataSetChanged() {
            // 在 notifyAppWidgetViewDataChanged 被触发时在后台工作线程回调
            loadData();
        }

        @Override
        public void onDestroy() {
            items.clear();
        }

        @Override
        public int getCount() {
            return items.size();
        }

        @Override
        public RemoteViews getViewAt(int position) {
            if (position < 0 || position >= items.size()) {
                return null;
            }

            YanmComponentItem item = items.get(position);
            RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm_item);

            views.setTextViewText(R.id.widget_item_title, item.title);
            views.setTextViewText(R.id.widget_item_type, item.type.toUpperCase());
            views.setTextViewText(R.id.widget_item_summary, item.summary);

            // 如果有特定的主题色彩，修改左边边框线的颜色
            if (item.accentHex != null && !item.accentHex.trim().isEmpty()) {
                try {
                    String colorStr = item.accentHex.trim();
                    if (!colorStr.startsWith("#")) {
                        colorStr = "#" + colorStr;
                    }
                    int color = Color.parseColor(colorStr);
                    views.setInt(R.id.widget_item_accent_line, "setBackgroundColor", color);
                } catch (Exception ignored) {}
            } else {
                views.setInt(R.id.widget_item_accent_line, "setBackgroundColor", Color.parseColor("#22D3EE"));
            }

            return views;
        }

        @Override
        public RemoteViews getLoadingView() {
            return null;
        }

        @Override
        public int getViewTypeCount() {
            return 1;
        }

        @Override
        public long getItemId(int position) {
            return position;
        }

        @Override
        public boolean hasStableIds() {
            return true;
        }

        private void loadData() {
            items.clear();
            SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
            String yanmJson = prefs.getString("cacheYanmJson", "");

            if (yanmJson == null || yanmJson.trim().isEmpty() || yanmJson.equals("{}")) {
                return;
            }

            try {
                JSONObject snapshot = new JSONObject(yanmJson);
                JSONArray components = firstArray(snapshot, "components", "Components");

                if (components != null) {
                    // 读取排序索引
                    String sortedJson = prefs.getString("sortedComponentIds", "[]");
                    List<String> sortedIds = new ArrayList<>();
                    try {
                        JSONArray arr = new JSONArray(sortedJson);
                        for (int i = 0; i < arr.length(); i++) {
                            sortedIds.add(arr.getString(i));
                        }
                    } catch (Exception ignored) {}

                    List<JSONObject> sortedList = new ArrayList<>();
                    List<JSONObject> remainingList = new ArrayList<>();

                    for (int i = 0; i < components.length(); i++) {
                        JSONObject comp = components.optJSONObject(i);
                        if (comp != null) {
                            String title = firstNonEmpty(
                                    comp.optString("title"),
                                    comp.optString("Title"),
                                    comp.optString("name"),
                                    comp.optString("Name"),
                                    "组件");
                            String compId = firstNonEmpty(comp.optString("id"), comp.optString("Id"), title);
                            if (sortedIds.contains(compId)) {
                                sortedList.add(comp);
                            } else {
                                remainingList.add(comp);
                            }
                        }
                    }

                    // 排序
                    sortedList.sort((c1, c2) -> {
                        String t1 = firstNonEmpty(c1.optString("title"), c1.optString("Title"), c1.optString("name"), c1.optString("Name"), "组件");
                        String id1 = firstNonEmpty(c1.optString("id"), c1.optString("Id"), t1);
                        String t2 = firstNonEmpty(c2.optString("title"), c2.optString("Title"), c2.optString("name"), c2.optString("Name"), "组件");
                        String id2 = firstNonEmpty(c2.optString("id"), c2.optString("Id"), t2);
                        return Integer.compare(sortedIds.indexOf(id1), sortedIds.indexOf(id2));
                    });

                    List<JSONObject> finalComponents = new ArrayList<>(sortedList);
                    finalComponents.addAll(remainingList);

                    for (int i = 0; i < finalComponents.size(); i++) {
                        JSONObject comp = finalComponents.get(i);
                        String title = firstNonEmpty(
                                comp.optString("title"),
                                comp.optString("Title"),
                                comp.optString("name"),
                                comp.optString("Name"),
                                "组件 " + (i + 1));
                        String type = firstNonEmpty(
                                comp.optString("type"),
                                comp.optString("Type"),
                                comp.optString("kind"),
                                comp.optString("Kind"),
                                "component");
                        String accentHex = firstNonEmpty(
                                comp.optString("accentHex"),
                                comp.optString("accent_hex"),
                                comp.optString("AccentHex"));

                        String summary = summarizeYanmComponent(comp);

                        items.add(new YanmComponentItem(title, type, summary, accentHex));
                    }
                }
            } catch (Exception ignored) {}
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
            
            String html = firstNonEmpty(
                    component.optString("html"),
                    component.optString("Html"),
                    component.optString("markup"),
                    component.optString("Markup"));
            
            if (text.isEmpty()) {
                if (!html.isEmpty()) {
                    text = "[富文本网页组件，可在 App 展开查看]";
                } else {
                    text = component.toString();
                }
            }
            text = text.replaceAll("\\s+", " ").trim();
            return text.length() > 100 ? text.substring(0, 100) + "..." : text;
        }

        private static JSONArray firstArray(JSONObject obj, String... keys) {
            for (String key : keys) {
                JSONArray val = obj.optJSONArray(key);
                if (val != null) {
                    return val;
                }
            }
            return null;
        }

        private static String firstNonEmpty(String... values) {
            for (String val : values) {
                if (val != null && !val.trim().isEmpty()) {
                    return val;
                }
            }
            return "";
        }
    }

    private static class YanmComponentItem {
        final String title;
        final String type;
        final String summary;
        final String accentHex;

        YanmComponentItem(String title, String type, String summary, String accentHex) {
            this.title = title;
            this.type = type;
            this.summary = summary;
            this.accentHex = accentHex;
        }
    }
}

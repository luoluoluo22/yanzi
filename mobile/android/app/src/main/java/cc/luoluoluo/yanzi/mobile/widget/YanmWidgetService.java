package cc.luoluoluo.yanzi.mobile.widget;

import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.view.View;
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
        return new YanmRemoteViewsFactory(this.getApplicationContext());
    }
}

class YanmRemoteViewsFactory implements RemoteViewsService.RemoteViewsFactory {
    private final Context context;
    private final List<JSONObject> componentsList = new ArrayList<>();

    public YanmRemoteViewsFactory(Context context) {
        this.context = context;
    }

    @Override
    public void onCreate() {
        loadData();
    }

    @Override
    public void onDataSetChanged() {
        loadData();
    }

    private void loadData() {
        componentsList.clear();
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

                    componentsList.addAll(sortedList);
                    componentsList.addAll(remainingList);
                }
            }
        } catch (Exception ignored) {}
    }

    @Override
    public void onDestroy() {
        componentsList.clear();
    }

    @Override
    public int getCount() {
        return componentsList.size();
    }

    @Override
    public RemoteViews getViewAt(int position) {
        if (position < 0 || position >= componentsList.size()) {
            return null;
        }

        JSONObject component = componentsList.get(position);
        String title = firstNonEmpty(
                component.optString("title"),
                component.optString("Title"),
                component.optString("name"),
                component.optString("Name"),
                "组件 " + (position + 1));
        String type = firstNonEmpty(
                component.optString("type"),
                component.optString("Type"),
                component.optString("kind"),
                component.optString("Kind"),
                "component");
        String componentId = firstNonEmpty(component.optString("id"), component.optString("Id"), title);

        RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm_item);
        views.setTextViewText(R.id.widget_item_title, title);
        
        // 按照用户需求，快捷入口不需要去渲染内容文本
        views.setViewVisibility(R.id.widget_item_summary, View.GONE);

        // 设置快捷点击的 fillInIntent 传递锚点
        Intent fillInIntent = new Intent();
        fillInIntent.putExtra("target_component_id", componentId);
        views.setOnClickFillInIntent(R.id.widget_yanm_item_root, fillInIntent);

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

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

import cc.luoluoluo.yanzi.mobile.MainActivity;
import cc.luoluoluo.yanzi.mobile.R;

public final class YanmWidgetProvider extends AppWidgetProvider {

    public static final String ACTION_REFRESH_YANM = "cc.luoluoluo.yanzi.mobile.widget.ACTION_REFRESH_YANM";

    @Override
    public void onUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds) {
        SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        String yanmJson = prefs.getString("cacheYanmJson", "");
        
        boolean hasData = false;
        try {
            if (yanmJson != null && !yanmJson.trim().isEmpty() && !yanmJson.equals("{}")) {
                JSONObject yanmObj = new JSONObject(yanmJson);
                JSONArray components = firstArray(yanmObj, "components", "Components");
                if (components != null && components.length() > 0) {
                    hasData = true;
                }
            }
        } catch (Exception ignored) {}

        for (int appWidgetId : appWidgetIds) {
            RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm);

            // 绑定一键刷新 PendingIntent
            Intent refreshIntent = new Intent(context, YanmWidgetProvider.class);
            refreshIntent.setAction(ACTION_REFRESH_YANM);
            PendingIntent refreshPI = PendingIntent.getBroadcast(
                    context, appWidgetId, refreshIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            views.setOnClickPendingIntent(R.id.widget_yanm_refresh, refreshPI);

            // 无条件为 GridView 设置 RemoteViewsService 适配器和 PendingIntent 模板
            Intent serviceIntent = new Intent(context, YanmWidgetService.class);
            serviceIntent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId);
            serviceIntent.setData(Uri.parse(serviceIntent.toUri(Intent.URI_INTENT_SCHEME)));
            views.setRemoteAdapter(R.id.widget_yanm_grid, serviceIntent);

            Intent clickIntent = new Intent(context, MainActivity.class);
            clickIntent.setAction("cc.luoluoluo.yanzi.mobile.yanm");
            PendingIntent clickPI = PendingIntent.getActivity(
                    context, 0, clickIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
            views.setPendingIntentTemplate(R.id.widget_yanm_grid, clickPI);

            if (!hasData) {
                views.setViewVisibility(R.id.widget_yanm_empty_text, View.VISIBLE);
                views.setViewVisibility(R.id.widget_yanm_grid, View.GONE);
            } else {
                views.setViewVisibility(R.id.widget_yanm_empty_text, View.GONE);
                views.setViewVisibility(R.id.widget_yanm_grid, View.VISIBLE);
            }

            appWidgetManager.updateAppWidget(appWidgetId, views);
            appWidgetManager.notifyAppWidgetViewDataChanged(appWidgetId, R.id.widget_yanm_grid);
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

            // 强制通知其绑定的 GridView 刷新数据
            for (int appWidgetId : appWidgetIds) {
                appWidgetManager.notifyAppWidgetViewDataChanged(appWidgetId, R.id.widget_yanm_grid);
            }

            Toast.makeText(context, "燕幕组件列表已刷新。", Toast.LENGTH_SHORT).show();
        }
    }

    private static JSONArray firstArray(JSONObject obj, String... keys) {
        for (String key : keys) {
            JSONArray arr = obj.optJSONArray(key);
            if (arr != null) return arr;
        }
        return null;
    }
}

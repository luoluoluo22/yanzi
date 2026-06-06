package cc.luoluoluo.yanzi.mobile.widget;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.widget.RemoteViews;
import android.widget.Toast;

import cc.luoluoluo.yanzi.mobile.R;

public final class YanmWidgetProvider extends AppWidgetProvider {

    public static final String ACTION_REFRESH_YANM = "cc.luoluoluo.yanzi.mobile.widget.ACTION_REFRESH_YANM";

    @Override
    public void onUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds) {
        SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        String yanmJson = prefs.getString("cacheYanmJson", "");

        boolean hasData = (yanmJson != null && !yanmJson.trim().isEmpty() && !yanmJson.equals("{}"));

        for (int appWidgetId : appWidgetIds) {
            RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm);

            // 绑定一键刷新 PendingIntent
            Intent refreshIntent = new Intent(context, YanmWidgetProvider.class);
            refreshIntent.setAction(ACTION_REFRESH_YANM);
            PendingIntent refreshPI = PendingIntent.getBroadcast(
                    context, appWidgetId, refreshIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            views.setOnClickPendingIntent(R.id.widget_yanm_refresh, refreshPI);

            if (!hasData) {
                views.setViewVisibility(R.id.widget_yanm_empty_text, android.view.View.VISIBLE);
                views.setViewVisibility(R.id.widget_yanm_list, android.view.View.GONE);
            } else {
                views.setViewVisibility(R.id.widget_yanm_empty_text, android.view.View.GONE);
                views.setViewVisibility(R.id.widget_yanm_list, android.view.View.VISIBLE);

                // 绑定 RemoteViewsService 作为 ListView 适配器
                Intent serviceIntent = new Intent(context, YanmWidgetService.class);
                serviceIntent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId);
                serviceIntent.setData(Uri.parse(serviceIntent.toUri(Intent.URI_INTENT_SCHEME)));
                views.setRemoteAdapter(R.id.widget_yanm_list, serviceIntent);
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

            // 通知 ListView 重新加载数据源
            appWidgetManager.notifyAppWidgetViewDataChanged(appWidgetIds, R.id.widget_yanm_list);
            
            // 重新绘制小部件卡片外层状态
            onUpdate(context, appWidgetManager, appWidgetIds);

            Toast.makeText(context, "燕幕组件列表已刷新。", Toast.LENGTH_SHORT).show();
        }
    }
}

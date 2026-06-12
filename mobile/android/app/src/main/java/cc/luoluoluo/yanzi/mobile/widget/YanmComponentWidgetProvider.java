package cc.luoluoluo.yanzi.mobile.widget;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.view.View;
import android.widget.RemoteViews;
import android.widget.Toast;

import org.json.JSONObject;

import cc.luoluoluo.yanzi.mobile.R;
import cc.luoluoluo.yanzi.mobile.YanmComponentEditActivity;

public final class YanmComponentWidgetProvider extends AppWidgetProvider {
    public static final String ACTION_REFRESH = "cc.luoluoluo.yanzi.mobile.widget.ACTION_REFRESH_YANM_COMPONENT";

    @Override
    public void onUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds) {
        for (int appWidgetId : appWidgetIds) {
            updateWidget(context, appWidgetManager, appWidgetId);
        }
    }

    @Override
    public void onDeleted(Context context, int[] appWidgetIds) {
        for (int appWidgetId : appWidgetIds) {
            YanmWidgetData.deleteComponentWidget(context, appWidgetId);
        }
    }

    @Override
    public void onReceive(Context context, Intent intent) {
        super.onReceive(context, intent);
        if (!ACTION_REFRESH.equals(intent.getAction())) {
            return;
        }

        AppWidgetManager manager = AppWidgetManager.getInstance(context);
        int appWidgetId = intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID);
        if (appWidgetId != AppWidgetManager.INVALID_APPWIDGET_ID) {
            updateWidget(context, manager, appWidgetId);
            Toast.makeText(context, "燕幕组件已刷新。", Toast.LENGTH_SHORT).show();
            return;
        }

        int[] ids = manager.getAppWidgetIds(new ComponentName(context, YanmComponentWidgetProvider.class));
        onUpdate(context, manager, ids);
        Toast.makeText(context, "燕幕组件已刷新。", Toast.LENGTH_SHORT).show();
    }

    public static void updateWidget(Context context, AppWidgetManager manager, int appWidgetId) {
        RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_yanm_component);
        String componentId = YanmWidgetData.getWidgetComponentId(context, appWidgetId);
        String stateKey = YanmWidgetData.getWidgetStateKey(context, appWidgetId);
        YanmWidgetData.ComponentInfo component = YanmWidgetData.findComponent(context, componentId);
        if (component != null && (stateKey == null || stateKey.trim().isEmpty())) {
            stateKey = component.stateKey;
        }

        if (component == null) {
            views.setTextViewText(R.id.widget_yanm_component_title, "燕幕组件");
            views.setTextViewText(R.id.widget_yanm_component_key, "未选择组件");
            views.setTextViewText(R.id.widget_yanm_component_content, "长按小部件重新配置，或打开燕子刷新燕幕数据。");
            views.setTextViewText(R.id.widget_yanm_component_hint, "点击打开燕幕");
        } else {
            String value = readValue(context, component.component, stateKey);
            String summary = YanmWidgetData.summarize(value);
            views.setTextViewText(R.id.widget_yanm_component_title, component.title);
            views.setTextViewText(R.id.widget_yanm_component_key, "key: " + stateKey);
            views.setTextViewText(R.id.widget_yanm_component_content, summary.trim().isEmpty() ? "暂无内容，点击编辑。" : summary);
            views.setTextViewText(R.id.widget_yanm_component_hint, "点击编辑");
        }

        Intent refreshIntent = new Intent(context, YanmComponentWidgetProvider.class);
        refreshIntent.setAction(ACTION_REFRESH);
        refreshIntent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId);
        PendingIntent refreshPi = PendingIntent.getBroadcast(
                context,
                appWidgetId,
                refreshIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        views.setOnClickPendingIntent(R.id.widget_yanm_component_refresh, refreshPi);

        Intent editIntent = new Intent(context, YanmComponentEditActivity.class);
        editIntent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId);
        editIntent.putExtra("component_id", componentId == null ? "" : componentId);
        editIntent.putExtra("state_key", stateKey == null ? "" : stateKey);
        PendingIntent editPi = PendingIntent.getActivity(
                context,
                appWidgetId + 40000,
                editIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        views.setOnClickPendingIntent(R.id.widget_yanm_component_content, editPi);
        views.setOnClickPendingIntent(R.id.widget_yanm_component_title, editPi);
        views.setOnClickPendingIntent(R.id.widget_yanm_component_hint, editPi);

        manager.updateAppWidget(appWidgetId, views);
    }

    private static String readValue(Context context, JSONObject component, String stateKey) {
        JSONObject state = YanmWidgetData.readComponentState(context);
        String key = stateKey == null ? "" : stateKey.trim();
        if (!key.isEmpty()) {
            String value = state.optString(key, "");
            if (!value.trim().isEmpty()) {
                return value;
            }
        }

        for (String fallback : new String[] { "note", "content", "text", "value" }) {
            String value = state.optString(fallback, "");
            if (!value.trim().isEmpty()) {
                return value;
            }
        }

        return YanmWidgetData.firstNonEmpty(
                component.optString("note"),
                component.optString("Note"),
                component.optString("content"),
                component.optString("Content"),
                component.optString("text"),
                component.optString("Text"),
                component.optString("description"),
                component.optString("Description"));
    }
}

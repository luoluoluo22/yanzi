package cc.luoluoluo.yanzi.mobile.widget;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.widget.RemoteViews;
import android.widget.Toast;

import androidx.core.app.NotificationCompat;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

import cc.luoluoluo.yanzi.mobile.MainActivity;
import cc.luoluoluo.yanzi.mobile.MobileIconLibrary;
import cc.luoluoluo.yanzi.mobile.R;
import cc.luoluoluo.yanzi.mobile.MainActivity.YanziApiClient;

public final class ExtensionsWidgetProvider extends AppWidgetProvider {

    public static final String ACTION_RUN_EXT = "cc.luoluoluo.yanzi.mobile.widget.ACTION_RUN_EXT";
    public static final String ACTION_REFRESH_EXT = "cc.luoluoluo.yanzi.mobile.widget.ACTION_REFRESH_EXT";
    private static final String CHANNEL_ID = "yanzi_widget_channel";

    @Override
    public void onUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds) {
        // 初始化图标库
        MobileIconLibrary.init(context);

        SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        String extensionsJson = prefs.getString("cacheRemoteExtensionsJson", "[]");
        String widgetOrderJson = prefs.getString("widgetExtensionsOrder", "[]");

        List<RemoteExtensionItem> allExtensions = new ArrayList<>();
        try {
            JSONArray array = new JSONArray(extensionsJson);
            for (int i = 0; i < array.length(); i++) {
                JSONObject obj = array.optJSONObject(i);
                if (obj != null) {
                    allExtensions.add(new RemoteExtensionItem(
                            obj.optString("extensionId"),
                            obj.optString("name"),
                            obj.optString("icon"),
                            obj.optString("accentHex")
                    ));
                }
            }
        } catch (Exception ignored) {}

        List<String> orderedIds = new ArrayList<>();
        try {
            JSONArray orderArray = new JSONArray(widgetOrderJson);
            for (int i = 0; i < orderArray.length(); i++) {
                orderedIds.add(orderArray.optString(i, ""));
            }
        } catch (Exception ignored) {}

        // 创建 4 个槽位
        RemoteExtensionItem[] slots = new RemoteExtensionItem[4];
        java.util.Set<String> assignedIds = new java.util.HashSet<>();

        // 1. 第一步：优先把用户指定的扩展放到对应槽位
        for (int i = 0; i < Math.min(4, orderedIds.size()); i++) {
            String targetId = orderedIds.get(i);
            if (targetId != null && !targetId.trim().isEmpty()) {
                for (RemoteExtensionItem ext : allExtensions) {
                    if (ext.id.equals(targetId)) {
                        slots[i] = ext;
                        assignedIds.add(targetId);
                        break;
                    }
                }
            }
        }

        // 2. 第二步：剩下的空槽位由其它没有被指定的扩展按默认顺序填满
        int extIndex = 0;
        for (int i = 0; i < 4; i++) {
            if (slots[i] == null) {
                while (extIndex < allExtensions.size()) {
                    RemoteExtensionItem candidate = allExtensions.get(extIndex++);
                    if (!assignedIds.contains(candidate.id)) {
                        slots[i] = candidate;
                        assignedIds.add(candidate.id);
                        break;
                    }
                }
            }
        }

        // 整理最终用来展示的列表
        List<RemoteExtensionItem> items = new ArrayList<>();
        for (RemoteExtensionItem slot : slots) {
            if (slot != null) {
                items.add(slot);
            }
        }

        for (int appWidgetId : appWidgetIds) {
            RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget_extensions);

            // 绑定刷新按钮的 PendingIntent
            Intent refreshIntent = new Intent(context, ExtensionsWidgetProvider.class);
            refreshIntent.setAction(ACTION_REFRESH_EXT);
            PendingIntent refreshPI = PendingIntent.getBroadcast(
                    context, 0, refreshIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            views.setOnClickPendingIntent(R.id.widget_refresh, refreshPI);

            if (items.isEmpty()) {
                views.setViewVisibility(R.id.widget_empty_text, android.view.View.VISIBLE);
                views.setViewVisibility(R.id.widget_grid_container, android.view.View.GONE);
            } else {
                views.setViewVisibility(R.id.widget_empty_text, android.view.View.GONE);
                views.setViewVisibility(R.id.widget_grid_container, android.view.View.VISIBLE);

                // 渲染前4个扩展到 2x2 布局
                int[] itemLayouts = {
                        R.id.widget_ext_item_1,
                        R.id.widget_ext_item_2,
                        R.id.widget_ext_item_3,
                        R.id.widget_ext_item_4
                };
                int[] iconLayouts = {
                        R.id.widget_ext_icon_1,
                        R.id.widget_ext_icon_2,
                        R.id.widget_ext_icon_3,
                        R.id.widget_ext_icon_4
                };
                int[] nameLayouts = {
                        R.id.widget_ext_name_1,
                        R.id.widget_ext_name_2,
                        R.id.widget_ext_name_3,
                        R.id.widget_ext_name_4
                };

                for (int i = 0; i < 4; i++) {
                    int itemLayoutId = itemLayouts[i];
                    int iconLayoutId = iconLayouts[i];
                    int nameLayoutId = nameLayouts[i];

                    if (i < items.size()) {
                        RemoteExtensionItem ext = items.get(i);
                        views.setViewVisibility(itemLayoutId, android.view.View.VISIBLE);
                        views.setTextViewText(nameLayoutId, ext.name);

                        // 渲染矢量图标并生成 Bitmap
                        Bitmap iconBmp = renderVectorIconToBitmap(context, ext.icon, ext.accentHex);
                        if (iconBmp != null) {
                            views.setImageViewBitmap(iconLayoutId, iconBmp);
                        } else {
                            views.setImageViewResource(iconLayoutId, android.R.drawable.sym_def_app_icon);
                        }

                        // 绑定点击 PendingIntent
                        Intent runIntent = new Intent(context, ExtensionsWidgetProvider.class);
                        runIntent.setAction(ACTION_RUN_EXT);
                        runIntent.putExtra("ext_id", ext.id);
                        runIntent.putExtra("ext_name", ext.name);

                        // 对每一个 item 赋予唯一的 requestCode 防止 Intent Extra 被覆写
                        PendingIntent runPI = PendingIntent.getBroadcast(
                                context, i + 1, runIntent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
                        
                        // 仅绑定到外层 Item 容器，子控件不设点击，事件自然穿透到底座触发
                        views.setOnClickPendingIntent(itemLayoutId, runPI);
                    } else {
                        // 如果没有足够的扩展，隐藏多余卡片
                        views.setViewVisibility(itemLayoutId, android.view.View.INVISIBLE);
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

        if (ACTION_RUN_EXT.equals(action)) {
            String extId = intent.getStringExtra("ext_id");
            String extName = intent.getStringExtra("ext_name");

            if (extId != null && !extId.trim().isEmpty()) {
                showToast(context, "正在发送扩展执行请求：" + extName);
                runExtensionInBackground(context, extId, extName);
            }
        } else if (ACTION_REFRESH_EXT.equals(action)) {
            // 刷新广播：触发 MainActivity 的数据拉取逻辑（若 App 已运行），或者直接用 AppWidgetManager 刷新小部件本身
            AppWidgetManager appWidgetManager = AppWidgetManager.getInstance(context);
            int[] appWidgetIds = appWidgetManager.getAppWidgetIds(new ComponentName(context, ExtensionsWidgetProvider.class));
            onUpdate(context, appWidgetManager, appWidgetIds);
            showToast(context, "桌面小部件已刷新。");
        }
    }

    private void runExtensionInBackground(final Context context, final String extId, final String extName) {
        new Thread(() -> {
            SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
            String baseUrl = prefs.getString("baseUrl", null);
            String token = prefs.getString("token", null);
            String deviceId = prefs.getString("deviceId", null);

            if (baseUrl == null || token == null || deviceId == null) {
                showToast(context, "执行失败：请先打开 App 登录账号。");
                return;
            }

            // 对 URL 规范化
            int v1Index = baseUrl.indexOf("/v1/");
            if (v1Index >= 0) {
                baseUrl = baseUrl.substring(0, v1Index);
            }
            while (baseUrl.endsWith("/")) {
                baseUrl = baseUrl.substring(0, baseUrl.length() - 1);
            }

            try {
                // 运行设备名称
                String marketName = Build.MANUFACTURER + " " + Build.MODEL;
                String deviceName = marketName.trim().isEmpty() ? "Android Widget" : marketName;

                // 1. 发起请求
                String messageId = MainActivity.YanziApiClient.runExtensionOnDesktop(
                        baseUrl, token, deviceId, deviceName, extId, "");

                showToast(context, "请求已发送，开始检测执行状态...");

                // 2. 轮询查询结果
                long startTime = System.currentTimeMillis();
                long timeout = 20000;
                String statusResult = "timeout";
                String execOutput = "";

                while (System.currentTimeMillis() - startTime < timeout) {
                    try {
                        JSONObject msgDetail = MainActivity.YanziApiClient.fetchMessageDetail(baseUrl, token, messageId);
                        String status = msgDetail.optString("status", "pending");

                        if ("completed".equals(status)) {
                            statusResult = "completed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null) {
                                JSONObject execRes = payloadObj.optJSONObject("executionResult");
                                if (execRes != null) {
                                    execOutput = execRes.optString("output", "");
                                }
                            }
                            break;
                        } else if ("failed".equals(status)) {
                            statusResult = "failed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null) {
                                JSONObject execRes = payloadObj.optJSONObject("executionResult");
                                if (execRes != null) {
                                    execOutput = execRes.optString("output", "");
                                }
                            }
                            break;
                        } else if ("acked".equals(status)) {
                            statusResult = "acked";
                            break;
                        }
                    } catch (Exception ignored) {}

                    try {
                        Thread.sleep(1000);
                    } catch (InterruptedException e) {
                        break;
                    }
                }

                // 3. 发送通知反馈结果
                sendResultNotification(context, extName, statusResult, execOutput);

            } catch (Exception ex) {
                sendResultNotification(context, extName, "failed", ex.getMessage());
            }
        }).start();
    }

    private void sendResultNotification(Context context, String extName, String status, String output) {
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null) return;

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(CHANNEL_ID, "燕子小部件状态反馈", NotificationManager.IMPORTANCE_HIGH);
            manager.createNotificationChannel(channel);
        }

        String title;
        String text;
        if ("completed".equals(status)) {
            title = "扩展执行成功：" + extName;
            text = output.trim().isEmpty() ? "命令在电脑端已顺利执行完毕。" : output;
        } else if ("failed".equals(status)) {
            title = "扩展执行失败：" + extName;
            text = output.trim().isEmpty() ? "执行中返回了错误状态。" : output;
        } else if ("acked".equals(status)) {
            title = "扩展已执行完成：" + extName;
            text = "扩展指令已送达电脑端运行。";
        } else {
            title = "扩展执行超时：" + extName;
            text = "未能在20秒内获取到状态反馈，请检查电脑端是否离线。";
        }

        NotificationCompat.Builder builder = new NotificationCompat.Builder(context, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.stat_sys_warning)
                .setContentTitle(title)
                .setContentText(text)
                .setAutoCancel(true)
                .setStyle(new NotificationCompat.BigTextStyle().bigText(text))
                .setPriority(NotificationCompat.PRIORITY_HIGH);

        manager.notify((int) System.currentTimeMillis(), builder.build());
    }

    private void showToast(final Context context, final String message) {
        new Handler(Looper.getMainLooper()).post(() -> Toast.makeText(context, message, Toast.LENGTH_SHORT).show());
    }

    private Bitmap renderVectorIconToBitmap(Context context, String iconName, String accentHex) {
        try {
            int sizeDp = 32;
            float density = context.getResources().getDisplayMetrics().density;
            int sizePx = Math.round(sizeDp * density);

            Bitmap bitmap = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888);
            Canvas canvas = new Canvas(bitmap);

            // 1. 绘制圆角矩形背景色
            Paint bgPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
            int baseColor = Color.rgb(45, 45, 45); // 暗灰色底槽
            if (accentHex != null && !accentHex.trim().isEmpty()) {
                try {
                    String colorStr = accentHex.trim();
                    if (!colorStr.startsWith("#")) {
                        colorStr = "#" + colorStr;
                    }
                    baseColor = Color.parseColor(colorStr);
                } catch (Exception ignored) {}
            }
            bgPaint.setColor(baseColor);
            float rxry = 6 * density; // 圆角半径 6dp
            canvas.drawRoundRect(0, 0, sizePx, sizePx, rxry, rxry, bgPaint);

            // 2. 绘制 SVG 矢量路径
            Path path = MobileIconLibrary.resolveOrDefault(iconName);
            if (path != null) {
                Paint pathPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
                pathPaint.setColor(Color.WHITE);
                pathPaint.setStyle(Paint.Style.FILL);

                // MDI 图标是以 24x24 作为标准视口设计的。
                // 我们要将图标等比缩放到 Bitmap 视口中间（比如整体 size 32dp，四周各有 6dp padding，绘图区域 20dp）
                float paddingPx = 6 * density;
                float drawAreaPx = sizePx - 2 * paddingPx;

                canvas.save();
                canvas.translate(paddingPx, paddingPx);
                float scale = drawAreaPx / 24.0f;
                canvas.scale(scale, scale);
                canvas.drawPath(path, pathPaint);
                canvas.restore();
            }

            return bitmap;
        } catch (Exception ignored) {
            return null;
        }
    }

    private static class RemoteExtensionItem {
        final String id;
        final String name;
        final String icon;
        final String accentHex;

        RemoteExtensionItem(String id, String name, String icon, String accentHex) {
            this.id = id;
            this.name = name;
            this.icon = icon;
            this.accentHex = accentHex;
        }
    }
}

package cc.luoluoluo.yanzi.mobile;

import android.app.Instrumentation;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.service.notification.StatusBarNotification;

final class ChatNotificationVerification {
    static void run(Instrumentation test) {
        Bundle result = new Bundle();
        Context context = test.getTargetContext();
        try {
            require(context.getPackageName().endsWith(".dev") && android.os.Build.MODEL.toLowerCase().contains("sdk"), "Emulator Dev only");
            MobileNotificationManager.ensureChannels(context);
            NotificationManager manager = context.getSystemService(NotificationManager.class);
            NotificationChannel chat = manager.getNotificationChannel(MobileNotificationManager.CHANNEL_CHAT);
            require(chat.getImportance() == NotificationManager.IMPORTANCE_HIGH && chat.getSound() != null && chat.shouldVibrate(), "Fresh chat defaults");
            android.net.Uri systemSound = android.media.RingtoneManager.getActualDefaultRingtoneUri(context, android.media.RingtoneManager.TYPE_NOTIFICATION);
            if (systemSound != null) require(systemSound.equals(chat.getSound()), "Fresh channel selects configured system ringtone");
            require(manager.getNotificationChannel(MobileNotificationManager.CHANNEL_CONNECTION).getImportance() == NotificationManager.IMPORTANCE_LOW, "Connection remains quiet");
            require(MobileEventNotifier.notifyChatMessage(context, "chat-fixture", "电脑", "[图片]"), "Chat posted");
            require(MobileEventNotifier.notifyMessage(context, "sync-fixture", "同步", "完成"), "Sync posted");
            require(MobileEventNotifier.notifyChatMessage(context, "chat-fixture", "电脑", "[图片]"), "Duplicate updates same notification");
            Thread.sleep(800);
            int chats = 0, syncs = 0;
            for (StatusBarNotification posted : manager.getActiveNotifications()) {
                if ("chat-fixture".equals(posted.getTag())) {
                    chats++;
                    require(MobileNotificationManager.CHANNEL_CHAT.equals(posted.getNotification().getChannelId()), "Chat channel routing");
                    require(android.app.Notification.CATEGORY_MESSAGE.equals(posted.getNotification().category), "Message category");
                    require((posted.getNotification().flags & android.app.Notification.FLAG_ONLY_ALERT_ONCE) != 0, "Retry will not alert twice");
                }
                if ("sync-fixture".equals(posted.getTag())) {
                    syncs++; require(MobileNotificationManager.CHANNEL_SYNC.equals(posted.getNotification().getChannelId()), "General event retains sync channel");
                }
            }
            require(chats == 1 && syncs == 1, "No duplicate notification");
            Intent settings = NotificationSettingsActivity.systemSettingsIntent(context, MobileNotificationManager.CHANNEL_CHAT);
            require(android.provider.Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS.equals(settings.getAction())
                    && context.getPackageName().equals(settings.getStringExtra(android.provider.Settings.EXTRA_APP_PACKAGE))
                    && MobileNotificationManager.CHANNEL_CHAT.equals(settings.getStringExtra(android.provider.Settings.EXTRA_CHANNEL_ID)), "Dev settings cannot target production");
            NotificationChannel lowered = new NotificationChannel(MobileNotificationManager.CHANNEL_CHAT, "聊天消息", NotificationManager.IMPORTANCE_LOW);
            manager.createNotificationChannel(lowered);
            MobileNotificationManager.ensureChannels(context);
            require(manager.getNotificationChannel(MobileNotificationManager.CHANNEL_CHAT).getImportance() == NotificationManager.IMPORTANCE_LOW, "Ensure preserves changed channel importance");
            result.putString("stream", "CHAT_NOTIFICATION=PASSED: actual posting, routing, dedup, settings target, preference preservation");
            test.finish(android.app.Activity.RESULT_OK, result);
        } catch (Throwable error) {
            result.putString("stream", android.util.Log.getStackTraceString(error));
            test.finish(android.app.Activity.RESULT_CANCELED, result);
        } finally {
            context.getSystemService(NotificationManager.class).cancel("chat-fixture", 41002);
            context.getSystemService(NotificationManager.class).cancel("sync-fixture", 41002);
        }
    }
    private static void require(boolean ok, String message) { if (!ok) throw new AssertionError(message); }
}

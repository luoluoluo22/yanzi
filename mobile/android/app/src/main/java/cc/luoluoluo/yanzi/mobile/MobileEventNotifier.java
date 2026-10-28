package cc.luoluoluo.yanzi.mobile;

import android.app.NotificationManager;
import android.content.Context;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import androidx.core.app.NotificationCompat;

/** Shared posting helpers; chat and general events have separate user controls. */
public final class MobileEventNotifier {
    private MobileEventNotifier() {}

    public static void notifySyncEvent(Context context, String title, String message) {
        notifyMessage(context, java.util.UUID.randomUUID().toString(), title, message);
    }

    public static boolean canNotify(Context context) {
        return canNotify(context, MobileNotificationManager.CHANNEL_SYNC);
    }

    public static boolean canNotifyChat(Context context) {
        return canNotify(context, MobileNotificationManager.CHANNEL_CHAT);
    }

    private static boolean canNotify(Context context, String channelId) {
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null || !manager.areNotificationsEnabled()) return false;
        android.app.AppOpsManager operations = (android.app.AppOpsManager) context.getSystemService(Context.APP_OPS_SERVICE);
        if (operations != null) {
            int mode = operations.checkOpNoThrow("android:post_notification",
                    android.os.Process.myUid(), context.getPackageName());
            if (mode != android.app.AppOpsManager.MODE_ALLOWED && mode != android.app.AppOpsManager.MODE_DEFAULT) return false;
        }
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)
                != android.content.pm.PackageManager.PERMISSION_GRANTED) return false;
        android.app.NotificationChannel channel = manager.getNotificationChannel(channelId);
        return channel == null || channel.getImportance() != NotificationManager.IMPORTANCE_NONE;
    }

    public static boolean notifyMessage(Context context, String id, String title, String message) {
        return post(context, id, title, message, false);
    }

    public static boolean notifyChatMessage(Context context, String id, String title, String message) {
        return post(context, id, title, message, true);
    }

    private static boolean post(Context context, String id, String title, String message, boolean chat) {
        MobileNotificationManager.ensureChannels(context);
        String channel = chat ? MobileNotificationManager.CHANNEL_CHAT : MobileNotificationManager.CHANNEL_SYNC;
        if (!canNotify(context, channel)) return false;
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null) return false;
        Intent intent = new Intent(context, MainActivity.class);
        PendingIntent open = PendingIntent.getActivity(context, chat ? 41003 : 41002, intent,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        NotificationCompat.Builder builder = new NotificationCompat.Builder(context, channel)
                .setSmallIcon(android.R.drawable.ic_dialog_email)
                .setContentTitle(title)
                .setContentText(message)
                .setStyle(new NotificationCompat.BigTextStyle().bigText(message))
                .setContentIntent(open)
                .setOnlyAlertOnce(true)
                .setAutoCancel(true);
        if (chat) {
            builder.setCategory(NotificationCompat.CATEGORY_MESSAGE)
                    .setPriority(NotificationCompat.PRIORITY_HIGH)
                    .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
                    .setDefaults(NotificationCompat.DEFAULT_SOUND | NotificationCompat.DEFAULT_VIBRATE);
        }
        manager.notify(id, 41002, builder.build());
        android.util.Log.i("YanziNotification", "messageId=" + id + " channel=" + channel + " posted=true");
        return true;
    }
}

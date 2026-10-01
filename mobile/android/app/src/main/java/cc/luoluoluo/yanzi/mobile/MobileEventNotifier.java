package cc.luoluoluo.yanzi.mobile;

import android.app.NotificationManager;
import android.content.Context;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import androidx.core.app.NotificationCompat;

/**
 * Unified entry for future cloud event notifications.
 */
public final class MobileEventNotifier {
    private MobileEventNotifier() {}

    public static void notifySyncEvent(Context context, String title, String message) {
        notifyMessage(context, java.util.UUID.randomUUID().toString(), title, message);
    }

    public static boolean canNotify(Context context) {
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
        android.app.NotificationChannel channel = manager.getNotificationChannel(MobileNotificationManager.CHANNEL_SYNC);
        return channel == null || channel.getImportance() != NotificationManager.IMPORTANCE_NONE;
    }

    public static boolean notifyMessage(Context context, String id, String title, String message) {
        MobileNotificationManager.ensureChannels(context);
        if (!canNotify(context)) return false;
        NotificationManager manager = (NotificationManager)
                context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null) return false;

        PendingIntent open = PendingIntent.getActivity(context, 0, new Intent(context, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        NotificationCompat.Builder builder = new NotificationCompat.Builder(
                context, MobileNotificationManager.CHANNEL_SYNC)
                .setSmallIcon(android.R.drawable.ic_popup_sync)
                .setContentTitle(title)
                .setContentText(message)
                .setStyle(new NotificationCompat.BigTextStyle().bigText(message))
                .setContentIntent(open)
                .setOnlyAlertOnce(true)
                .setAutoCancel(true);

        manager.notify(id, 41002, builder.build());
        return true;
    }
}

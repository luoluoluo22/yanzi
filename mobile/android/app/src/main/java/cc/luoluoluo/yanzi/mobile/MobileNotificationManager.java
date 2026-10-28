package cc.luoluoluo.yanzi.mobile;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.media.AudioAttributes;
import android.media.RingtoneManager;
import android.os.Build;

public final class MobileNotificationManager {
    public static final String CHANNEL_SYNC = "yanzi_sync";
    public static final String CHANNEL_CHAT = "yanzi_chat";
    public static final String CHANNEL_CONNECTION = "yanzi_connection";
    static final long[] CHAT_VIBRATION = {0, 200, 100, 200};

    private MobileNotificationManager() {}

    public static void ensureChannels(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (manager == null) return;
            NotificationChannel sync = new NotificationChannel(
                    CHANNEL_SYNC, "燕子同步通知", NotificationManager.IMPORTANCE_DEFAULT);
            sync.setDescription("云同步、设备状态和小程序事件通知");
            manager.createNotificationChannel(sync);
            // A separate stable channel avoids changing existing sync preferences.
            // Re-registering it must preserve choices made in system settings.
            NotificationChannel chat = new NotificationChannel(
                    CHANNEL_CHAT, "聊天消息", NotificationManager.IMPORTANCE_HIGH);
            chat.setDescription("电脑与手机之间的文字、图片和文件消息");
            // Resolve the configured ringtone rather than an indirect settings URI:
            // some OEM channel settings treat the latter as an unselected ringtone.
            android.net.Uri sound = RingtoneManager.getActualDefaultRingtoneUri(context, RingtoneManager.TYPE_NOTIFICATION);
            if (sound == null) sound = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
            chat.setSound(sound,
                    new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_NOTIFICATION)
                            .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build());
            chat.setVibrationPattern(CHAT_VIBRATION.clone());
            chat.enableVibration(true);
            chat.setShowBadge(true);
            chat.setLockscreenVisibility(android.app.Notification.VISIBILITY_PRIVATE);
            manager.createNotificationChannel(chat);
            NotificationChannel connection = new NotificationChannel(
                    CHANNEL_CONNECTION, "燕子实时连接", NotificationManager.IMPORTANCE_LOW);
            connection.setDescription("保持手机与电脑的实时消息连接");
            manager.createNotificationChannel(connection);
        }
    }
}

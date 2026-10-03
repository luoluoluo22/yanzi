package cc.luoluoluo.yanzi.mobile;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.os.Build;

public final class MobileNotificationManager {
    public static final String CHANNEL_SYNC = "yanzi_sync";
    public static final String CHANNEL_CONNECTION = "yanzi_connection";

    private MobileNotificationManager() {}

    public static void ensureChannels(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (manager == null) return;
            NotificationChannel sync = new NotificationChannel(
                CHANNEL_SYNC,
                "燕子同步通知",
                NotificationManager.IMPORTANCE_DEFAULT
            );
            sync.setDescription("云同步、设备状态和小程序事件通知");
            manager.createNotificationChannel(sync);
            NotificationChannel connection = new NotificationChannel(
                    CHANNEL_CONNECTION,
                    "燕子实时连接",
                    NotificationManager.IMPORTANCE_LOW);
            connection.setDescription("保持手机与电脑的实时消息连接");
            manager.createNotificationChannel(connection);
        }
    }
}

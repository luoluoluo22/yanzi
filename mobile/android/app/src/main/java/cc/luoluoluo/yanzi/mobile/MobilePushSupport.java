package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import android.util.Log;

public final class MobilePushSupport {
    private static boolean initialized;
    public static synchronized void initialize(Context context) {
        if (initialized) return;
        initialized = true;
        try { Class.forName("cc.luoluoluo.yanzi.mobile.FirebasePushBootstrap").getMethod("initialize", Context.class).invoke(null, context.getApplicationContext()); }
        catch (ClassNotFoundException ignored) { }
        catch (Exception ex) { Log.w("YanziPush", "Push configuration unavailable: " + ex.getClass().getSimpleName()); }
    }
    public static void registerToken(Context context, String provider, String token) {
        if (!("fcm".equals(provider) || "webhook".equals(provider)) || token == null || token.length() > 512) return;
        context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE).edit().putString("pushProvider", provider).putString("pushToken", token).apply();
        DeviceHeartbeatService.startIfLoggedIn(context);
    }
    public static void receive(Context context, String messageId) {
        MobileNotificationManager.ensureChannels(context);
        MobileEventNotifier.notifyMessage(context, messageId == null ? "push-wakeup" : messageId, "燕子新消息", "打开燕子查看新消息");
        DeviceHeartbeatService.startIfLoggedIn(context);
        Log.i("YanziPush", "Push wakeup handled");
    }
}

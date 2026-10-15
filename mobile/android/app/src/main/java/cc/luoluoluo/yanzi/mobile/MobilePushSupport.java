package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import android.util.Log;

public final class MobilePushSupport {
    private static boolean initialized;
    private static MessageWakeLock pendingWake;
    static synchronized void releaseWakeup() {
        if (pendingWake != null) { pendingWake.close(); pendingWake = null; }
    }
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
    public static synchronized void receive(Context context, String messageId) {
        if (context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE).getString("token", "").isEmpty()) return;
        MessageWakeLock wake = MessageWakeLock.acquire(context, "push");
        releaseWakeup();
        pendingWake = wake;
        {
            try {
                context.startForegroundService(new android.content.Intent(context, DeviceHeartbeatService.class).putExtra("messagesReady", true));
                Log.i("YanziPush", "messages-ready forwarded; real notification follows saved message");
            } catch (IllegalStateException | SecurityException failure) {
                releaseWakeup();
                Log.w("YanziPush", "Push service start deferred: " + failure.getClass().getSimpleName());
            }
        }
    }
}

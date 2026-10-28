package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.os.PowerManager;
import android.util.Log;

/** Protect work that has already been triggered; this is not a Doze wakeup source. */
final class MessageWakeLock implements AutoCloseable {
    static final long TIMEOUT_MS = 30000;
    private PowerManager.WakeLock lock;
    private MessageWakeLock() { }
    static MessageWakeLock acquire(Context context, String stage) {
        return acquire(context, stage, TIMEOUT_MS);
    }
    static MessageWakeLock acquire(Context context, String stage, long timeoutMs) {
        if (timeoutMs <= 0 || timeoutMs > TIMEOUT_MS) throw new IllegalArgumentException("Wake lock must be bounded to 30s");
        MessageWakeLock scope = new MessageWakeLock();
        try {
            PowerManager manager = context.getSystemService(PowerManager.class);
            if (manager != null) {
                scope.lock = manager.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Yanzi:message-" + stage);
                scope.lock.setReferenceCounted(false);
                scope.lock.acquire(timeoutMs);
            }
        } catch (RuntimeException failure) {
            Log.w("YanziMessageBridge", "Short wake lock unavailable: " + failure.getClass().getSimpleName());
        }
        return scope;
    }
    synchronized boolean isHeld() { return lock != null && lock.isHeld(); }
    @Override public synchronized void close() {
        if (lock != null) {
            try { if (lock.isHeld()) lock.release(); }
            catch (RuntimeException failure) { Log.w("YanziMessageBridge", "Wake lock release deferred"); }
            lock = null;
        }
    }
}

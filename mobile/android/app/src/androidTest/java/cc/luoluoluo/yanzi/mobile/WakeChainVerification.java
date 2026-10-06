package cc.luoluoluo.yanzi.mobile;

import android.app.Instrumentation;
import android.content.Context;
import android.os.Bundle;
import android.os.SystemClock;

final class WakeChainVerification {
    private static int checks;
    static void require(boolean ok, String label) { if (!ok) throw new AssertionError(label); checks++; }
    static void run(Instrumentation test) {
        Bundle result = new Bundle();
        try {
            Context context = test.getTargetContext();
            require(context.getPackageName().endsWith(".dev") && android.os.Build.MODEL.toLowerCase().contains("sdk"), "Emulator Dev only");
            try (MessageWakeLock lock = MessageWakeLock.acquire(context, "fixture")) {
                require(lock.isHeld(), "Manifest permission permits partial lock");
                lock.close(); require(!lock.isHeld(), "Normal close releases lock");
                lock.close(); require(!lock.isHeld(), "Duplicate close is safe");
            }
            try (MessageWakeLock lock = MessageWakeLock.acquire(context, "timeout-fixture", 100)) {
                SystemClock.sleep(300);
                require(!lock.isHeld(), "System timeout releases a stuck task lock");
            }
            try { MessageWakeLock.acquire(context, "invalid", 30001); throw new AssertionError("Unbounded lock accepted"); }
            catch (IllegalArgumentException expected) { checks++; }
            RealtimeLiveness health = new RealtimeLiveness();
            require(!health.stale(999999), "Unopened socket is not connected");
            health.opened(100);
            require(health.pingDue(100) && !health.pingDue(101), "Application ping cadence bounded");
            require(!health.stale(120099) && health.stale(120100), "Dead connected socket expires");
            health.pong(120100); require(!health.stale(120101), "Pong proves activity");
            health.event(240000); require(!health.stale(240001), "Data event proves activity");
            health.opened(300000); require(health.stale(420000), "Reconnect resets old activity");
            result.putString("result", "WAKE_CHAIN_PARTIAL_LOCK_TIMEOUT_AND_REALTIME_LIVENESS=PASSED; checks=" + checks);
            test.finish(-1, result);
        } catch (Throwable failure) {
            result.putString("error", failure.toString()); test.finish(0, result);
        }
    }
}

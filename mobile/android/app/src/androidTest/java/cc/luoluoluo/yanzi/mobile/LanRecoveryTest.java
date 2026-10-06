package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.app.Instrumentation;
import android.os.Bundle;
import org.json.JSONObject;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;

/** Loopback fault fixture: only the isolated Dev package on an emulator. */
public final class LanRecoveryTest extends Instrumentation {
    private Bundle arguments;
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); this.arguments=arguments; start(); }
    @Override public void onStart() {
        if ("wake-chain".equals(arguments.getString("suite"))) { WakeChainVerification.run(this); return; }
        if ("chat-notification".equals(arguments.getString("suite"))) { ChatNotificationVerification.run(this); return; }
        if ("message-delivery".equals(arguments.getString("suite"))) { MessageDeliveryVerification.run(this); return; }
        if ("task-query-original".equals(arguments.getString("suite"))) { TaskRecoveryVerification.queryOriginal(this, arguments); return; }
        if ("task-recovery-ui".equals(arguments.getString("suite"))) { TaskRecoveryVerification.verifyUi(this, arguments); return; }
        if ("task-recovery-restart".equals(arguments.getString("suite"))) { TaskRecoveryVerification.verifyRestart(this); return; }
        if ("task-recovery".equals(arguments.getString("suite"))) { TaskRecoveryVerification.run(this); return; }
        if ("application-store".equals(arguments.getString("suite"))) { ApplicationStoreVerification.run(this); return; }
        if ("mobile-app-capabilities".equals(arguments.getString("suite"))) { MobileAppCapabilityVerification.run(this); return; }
        if ("phone-workspace".equals(arguments.getString("suite"))) { PhoneWorkspaceVerification.run(this); return; }
        if ("update".equals(arguments.getString("suite"))) { UpdateFeedbackTest.run(this, "true".equals(arguments.getString("probeNetwork"))); return; }
        if ("desktop-execution".equals(arguments.getString("suite"))) { DesktopExecutionVerification.run(this, arguments); return; }
        if ("desktop-navigation".equals(arguments.getString("suite"))) { DesktopNavigationVerification.run(this, arguments); return; }
        Bundle result = new Bundle();
        try {
            try { setUp(); testOutageRetainsAddressAndRecoversWithoutBroadcast(); } finally { tearDown(); }
            try { setUp(); testHttpRejectionAndAccountChangeCannotKeepLanGreen(); } finally { tearDown(); }
            result.putString("stream", "LAN_NATIVE_RECOVERY=PASSED (2 scenarios)");
            finish(android.app.Activity.RESULT_OK, result);
        } catch (Throwable failed) {
            result.putString("stream", android.util.Log.getStackTraceString(failed));
            finish(android.app.Activity.RESULT_CANCELED, result);
        }
    }
    private static void assertTrue(boolean value) { assertTrue("Expected true", value); }
    private static void assertTrue(String label, boolean value) { if (!value) throw new AssertionError(label); }
    private static void assertFalse(boolean value) { assertTrue(!value); }
    private static void assertFalse(String label, boolean value) { assertTrue(label, !value); }
    private static void assertEquals(Object expected, Object actual) { assertEquals("Unexpected result", expected, actual); }
    private static void assertEquals(String label, Object expected, Object actual) {
        if (!java.util.Objects.equals(expected, actual)) throw new AssertionError(label + ": expected=" + expected + ", actual=" + actual);
    }
    private Context context;
    private ServerSocket server;
    private Thread worker;
    private int port;
    private volatile int response = 200;

    private void setUp() throws Exception {
        context = getTargetContext();
        assertTrue(context.getPackageName().endsWith(".dev"));
        assertTrue("Emulator-only destructive fixture", android.os.Build.MODEL.toLowerCase().contains("sdk"));
        context.getSharedPreferences("yanzi-mobile", 0).edit().clear().putString("deviceId", "lan-fixture").commit();
        context.getSharedPreferences("YanziPrefs", 0).edit().clear().commit();
        LanDiscoveryManager.clearLanBaseUrl(context);
        assertFalse("Fixture requires emulator Wi-Fi", MobileNetworkRouting.lanNetworkKey().isEmpty());
        server = new ServerSocket(0, 10, java.net.InetAddress.getByName("127.0.0.1")); port = server.getLocalPort();
        startWorker();
        JSONObject pair = new JSONObject().put("ownerAccount", "local").put("mode", "local")
                .put("peerPlatform", "desktop").put("expiresAt", "2099-01-01T00:00:00Z");
        context.getSharedPreferences("YanziPrefs", 0).edit().putString("securePair.fixture-desktop", pair.toString())
                .putString("lanDeviceId", "fixture-desktop").putString("lanBaseUrl", "http://127.0.0.1:" + port).commit();
    }
    private void startWorker() {
        ServerSocket listening = server;
        worker = new Thread(() -> {
            while (!listening.isClosed()) {
                try (Socket client = listening.accept()) {
                    client.setSoTimeout(3000);
                    java.io.BufferedReader input = new java.io.BufferedReader(new java.io.InputStreamReader(client.getInputStream()));
                    String line; while ((line = input.readLine()) != null && !line.isEmpty()) { }
                    client.getOutputStream().write(("HTTP/1.1 " + response + " Fixture\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}").getBytes(StandardCharsets.US_ASCII));
                } catch (Exception stopped) { if (!listening.isClosed()) throw new RuntimeException(stopped); }
            }
        });
        worker.start();
    }
    private void closeServer() throws Exception { server.close(); worker.join(4000); }
    private void due() throws Exception {
        java.lang.reflect.Field field = LanDiscoveryManager.class.getDeclaredField("health"); field.setAccessible(true);
        LanConnectionHealth health = (LanConnectionHealth)field.get(null);
        java.lang.reflect.Field next = LanConnectionHealth.class.getDeclaredField("nextProbe"); next.setAccessible(true); next.setLong(health, 0);
    }
    public void testOutageRetainsAddressAndRecoversWithoutBroadcast() throws Exception {
        assertEquals(LanConnectionHealth.Status.AVAILABLE, LanDiscoveryManager.checkHealth(context));
        closeServer(); due();
        assertEquals(LanConnectionHealth.Status.RECONNECTING, LanDiscoveryManager.checkHealth(context));
        due();
        assertEquals(LanConnectionHealth.Status.UNAVAILABLE, LanDiscoveryManager.checkHealth(context));
        assertEquals("http://127.0.0.1:" + port, context.getSharedPreferences("YanziPrefs", 0).getString("lanBaseUrl", ""));
        java.lang.reflect.Field backoff = LanDiscoveryManager.class.getDeclaredField("discoverySuspendedUntil"); backoff.setAccessible(true);
        backoff.setLong(null, System.currentTimeMillis() + 300000);
        server = new ServerSocket(port, 10, java.net.InetAddress.getByName("127.0.0.1")); startWorker(); due();
        assertEquals("Known address recovers even during broadcast suspension", LanConnectionHealth.Status.AVAILABLE, LanDiscoveryManager.checkHealth(context));
        assertFalse(MainActivity.YanziApiClient.sLanFailedThisSession);
    }
    public void testHttpRejectionAndAccountChangeCannotKeepLanGreen() throws Exception {
        assertEquals(LanConnectionHealth.Status.AVAILABLE, LanDiscoveryManager.checkHealth(context));
        response = 403; due();
        assertEquals(LanConnectionHealth.Status.RECONNECTING, LanDiscoveryManager.checkHealth(context));
        due(); assertEquals(LanConnectionHealth.Status.UNAVAILABLE, LanDiscoveryManager.checkHealth(context));
        response = 200;
        context.getSharedPreferences("yanzi-mobile", 0).edit().putString("token", "changed-invalid-session").commit(); due();
        assertEquals(LanConnectionHealth.Status.UNAVAILABLE, LanDiscoveryManager.checkHealth(context));
    }
    private void tearDown() throws Exception {
        if (server != null && !server.isClosed()) closeServer();
        context.getSharedPreferences("yanzi-mobile", 0).edit().clear().commit();
        context.getSharedPreferences("YanziPrefs", 0).edit().clear().commit();
        LanDiscoveryManager.clearLanBaseUrl(context);
    }
}

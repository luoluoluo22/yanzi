package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;

public class LanDiscoveryManager {
    private static final String TAG = "LanDiscoveryManager";
    private static final int DISCOVERY_PORT = 42980;
    private static final String DISCOVER_REQUEST = "YANZI_DISCOVER_REQUEST";
    private static final int TIMEOUT_MS = 2000;
    private static final long DISCOVERY_COOL_DOWN_MS = 30000; // 30秒冷却时间
    private static final int MAX_CONSECUTIVE_DISCOVERY_FAILURES = 3;
    private static final long DISCOVERY_SUSPEND_MS = 5 * 60 * 1000; // 连续失败后暂停 5 分钟
    private static final long SUPPRESSED_LOG_COOL_DOWN_MS = 60000;

    private static volatile long lastDiscoveryFailedTime = 0;
    private static volatile long discoverySuspendedUntil = 0;
    private static volatile long lastSuppressedLogTime = 0;
    private static volatile int consecutiveDiscoveryFailures = 0;

    public static volatile String cachedLanBaseUrl = null;
    public static volatile String cachedLanApiToken = null;
    private static final LanConnectionHealth health = new LanConnectionHealth();
    private static String healthScope = "";
    private static long lastBroadcastAttempt = -1;
    private static LanConnectionHealth.Status loggedStatus;

    private static String rememberedAddress(Context context) {
        if (cachedLanBaseUrl == null) {
            SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
            cachedLanBaseUrl = prefs.getString("lanBaseUrl", null);
            cachedLanApiToken = prefs.getString("lanApiToken", null);
        }
        return cachedLanBaseUrl;
    }

    static synchronized void noteSuccess() {
        health.success(android.os.SystemClock.elapsedRealtime());
        MainActivity.YanziApiClient.sLanFailedThisSession = false;
    }

    static synchronized void noteTransportFailure(Exception error) {
        if (!CloudRequestRetry.retryable(error)) return;
        health.transportFailure(android.os.SystemClock.elapsedRealtime());
        MainActivity.YanziApiClient.sLanFailedThisSession = true;
    }

    static synchronized LanConnectionHealth.Status checkHealth(Context context) {
        MobileSessionStore.Snapshot session = MobileSessionStore.snapshot(context);
        String network = MobileNetworkRouting.lanNetworkKey();
        String scope = session.baseUrl + "\n" + session.token + "\n" + session.deviceId
                + "\n" + getLanDeviceId(context) + "\n" + network;
        if (!scope.equals(healthScope)) {
            healthScope = scope; health.reset(); resetDiscoveryBackoff(); lastBroadcastAttempt = -1;
            MainActivity.YanziApiClient.sLanFailedThisSession = true;
        }
        if (network.isEmpty()) return reportStatus(context, LanConnectionHealth.Status.UNAVAILABLE);
        long now = android.os.SystemClock.elapsedRealtime();
        String candidate = rememberedAddress(context);
        if (health.beginProbe(now)) {
            if (candidate != null) {
                java.net.HttpURLConnection probe = null;
                try {
                    JSONObject pair = SecureLanConnection.load(context, getLanDeviceId(context));
                    if (!LanConnectionHealth.isDesktopPeer(pair.optString("mode"), pair.optString("peerPlatform")))
                        throw new IllegalStateException("LAN peer is not a desktop");
                    probe = MobileNetworkRouting.openLanConnection(new java.net.URL(candidate
                            + "/v1/me/devices/protocol?notificationPort="
                            + (BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981)));
                    probe.setConnectTimeout(2000); probe.setReadTimeout(2500);
                    int status = probe.getResponseCode();
                    session.requireCurrent();
                    if (!network.equals(MobileNetworkRouting.lanNetworkKey())) throw new CloudRequestRetry.SessionChanged();
                    if (status != 200) throw new IllegalStateException("LAN probe HTTP " + status);
                    noteSuccess();
                } catch (CloudRequestRetry.SessionChanged changed) {
                    health.reset(); return reportStatus(context, LanConnectionHealth.Status.UNAVAILABLE);
                } catch (Exception failed) {
                    health.probeFailure(android.os.SystemClock.elapsedRealtime());
                    MainActivity.YanziApiClient.sLanFailedThisSession = true;
                    Log.w(TAG, "LAN health probe failed; retaining address: " + failed.getClass().getSimpleName());
                    MobileDiagnostics.append(context, "局域网探测失败，保留地址重试：" + failed.getClass().getSimpleName());
                } finally { if (probe != null) probe.disconnect(); }
            }
            if (health.shouldRediscover()) discoverSync(context);
        }
        return reportStatus(context, health.status(android.os.SystemClock.elapsedRealtime()));
    }

    private static LanConnectionHealth.Status reportStatus(Context context, LanConnectionHealth.Status status) {
        if (status != loggedStatus) {
            loggedStatus = status;
            MobileDiagnostics.append(context, "局域网连接状态：" + status.name());
            Log.i(TAG, "LAN health state=" + status.name());
        }
        return status;
    }

    public static void discover(Context context) {
        new Thread(() -> discoverSync(context)).start();
    }

    public static String discoverSync(Context context) {
        return discoverSync(context, false);
    }

    public static void resetDiscoveryBackoff() {
        lastDiscoveryFailedTime = 0;
        discoverySuspendedUntil = 0;
        lastSuppressedLogTime = 0;
        consecutiveDiscoveryFailures = 0;
    }

    public static String discoverNow(Context context) {
        return discoverSync(context, true);
    }

    private static synchronized String discoverSync(Context context, boolean force) {
        long elapsed = android.os.SystemClock.elapsedRealtime();
        String known = rememberedAddress(context);
        if (!force && known != null && health.status(elapsed) == LanConnectionHealth.Status.AVAILABLE) return known;
        if (!force && lastBroadcastAttempt >= 0 && elapsed - lastBroadcastAttempt < DISCOVERY_COOL_DOWN_MS) return null;
        long now = System.currentTimeMillis();
        if (!force && now < discoverySuspendedUntil) {
            long remainingSeconds = Math.max(1, (discoverySuspendedUntil - now + 999) / 1000);
            Log.d(TAG, "Discovery is suspended, skip broadcast");
            if (now - lastSuppressedLogTime > SUPPRESSED_LOG_COOL_DOWN_MS) {
                lastSuppressedLogTime = now;
                MobileDiagnostics.append(context, "局域网直连发现已暂停，剩余约 " + remainingSeconds + " 秒，期间使用云端连接。");
            }
            return null;
        }
        if (!force && now - lastDiscoveryFailedTime < DISCOVERY_COOL_DOWN_MS) {
            Log.d(TAG, "Discovery is cooling down, skip broadcast");
            return null;
        }
        DatagramSocket socket = null;
        MobileSessionStore.Snapshot session = MobileSessionStore.snapshot(context);
        lastBroadcastAttempt = elapsed;
        try {
            socket = new DatagramSocket();
            MobileNetworkRouting.bindLanSocket(socket);
            socket.setBroadcast(true);
            socket.setSoTimeout(TIMEOUT_MS);

            byte[] sendData = (DISCOVER_REQUEST + (BuildConfig.APPLICATION_ID.endsWith(".dev") ? ":42982" : ":42981")).getBytes(java.nio.charset.StandardCharsets.UTF_8);
            DatagramPacket sendPacket = new DatagramPacket(
                    sendData,
                    sendData.length,
                    InetAddress.getByName("255.255.255.255"),
                    DISCOVERY_PORT
            );
            socket.send(sendPacket);
            Log.d(TAG, "Sent discovery broadcast");

            byte[] recvBuf = new byte[1024];
            DatagramPacket receivePacket = new DatagramPacket(recvBuf, recvBuf.length);
            long discoveryDeadline = System.currentTimeMillis() + TIMEOUT_MS;
            while (true) {
            if (System.currentTimeMillis() >= discoveryDeadline) throw new java.net.SocketTimeoutException("Discovery deadline exceeded");
            receivePacket.setLength(recvBuf.length);
            socket.setSoTimeout((int)Math.max(1, discoveryDeadline - System.currentTimeMillis()));
            socket.receive(receivePacket);

            String response = new String(receivePacket.getData(), 0, receivePacket.getLength());
            Log.d(TAG, "Received discovery response");

            JSONObject json;
            try { json = new JSONObject(response); }
            catch (org.json.JSONException malformed) { continue; }
            String ip = json.optString("ip");
            int port = json.optInt("port");
            String token = "";
            String deviceId = json.optString("deviceId");

            try {
                if (!SecureLanConnection.PROTOCOL.equals(json.optString("secureProtocol"))) continue;
                JSONObject pair = SecureLanConnection.load(context, deviceId);
                if (!LanConnectionHealth.isDesktopPeer(pair.optString("mode"), pair.optString("peerPlatform"))) continue;
            } catch (Exception unpaired) {
                Log.d(TAG, "Discovery response rejected peer=" + deviceId + ": " + unpaired.getClass().getSimpleName() + ": " + unpaired.getMessage());
                continue;
            }
            if (!ip.isEmpty() && port > 0 && ip.equals(receivePacket.getAddress().getHostAddress())) {
                String candidate = "http://" + ip + ":" + port;
                java.net.HttpURLConnection probe = new SecureLanConnection(new java.net.URL(candidate + "/v1/me/devices/protocol?notificationPort=" + (BuildConfig.APPLICATION_ID.endsWith(".dev") ? 42982 : 42981)), SecureLanConnection.load(context, deviceId));
                try { probe.setConnectTimeout(1500); probe.setReadTimeout(2500); if (probe.getResponseCode() != 200) continue; }
                catch (Exception failedPeer) {
                    Log.d(TAG, "Authenticated LAN probe rejected peer=" + deviceId + " at " + candidate + ": " + failedPeer.getClass().getSimpleName() + ": " + failedPeer.getMessage());
                    continue;
                }
                finally { probe.disconnect(); }
                session.requireCurrent();
                cachedLanBaseUrl = candidate;
                cachedLanApiToken = token;
                noteSuccess();
                consecutiveDiscoveryFailures = 0;
                discoverySuspendedUntil = 0;
                lastDiscoveryFailedTime = 0;
                SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
                prefs.edit()
                     .putString("lanBaseUrl", cachedLanBaseUrl)
                     .putString("lanApiToken", cachedLanApiToken)
                     .putString("lanDeviceId", deviceId)
                     .apply();
                Log.i(TAG, "Saved LAN Base URL: " + cachedLanBaseUrl);
                MobileDiagnostics.append(context, "局域网直连就绪: " + ip);
                return cachedLanBaseUrl;
            }
            }

        } catch (CloudRequestRetry.SessionChanged changed) {
            health.reset();
        } catch (Exception e) {
            lastDiscoveryFailedTime = System.currentTimeMillis();
            consecutiveDiscoveryFailures++;
            if (consecutiveDiscoveryFailures >= MAX_CONSECUTIVE_DISCOVERY_FAILURES) {
                discoverySuspendedUntil = lastDiscoveryFailedTime + DISCOVERY_SUSPEND_MS;
            }
            Log.e(TAG, "Discovery failed: " + e.getMessage());
            if (discoverySuspendedUntil > lastDiscoveryFailedTime) {
                MobileDiagnostics.append(context, "局域网直连发现连续失败 " + consecutiveDiscoveryFailures + " 次，暂停 5 分钟，期间使用云端连接。最后错误: " + e.getMessage());
            } else {
                MobileDiagnostics.append(context, "局域网直连发现失败(" + consecutiveDiscoveryFailures + "/" + MAX_CONSECUTIVE_DISCOVERY_FAILURES + "): " + e.getMessage());
            }
        } finally {
            if (socket != null && !socket.isClosed()) {
                socket.close();
            }
        }
        return null;
    }

    public static String getLanBaseUrl(Context context) {
        rememberedAddress(context);
        if (cachedLanBaseUrl == null) {
            return discoverSync(context);
        }
        return cachedLanBaseUrl;
    }

    public static String getLanApiToken(Context context) {
        if (cachedLanApiToken != null) return cachedLanApiToken;
        SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
        cachedLanApiToken = prefs.getString("lanApiToken", null);
        return cachedLanApiToken;
    }

    public static String getLanDeviceId(Context context) {
        return context == null ? "" : context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE).getString("lanDeviceId", "");
    }

    public static synchronized void clearLanBaseUrl(Context context) {
        health.reset(); healthScope = ""; loggedStatus = null;
        MainActivity.YanziApiClient.sLanFailedThisSession = true;
        cachedLanBaseUrl = null;
        cachedLanApiToken = null;
        SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
        prefs.edit().remove("lanBaseUrl").remove("lanApiToken").apply();
    }
}

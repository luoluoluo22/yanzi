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

    public static void discover(Context context) {
        new Thread(() -> discoverSync(context)).start();
    }

    public static String discoverSync(Context context) {
        return discoverSync(context, false);
    }

    public static String discoverNow(Context context) {
        return discoverSync(context, true);
    }

    private static synchronized String discoverSync(Context context, boolean force) {
        long now = System.currentTimeMillis();
        if (now < discoverySuspendedUntil) {
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
        try {
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            socket.setSoTimeout(TIMEOUT_MS);

            byte[] sendData = DISCOVER_REQUEST.getBytes();
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
            socket.receive(receivePacket);

            String response = new String(receivePacket.getData(), 0, receivePacket.getLength());
            Log.d(TAG, "Received discovery response: " + response);

            JSONObject json = new JSONObject(response);
            String ip = json.optString("ip");
            int port = json.optInt("port");
            String token = json.optString("token");

            if (!ip.isEmpty() && port > 0) {
                cachedLanBaseUrl = "http://" + ip + ":" + port;
                cachedLanApiToken = token;
                consecutiveDiscoveryFailures = 0;
                discoverySuspendedUntil = 0;
                lastDiscoveryFailedTime = 0;
                SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
                prefs.edit()
                     .putString("lanBaseUrl", cachedLanBaseUrl)
                     .putString("lanApiToken", cachedLanApiToken)
                     .apply();
                Log.i(TAG, "Saved LAN Base URL: " + cachedLanBaseUrl);
                MobileDiagnostics.append(context, "局域网直连就绪: " + ip);
                return cachedLanBaseUrl;
            }

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
        if (cachedLanBaseUrl != null) return cachedLanBaseUrl;
        SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
        cachedLanBaseUrl = prefs.getString("lanBaseUrl", null);
        cachedLanApiToken = prefs.getString("lanApiToken", null);
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
    
    public static void clearLanBaseUrl(Context context) {
        cachedLanBaseUrl = null;
        cachedLanApiToken = null;
        SharedPreferences prefs = context.getSharedPreferences("YanziPrefs", Context.MODE_PRIVATE);
        prefs.edit().remove("lanBaseUrl").remove("lanApiToken").apply();
    }
}

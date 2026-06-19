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

    public static volatile String cachedLanBaseUrl = null;
    public static volatile String cachedLanApiToken = null;

    public static void discover(Context context) {
        new Thread(() -> discoverSync(context)).start();
    }

    public static String discoverSync(Context context) {
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
            Log.e(TAG, "Discovery failed: " + e.getMessage());
            MobileDiagnostics.append(context, "局域网直连发现失败: " + e.getMessage());
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

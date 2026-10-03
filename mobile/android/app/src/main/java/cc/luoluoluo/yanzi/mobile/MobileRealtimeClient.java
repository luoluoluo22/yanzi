package cc.luoluoluo.yanzi.mobile;
import android.net.Network;
import okhttp3.*;
import org.json.JSONObject;
import java.util.concurrent.TimeUnit;

final class MobileRealtimeClient {
    interface Listener { void connected(); void event(JSONObject event); void disconnected(); }
    private OkHttpClient client;
    private WebSocket socket;
    void connect(String base, String token, String device, Listener listener) throws Exception {
        close();
        OkHttpClient.Builder builder = new OkHttpClient.Builder().pingInterval(60, TimeUnit.SECONDS)
                .connectTimeout(10, TimeUnit.SECONDS).readTimeout(0, TimeUnit.SECONDS);
        Network direct = MobileNetworkRouting.findPreferredDirectNetwork();
        if (direct != null) builder.socketFactory(direct.getSocketFactory()).dns(host -> java.util.Arrays.asList(direct.getAllByName(host)));
        client = builder.build();
        Request request = new Request.Builder().url(base + "/v1/me/mobile/messages/ws?deviceId=" + java.net.URLEncoder.encode(device, "UTF-8"))
                .header("Authorization", "Bearer " + token).header("User-Agent", "YanziClient-Mobile/" + BuildConfig.VERSION_NAME)
                .header("X-Yanzi-Client", "mobile").build();
        socket = client.newWebSocket(request, new WebSocketListener() {
            @Override public void onOpen(WebSocket webSocket, Response response) { listener.connected(); }
            @Override public void onMessage(WebSocket webSocket, String text) {
                if (text.equals("pong")) return;
                try { if (text.length() > 2 * 1024 * 1024) throw new IllegalArgumentException(); listener.event(new JSONObject(text)); }
                catch (Exception ex) { webSocket.close(1008, "Invalid event"); listener.disconnected(); }
            }
            @Override public void onFailure(WebSocket webSocket, Throwable failure, Response response) { listener.disconnected(); }
            @Override public void onClosed(WebSocket webSocket, int code, String reason) { listener.disconnected(); }
            @Override public void onClosing(WebSocket webSocket, int code, String reason) { webSocket.close(code, reason); }
        });
    }
    void close() {
        if (socket != null) { socket.cancel(); socket = null; }
        if (client != null) { client.dispatcher().executorService().shutdown(); client.connectionPool().evictAll(); client = null; }
    }
}

package cc.luoluoluo.yanzi.mobile;
import android.net.Network;
import okhttp3.*;
import org.json.JSONObject;
import java.util.concurrent.TimeUnit;

final class MobileRealtimeClient {
    interface Listener { void connected(); void event(JSONObject event); void disconnected(); }
    private OkHttpClient client;
    private WebSocket socket;
    private final RealtimeLiveness liveness = new RealtimeLiveness();
    private volatile int generation;
    boolean stale(long now) { return liveness.stale(now); }
    void ping(long now) { if (socket != null && liveness.pingDue(now)) socket.send("ping"); }
    void connect(String base, String token, String device, Listener listener) throws Exception {
        close();
        final int stamp = ++generation;
        OkHttpClient.Builder builder = new OkHttpClient.Builder().pingInterval(60, TimeUnit.SECONDS)
                .connectTimeout(10, TimeUnit.SECONDS).readTimeout(0, TimeUnit.SECONDS);
        Network direct = MobileNetworkRouting.findPreferredDirectNetwork();
        if (direct != null) builder.socketFactory(direct.getSocketFactory()).dns(host -> java.util.Arrays.asList(direct.getAllByName(host)));
        client = builder.build();
        Request request = new Request.Builder().url(base + "/v1/me/mobile/messages/ws?deviceId=" + java.net.URLEncoder.encode(device, "UTF-8"))
                .header("Authorization", "Bearer " + token).header("User-Agent", "YanziClient-Mobile/" + BuildConfig.VERSION_NAME)
                .header("X-Yanzi-Client", "mobile").build();
        socket = client.newWebSocket(request, new WebSocketListener() {
            @Override public void onOpen(WebSocket webSocket, Response response) {
                if (stamp != generation) return;
                liveness.opened(android.os.SystemClock.elapsedRealtime()); listener.connected();
            }
            @Override public void onMessage(WebSocket webSocket, String text) {
                if (stamp != generation) return;
                if (text.equals("pong")) { liveness.pong(android.os.SystemClock.elapsedRealtime()); return; }
                liveness.event(android.os.SystemClock.elapsedRealtime());
                try { if (text.length() > 2 * 1024 * 1024) throw new IllegalArgumentException(); listener.event(new JSONObject(text)); }
                catch (Exception ex) { webSocket.close(1008, "Invalid event"); listener.disconnected(); }
            }
            @Override public void onFailure(WebSocket webSocket, Throwable failure, Response response) { if (stamp == generation) listener.disconnected(); }
            @Override public void onClosed(WebSocket webSocket, int code, String reason) { if (stamp == generation) listener.disconnected(); }
            @Override public void onClosing(WebSocket webSocket, int code, String reason) { webSocket.close(code, reason); }
        });
    }
    void close() {
        generation++;
        if (socket != null) { socket.cancel(); socket = null; }
        if (client != null) { client.dispatcher().executorService().shutdown(); client.connectionPool().evictAll(); client = null; }
    }
}

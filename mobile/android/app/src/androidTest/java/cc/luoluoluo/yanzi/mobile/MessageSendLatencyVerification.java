package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.os.Bundle;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;

/** Real HTTP and durable outbox fixtures; never uses a real account or phone. */
final class MessageSendLatencyVerification {
    private static void require(boolean value, String reason) { if (!value) throw new AssertionError(reason); }
    static void run(Instrumentation test) {
        Bundle result = new Bundle();
        Context context = test.getTargetContext();
        try {
            require(context.getPackageName().endsWith(".dev") && android.os.Build.MODEL.toLowerCase().contains("sdk"), "Isolated emulator required");
            MobileApplicationContext.initialize(context);
            context.getSharedPreferences("yanzi-mobile", 0).edit().clear().commit();
            String token = "fixture." + android.util.Base64.encodeToString("{\"sub\":\"send-latency-fixture\"}".getBytes(StandardCharsets.UTF_8), 11) + ".fixture";
            try (Fixture lan = new Fixture(); Fixture cloud = new Fixture()) {
                JSONObject pair = new JSONObject().put("ownerAccount", "local").put("mode", "local")
                        .put("peerPlatform", "desktop").put("expiresAt", "2099-01-01T00:00:00Z");
                context.getSharedPreferences("YanziPrefs", 0).edit().clear().putString("securePair.fixture-desktop", pair.toString())
                        .putString("lanDeviceId", "fixture-desktop").putString("lanBaseUrl", lan.base()).commit();
                JSONObject chat = message("text");
                long started = android.os.SystemClock.elapsedRealtime();
                JSONObject delivered = MobileDeviceMessageSender.send(cloud.base(), token, chat);
                require("lan".equals(delivered.optString("_transport")), "LAN delivery returned");
                require(android.os.SystemClock.elapsedRealtime() - started < 2000, "LAN result does not wait for cloud");
                require(cloud.posts == 0, "No foreground cloud submission after LAN success");
                File pending = record(context, chat.getString("clientMessageId"));
                require(pending != null, "LAN success retains durable cloud submission");
                MobileMessageOutbox.replay(cloud.base(), token);
                require(cloud.posts == 1 && !pending.exists(), "Cloud acceptance removes pending record");
                require(lan.lastId.equals(cloud.lastId), "LAN and cloud share the deduplication ID");

                lan.status = 503;
                JSONObject fallback = message("text");
                require(!"lan".equals(MobileDeviceMessageSender.send(cloud.base(), token, fallback).optString("_transport")), "Rejected LAN falls back to cloud");
                require(record(context, fallback.getString("clientMessageId")) == null, "Cloud success completes outbox");

                cloud.status = 503;
                JSONObject outage = message("text");
                try { MobileDeviceMessageSender.send(cloud.base(), token, outage); throw new AssertionError("Failure must be visible"); }
                catch (MobileMessageClient.HttpFailure expected) { require(expected.status == 503, "Original HTTP error preserved"); }
                File retry = record(context, outage.getString("clientMessageId"));
                require(retry != null, "Both routes failing preserves durable message");
                context.getSharedPreferences("yanzi-mobile", 0).edit().putString("baseUrl", cloud.base()).putString("token", token).commit();
                MobileSessionStore.Snapshot fence = MobileSessionStore.snapshot(context);
                context.getSharedPreferences("yanzi-mobile", 0).edit().putString("token", "changed-session").commit();
                try { MobileMessageOutbox.replay(cloud.base(), token, fence); throw new AssertionError("Changed session cannot replay"); }
                catch (CloudRequestRetry.SessionChanged expected) { require(retry.exists(), "Account change retains original account queue"); }
                context.getSharedPreferences("yanzi-mobile", 0).edit().clear().commit();
                cloud.status = 200;
                MobileMessageOutbox.replay(cloud.base(), token);
                require(!retry.exists() && outage.getString("clientMessageId").equals(cloud.lastId), "Retry preserves identity and completes");

                lan.status = 200; lan.success = false;
                int cloudBefore = cloud.posts;
                JSONObject command = message("run-shell");
                JSONObject failedCommand = MobileDeviceMessageSender.send(cloud.base(), token, command);
                require("lan".equals(failedCommand.optString("_transport")) && !failedCommand.optBoolean("success"), "Command failure is a terminal LAN result");
                require(cloud.posts == cloudBefore && record(context, command.getString("clientMessageId")) == null, "Command is not executed again through cloud");
            }
            result.putString("stream", "MESSAGE_SEND_LATENCY=PASSED (LAN fast return, durable retention, stable ID replay, cloud fallback, outage recovery, session fence, command no replay)");
            test.finish(Activity.RESULT_OK, result);
        } catch (Throwable error) {
            result.putString("stream", android.util.Log.getStackTraceString(error));
            test.finish(Activity.RESULT_CANCELED, result);
        } finally {
            context.getSharedPreferences("yanzi-mobile", 0).edit().clear().commit();
            context.getSharedPreferences("YanziPrefs", 0).edit().clear().commit();
            LanDiscoveryManager.clearLanBaseUrl(context);
        }
    }
    private static JSONObject message(String kind) throws Exception {
        return new JSONObject().put("clientMessageId", java.util.UUID.randomUUID().toString())
                .put("sourceDeviceId", "fixture-phone").put("kind", kind).put("text", "send fixture");
    }
    private static File record(Context context, String id) {
        File[] accounts = new File(context.getFilesDir(), "device-outbox").listFiles();
        if (accounts != null) for (File account : accounts) {
            File file = new File(account, id + ".json"); if (file.exists()) return file;
        }
        return null;
    }
    private static final class Fixture implements AutoCloseable {
        final ServerSocket server = new ServerSocket(0, 10, InetAddress.getByName("127.0.0.1"));
        final Thread worker;
        volatile int status = 200, posts;
        volatile boolean success = true;
        volatile String lastId = "";
        Fixture() throws IOException {
            worker = new Thread(() -> {
                while (!server.isClosed()) try (Socket socket = server.accept()) {
                    socket.setSoTimeout(3000);
                    BufferedReader input = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8));
                    String first = input.readLine(), line; int length = 0;
                    while ((line = input.readLine()) != null && !line.isEmpty()) if (line.toLowerCase().startsWith("content-length:")) length = Integer.parseInt(line.substring(15).trim());
                    char[] body = new char[length]; int offset = 0;
                    while (offset < length) { int count = input.read(body, offset, length - offset); if (count < 0) break; offset += count; }
                    if (first.startsWith("POST")) { posts++; lastId = new JSONObject(new String(body)).optString("clientMessageId"); }
                    byte[] response = new JSONObject().put("success", success).put("messageId", "fixture-message").toString().getBytes(StandardCharsets.UTF_8);
                    socket.getOutputStream().write(("HTTP/1.1 " + status + " Fixture\r\nContent-Length: " + response.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
                    socket.getOutputStream().write(response);
                } catch (Exception error) { if (!server.isClosed()) throw new RuntimeException(error); }
            });
            worker.start();
        }
        String base() { return "http://127.0.0.1:" + server.getLocalPort(); }
        public void close() throws Exception { server.close(); worker.join(4000); }
    }
}

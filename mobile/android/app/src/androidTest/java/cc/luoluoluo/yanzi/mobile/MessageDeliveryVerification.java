package cc.luoluoluo.yanzi.mobile;

import android.app.*;
import android.content.*;
import android.os.Bundle;
import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.lang.reflect.*;
import java.util.concurrent.*;

public final class MessageDeliveryVerification {
    static void require(boolean ok, String name) { if (!ok) throw new AssertionError(name); }
    static final class FixtureService extends DeviceHeartbeatService {
        FixtureService(Context context) { attachBaseContext(context); }
    }
    static void run(Instrumentation test) {
        Bundle result = new Bundle();
        FixtureService service = null;
        SharedPreferences prefs = null;
        String oldToken = "", oldDevice = "";
        try {
            Context context = test.getTargetContext();
            require(context.getPackageName().endsWith(".dev") && android.os.Build.MODEL.toLowerCase().contains("sdk"), "Emulator Dev only");
            require(!MobileEventNotifier.canNotify(context), "Disable notifications before test");
            MobileApplicationContext.initialize(context);
            prefs = context.getSharedPreferences("yanzi-mobile", 0);
            oldToken = prefs.getString("token", ""); oldDevice = prefs.getString("deviceId", "");
            String token = TaskRecoveryVerification.token("delivery-fixture-" + java.util.UUID.randomUUID());
            prefs.edit().putString("token", token).putString("deviceId", "delivery-fixture").commit();
            service = new FixtureService(context);
            Field field = DeviceHeartbeatService.class.getDeclaredField("prefs"); field.setAccessible(true); field.set(service, prefs);
            Method deliver = DeviceHeartbeatService.class.getDeclaredMethod("deliver", String.class, String.class, String.class, JSONObject.class); deliver.setAccessible(true);
            final int[] stages = {0};
            require(!DeviceHeartbeatService.runStage("receipt_cleanup_fixture", () -> { throw new IOException("inbox_cleanup_failed"); }), "Cleanup failure recorded");
            DeviceHeartbeatService.runStage("message_poll_fixture", () -> stages[0]++);
            require(stages[0] == 1, "Poll continues after cleanup failure");
            require(BackgroundCadence.pollInterval(true) >= 10L * 60L * 1000L, "Realtime compensation remains low frequency");
            require(BackgroundCadence.deliveryRetryInterval() >= 60000, "Delivery retry protects read budget");
            try (Server fixture = new Server()) {
                String id = "msg_" + java.util.UUID.randomUUID().toString().replace("-", "");
                JSONObject message = new JSONObject().put("messageId", id).put("kind", "photo").put("title", "YanziChat")
                        .put("sourceDeviceId", "desktop-fixture").put("payload", new JSONObject().put("attachmentId", fixture.attachmentId));
                String base = "http://127.0.0.1:" + fixture.socket.getLocalPort();
                fixture.failAck = true;
                try { deliver.invoke(service, base, token, "delivery-fixture", message); throw new AssertionError("ACK failure must propagate"); }
                catch (InvocationTargetException error) { require(error.getCause() instanceof MobileMessageClient.HttpFailure, "ACK failure visible"); }
                require(ChatHistoryStore.containsMessage(context, id), "Photo persisted with notifications disabled");
                JSONArray history = ChatHistoryStore.load(context);
                File saved = null;
                for (int i=0; i<history.length(); i++) if (id.equals(history.getJSONObject(i).optString("messageId"))) saved = new File(history.getJSONObject(i).getString("content"));
                require(saved != null && saved.isFile(), "Attachment exists");
                require(saved.length() == fixture.bytes.length && MobileAttachmentClient.hash(saved).equals(fixture.hash), "Original bytes SHA-256 verified");
                fixture.failAck = false;
                deliver.invoke(service, base, token, "delivery-fixture", message);
                deliver.invoke(service, base, token, "delivery-fixture", message);
                fixture.dropAck = true;
                deliver.invoke(service, base, token, "delivery-fixture", message);
                require(fixture.droppedAcks == 1 && fixture.acks >= 3, "Lost ACK response retries safely");
                int count = 0; history = ChatHistoryStore.load(context);
                for (int i=0; i<history.length(); i++) if (id.equals(history.getJSONObject(i).optString("messageId"))) count++;
                require(count == 1 && fixture.acks >= 2 && fixture.downloads == 1, "ACK retry keeps one chat row and one download");
                JSONObject text = new JSONObject().put("messageId", id + "a").put("kind", "text").put("title", "YanziChat").put("text", "delivery fixture");
                deliver.invoke(service, base, token, "delivery-fixture", text);
                require(ChatHistoryStore.containsMessage(context, id + "a"), "Text saves and ACKs without notifications");
                fixture.resumeFixture = true; fixture.cutContent = true;
                JSONObject resume = new JSONObject(message.toString()).put("messageId", id + "c");
                deliver.invoke(service, base, token, "delivery-fixture", resume);
                require(ChatHistoryStore.containsMessage(context, id + "c"), "Interrupted content resumes before ACK");
                require(fixture.downloads == 3 && fixture.rangeOffset == 16, "Retry continues at persisted byte offset");
                fixture.missing = true;
                JSONObject missing = new JSONObject(message.toString()).put("messageId", id + "b");
                deliver.invoke(service, base, token, "delivery-fixture", missing);
                require(!ChatHistoryStore.containsMessage(context, id + "b") && fixture.failedAcks == 1, "Missing attachment reports failure, never successful delivery");
                if (fixture.error != null) throw new AssertionError(fixture.error);
            }
            result.putString("stream", "MESSAGE_DELIVERY=PASSED (cleanup isolation, event-driven low-frequency compensation, original attachment SHA-256, notifications disabled, failed ACK retry, deduplication, text, terminal missing-attachment failure, interrupted content route retry and resume, lost ACK response)");
            test.finish(Activity.RESULT_OK, result);
        } catch (Throwable error) {
            result.putString("stream", android.util.Log.getStackTraceString(error)); test.finish(Activity.RESULT_CANCELED, result);
        } finally {
            if (prefs != null) prefs.edit().putString("token", oldToken).putString("deviceId", oldDevice).commit();
            if (service != null) for (String name : new String[]{"worker", "receivers", "outgoing", "synchronizer"}) try {
                Field f = DeviceHeartbeatService.class.getDeclaredField(name); f.setAccessible(true); ((ExecutorService)f.get(service)).shutdownNow();
            } catch (Exception ignored) {}
        }
    }
    static final class Server implements AutoCloseable {
        final ServerSocket socket = new ServerSocket(0);
        final String attachmentId = "att_" + java.util.UUID.randomUUID().toString().replace("-", "");
        final byte[] bytes = android.util.Base64.decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=", 0);
        final String hash;
        volatile boolean failAck, missing, resumeFixture, cutContent, dropAck;
        volatile int acks, downloads, failedAcks, rangeOffset, droppedAcks;
        volatile Throwable error;
        final Thread thread;
        Server() throws Exception {
            java.security.MessageDigest digest = java.security.MessageDigest.getInstance("SHA-256");
            StringBuilder h = new StringBuilder(); for (byte b : digest.digest(bytes)) h.append(String.format(java.util.Locale.ROOT, "%02x", b & 255)); hash = h.toString();
            thread = new Thread(() -> {
                while (!socket.isClosed()) try (Socket client = socket.accept()) {
                    client.setSoTimeout(10000);
                    BufferedReader input = new BufferedReader(new InputStreamReader(client.getInputStream(), StandardCharsets.UTF_8));
                    String request = input.readLine(), line; int size = 0, offset = 0;
                    while ((line=input.readLine()) != null && !line.isEmpty()) {
                        if (line.toLowerCase().startsWith("content-length:")) size=Integer.parseInt(line.substring(15).trim());
                        if (line.toLowerCase().startsWith("range: bytes=")) offset=Integer.parseInt(line.substring(13, line.indexOf('-')));
                    }
                    char[] body = new char[size]; int read=0; while (read<size) { int n=input.read(body,read,size-read); if(n<0)break; read+=n; }
                    byte[] response; int status = 200;
                    if (request.contains("/ack ")) {
                        if (dropAck) { dropAck = false; droppedAcks++; continue; }
                        require(ChatHistoryStore.load(getTargetContextStatic()).length() >= 1, "Chat must exist before ACK");
                        if (failAck) status = 500; else { acks++; if (new JSONObject(new String(body)).has("success") && !new JSONObject(new String(body)).getBoolean("success")) failedAcks++; }
                        response = "{}".getBytes(StandardCharsets.UTF_8);
                    } else if (request.contains("/content ")) {
                        downloads++; rangeOffset = offset;
                        if (cutContent) {
                            cutContent = false;
                            OutputStream truncated = client.getOutputStream();
                            truncated.write(("HTTP/1.1 200 OK\r\nContent-Length: " + bytes.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.UTF_8));
                            truncated.write(bytes, 0, 16); truncated.flush(); continue;
                        }
                        response = java.util.Arrays.copyOfRange(bytes, offset, bytes.length);
                        if (offset > 0) status = 206;
                    }
                    else if (missing) { status = 404; response = "{\"error\":\"attachment_not_found\"}".getBytes(StandardCharsets.UTF_8); }
                    else response = new JSONObject().put("size", bytes.length).put("sha256", hash).put("fileName", resumeFixture ? "resume.png" : "fixture.png").toString().getBytes(StandardCharsets.UTF_8);
                    OutputStream output = client.getOutputStream();
                    String contentRange = status == 206 ? "Content-Range: bytes " + offset + "-" + (bytes.length-1) + "/" + bytes.length + "\r\n" : "";
                    output.write(("HTTP/1.1 " + status + " OK\r\n" + contentRange + "Content-Length: " + response.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.UTF_8)); output.write(response); output.flush();
                } catch (Throwable ex) { if (!socket.isClosed()) error = ex; }
            }); thread.start();
        }
        static Context getTargetContextStatic() { return MobileApplicationContext.get(); }
        public void close() throws Exception { socket.close(); thread.join(2000); }
    }
}

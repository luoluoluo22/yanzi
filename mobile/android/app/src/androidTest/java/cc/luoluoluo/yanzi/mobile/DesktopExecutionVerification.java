package cc.luoluoluo.yanzi.mobile;

import android.app.Instrumentation;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Bundle;
import org.json.JSONObject;
import cc.luoluoluo.yanzi.sdk.DeviceTargets;

/** Explicit opt-in: execute one selected harmless extension on the logged-in user's PC. */
final class DesktopExecutionVerification {
    static void run(Instrumentation test, Bundle arguments) {
        Bundle result = new Bundle();
        StringBuilder evidence = new StringBuilder();
        try {
            Context context = test.getTargetContext();
            if (!context.getPackageName().endsWith(".dev") || !"true".equals(arguments.getString("allowPhysicalDev")))
                throw new AssertionError("Explicit isolated Dev authorization required");
            String extension = arguments.getString("extensionId", "");
            if (!"taskbar-calendar".equals(extension)) throw new AssertionError("Only harmless calendar verification allowed");
            MobileApplicationContext.initialize(context);
            MobileNetworkRouting.initialize(context);
            SharedPreferences prefs = context.getSharedPreferences("yanzi-mobile", 0);
            String base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd");
            String token = prefs.getString("token", ""), source = prefs.getString("deviceId", "");
            if (token.isEmpty() || source.isEmpty()) throw new AssertionError("Existing Dev login required");
            MobileDeviceSession.requireRegistered(base, token, source);
            if (!MobileDeviceIdentity.stableDeviceId(context).equals(MobileDeviceIdentity.stableDeviceId(context)))
                throw new AssertionError("Device identity changed without reinstall");
            String id = MobileApiClient.runExtensionOnDesktop(base, token, source, MobileDeviceIdentity.buildDeviceDisplayName(), extension, "");
            awaitCompleted(base, token, id);
            boolean persisted = false;
            for (JSONObject receipt : MobileTaskJournal.list(base, token)) if (id.equals(receipt.optString("messageId")) && "completed".equals(receipt.optString("status"))) persisted = true;
            if (!persisted) throw new AssertionError("Original execution receipt not persisted in task center");
            evidence.append("TASK_CENTER_ORIGINAL_RESULT=PASSED\n");
            evidence.append("APP_ROUTE=PASSED id=").append(id).append("\n");
            JSONObject desktop = DeviceTargets.uniqueOnlineDesktop(MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices", token, "GET", null).getJSONArray("items"));
            if (desktop == null) throw new AssertionError("One online desktop required");
            JSONObject envelope = new JSONObject().put("sourceDeviceId", source).put("targetDeviceId", desktop.getString("deviceId"))
                    .put("kind", "run-extension").put("title", "Desktop execution verification").put("text", "")
                    .put("clientMessageId", java.util.UUID.randomUUID().toString()).put("expiresAt", java.time.Instant.now().plusSeconds(120).toString())
                    .put("payload", new JSONObject().put("source", "android").put("extensionId", extension));
            id = MobileMessageClient.requestWithoutQueue(base, "/v1/me/mobile/messages", token, "POST", envelope).getString("messageId");
            awaitCompleted(base, token, id);
            evidence.append("CLOUD_ROUTE=PASSED id=").append(id).append("\n");
            // Disposable registration exercises removal without touching the user's devices.
            String fixture = "android-removal-check-" + java.util.UUID.randomUUID();
            try {
                MobileApiClient.registerDevice(base, token, fixture, "Temporary removal verification");
                MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices/" + fixture, token, "DELETE", null);
                try { MobileDeviceSession.requireRegistered(base, token, fixture); throw new AssertionError("Removal undetected"); }
                catch (MobileApiClient.MissingSourceDeviceException expected) { }
                try {
                    MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices", token, "POST", new JSONObject().put("deviceId", fixture).put("platform", "android").put("displayName", "Temporary removal verification"));
                    throw new AssertionError("Background re-enrollment accepted");
                } catch (MobileMessageClient.HttpFailure removed) { if (removed.status != 403) throw removed; }
                evidence.append("REMOVAL_DETECTED_AND_BACKGROUND_REJECTED=PASSED\n");
            } finally {
                try { MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices/" + fixture, token, "DELETE", null); }
                catch (MobileMessageClient.HttpFailure removed) { if (removed.status != 404) throw removed; }
            }
            Context disposable = new android.content.ContextWrapper(test.getContext()) {
                @Override public boolean stopService(android.content.Intent intent) { return false; }
            };
            SharedPreferences isolated = disposable.getSharedPreferences("yanzi-mobile", 0);
            isolated.edit().putString("token", "fixture-token").putString("password", "fixture-password").commit();
            MobileDeviceSession.clearRemovedLogin(disposable);
            if (isolated.contains("token") || isolated.contains("password") || !isolated.getBoolean("deviceLoginRemoved", false)) throw new AssertionError("Removed login retained credentials");
            evidence.append("REMOVED_LOGIN_CREDENTIALS_CLEARED=PASSED\n");
            if (!token.equals(prefs.getString("token", "")) || !source.equals(prefs.getString("deviceId", ""))) throw new AssertionError("User login unexpectedly modified");
            result.putString("stream", evidence.toString());
            test.finish(android.app.Activity.RESULT_OK, result);
        } catch (Throwable failure) {
            result.putString("stream", evidence + android.util.Log.getStackTraceString(failure));
            test.finish(android.app.Activity.RESULT_CANCELED, result);
        }
    }
    private static void awaitCompleted(String base, String token, String id) throws Exception {
        long deadline = android.os.SystemClock.elapsedRealtime() + 35000;
        do {
            JSONObject detail = MobileApiClient.fetchMessageDetail(base, token, id);
            JSONObject message = detail.optJSONObject("message");
            if (message == null) message = detail;
            String state = message.optString("status");
            if ("completed".equals(state) || "acked".equals(state)) return;
            if ("failed".equals(state) || "cancelled".equals(state) || "expired".equals(state)) throw new AssertionError("Desktop result " + id + ": " + state);
            Thread.sleep(1000);
        } while (android.os.SystemClock.elapsedRealtime() < deadline);
        throw new AssertionError("No terminal success for " + id);
    }
}

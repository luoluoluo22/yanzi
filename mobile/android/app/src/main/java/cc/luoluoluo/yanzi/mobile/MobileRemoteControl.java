package cc.luoluoluo.yanzi.mobile;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.app.KeyguardManager;
import android.content.Context;
import android.graphics.Path;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityNodeInfo;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.FutureTask;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Explicitly opted-in, account-owner remote screen actions.
 * The capability command is transported by the existing Yanzi authenticated device channel.
 * No arbitrary shell execution, root access, screen recording daemon or open listening port.
 */
final class MobileRemoteControl {
    private static final String ENABLE_KEY = "remoteScreenControlEnabled";
    private static final long TIMEOUT_MS = 6500;
    private static final int MAX_NODES = 130;

    static boolean enabled(Context context) {
        return context.getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE)
                .getBoolean(ENABLE_KEY, false);
    }

    static JSONObject status(Context context) throws Exception {
        return new JSONObject()
                .put("enabledByUser", enabled(context))
                .put("accessibilityConnected", MobileAccessibilityService.isEnabled())
                .put("ready", enabled(context) && MobileAccessibilityService.isEnabled())
                .put("rootOrAdbRequired", false)
                .put("requiresUnlockedDevice", true);
    }

    static JSONObject invoke(Context context, String name, JSONObject args) throws Exception {
        if ("mobile.screen.status".equals(name)) return status(context);
        if (!enabled(context)) throw new SecurityException("remote_screen_control_not_enabled_on_phone");
        final MobileAccessibilityService service = MobileAccessibilityService.connectedInstance();
        if (service == null) throw new IllegalStateException("accessibility_service_not_connected");
        KeyguardManager keyguard = (KeyguardManager) context.getSystemService(Context.KEYGUARD_SERVICE);
        if (keyguard != null && keyguard.isDeviceLocked()) throw new SecurityException("device_locked");
        if (args == null) args = new JSONObject();
        switch (name) {
            case "mobile.screen.capture": {
                CountDownLatch done = new CountDownLatch(1);
                AtomicReference<JSONObject> result = new AtomicReference<>();
                AtomicReference<String> error = new AtomicReference<>();
                MobileAccessibilityService.captureJpegBase64(new MobileAccessibilityService.ScreenshotCallback() {
                    @Override public void onSuccess(String image, int width, int height) {
                        try { result.set(new JSONObject().put("mimeType", "image/jpeg").put("encoding", "base64")
                                .put("image", image).put("width", width).put("height", height)); }
                        catch (Exception ex) { error.set(ex.getMessage()); }
                        finally { done.countDown(); }
                    }
                    @Override public void onFailure(String why) { error.set(why); done.countDown(); }
                });
                if (!done.await(TIMEOUT_MS, TimeUnit.MILLISECONDS)) throw new IllegalStateException("screenshot_timeout");
                if (error.get() != null) throw new IllegalStateException("screenshot_failed:" + error.get());
                if (result.get() == null) throw new IllegalStateException("screenshot_empty");
                return result.get();
            }
            case "mobile.screen.inspect": {
                final String target = args.optString("packageName", "");
                if (!MobilePublicFeedSnapshot.allowedPackage(target)) {
                    throw new SecurityException("public_feed_package_not_allowed");
                }
                return onMain(() -> MobilePublicFeedSnapshot.capture(service, target));
            }
            case "mobile.screen.tree":
                return onMain(() -> {
                    AccessibilityNodeInfo root = service.getRootForControl();
                    if (root == null) throw new IllegalStateException("active_window_unavailable");
                    JSONArray nodes = new JSONArray();
                    int[] seen = {0};
                    collect(root, nodes, seen, 0);
                    return new JSONObject().put("nodes", nodes).put("count", nodes.length())
                            .put("truncated", seen[0] >= MAX_NODES);
                });
            case "mobile.screen.tap": {
                final int x = coordinate(args, "x"), y = coordinate(args, "y");
                return gesture(service, x, y, x, y, 90);
            }
            case "mobile.screen.swipe": {
                final int x = coordinate(args, "x"), y = coordinate(args, "y");
                final int x2 = coordinate(args, "x2"), y2 = coordinate(args, "y2");
                final int duration = Math.max(100, Math.min(1500, args.optInt("durationMs", 350)));
                return gesture(service, x, y, x2, y2, duration);
            }
            case "mobile.screen.action": {
                final String action = args.getString("action");
                final int id;
                switch (action) {
                    case "back": id = AccessibilityService.GLOBAL_ACTION_BACK; break;
                    case "home": id = AccessibilityService.GLOBAL_ACTION_HOME; break;
                    case "recents": id = AccessibilityService.GLOBAL_ACTION_RECENTS; break;
                    case "notifications": id = AccessibilityService.GLOBAL_ACTION_NOTIFICATIONS; break;
                    default: throw new IllegalArgumentException("unsupported_action");
                }
                return onMain(() -> {
                    if (!service.performGlobalAction(id)) throw new IllegalStateException("global_action_rejected");
                    return new JSONObject().put("action", action).put("accepted", true);
                });
            }
            default: throw new IllegalArgumentException("unsupported_screen_capability");
        }
    }

    private static int coordinate(JSONObject args, String key) throws Exception {
        if (!args.has(key)) throw new IllegalArgumentException("missing_coordinate:" + key);
        int value = args.getInt(key);
        if (value < 0 || value > 10000) throw new IllegalArgumentException("invalid_coordinate:" + key);
        return value;
    }

    private static JSONObject gesture(MobileAccessibilityService service,
            int x, int y, int x2, int y2, int duration) throws Exception {
        CountDownLatch completed = new CountDownLatch(1);
        AtomicBoolean succeeded = new AtomicBoolean(false);
        Path path = new Path(); path.moveTo(x, y);
        if (x != x2 || y != y2) path.lineTo(x2, y2);
        GestureDescription gesture = new GestureDescription.Builder()
                .addStroke(new GestureDescription.StrokeDescription(path, 0, duration)).build();
        onMain(() -> {
            boolean accepted = service.dispatchGesture(gesture,
                    new AccessibilityService.GestureResultCallback() {
                        @Override public void onCompleted(GestureDescription completedGesture) {
                            succeeded.set(true); completed.countDown();
                        }
                        @Override public void onCancelled(GestureDescription cancelledGesture) {
                            completed.countDown();
                        }
                    }, null);
            if (!accepted) completed.countDown();
            return new JSONObject().put("dispatched", accepted);
        });
        if (!completed.await(TIMEOUT_MS, TimeUnit.MILLISECONDS)) throw new IllegalStateException("gesture_timeout");
        if (!succeeded.get()) throw new IllegalStateException("gesture_rejected_or_cancelled");
        return new JSONObject().put("completed", true);
    }

    private static <T> T onMain(Callable<T> call) throws Exception {
        if (Looper.myLooper() == Looper.getMainLooper()) return call.call();
        FutureTask<T> task = new FutureTask<>(call);
        new Handler(Looper.getMainLooper()).post(task);
        return task.get(TIMEOUT_MS, TimeUnit.MILLISECONDS);
    }

    private static void collect(AccessibilityNodeInfo node, JSONArray result, int[] seen, int depth)
            throws Exception {
        if (node == null || seen[0] >= MAX_NODES || depth > 14) return;
        seen[0]++;
        Rect bounds = new Rect(); node.getBoundsInScreen(bounds);
        JSONObject info = new JSONObject()
                .put("className", String.valueOf(node.getClassName()))
                .put("bounds", new JSONArray().put(bounds.left).put(bounds.top).put(bounds.right).put(bounds.bottom))
                .put("clickable", node.isClickable())
                .put("scrollable", node.isScrollable())
                .put("focused", node.isFocused())
                .put("editable", node.isEditable());
        if (!node.isPassword()) {
            if (node.getText() != null) info.put("text", truncate(node.getText().toString()));
            if (node.getContentDescription() != null)
                info.put("contentDescription", truncate(node.getContentDescription().toString()));
        }
        String viewId = node.getViewIdResourceName();
        if (viewId != null) info.put("resourceId", viewId);
        result.put(info);
        for (int i = 0; i < node.getChildCount() && seen[0] < MAX_NODES; i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child == null) continue;
            try { collect(child, result, seen, depth + 1); }
            finally { child.recycle(); }
        }
    }
    private static String truncate(String s) { return s.length() <= 160 ? s : s.substring(0, 160); }
}

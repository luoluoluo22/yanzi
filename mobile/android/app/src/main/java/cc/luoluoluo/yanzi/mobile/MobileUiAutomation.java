package cc.luoluoluo.yanzi.mobile;

import android.accessibilityservice.AccessibilityService;
import android.app.KeyguardManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Rect;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.accessibility.AccessibilityNodeInfo;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.IOException;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.FutureTask;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Bounded, opt-in foreground UI automation.
 * No raw shell, no blind coordinate macros, no background app data access.
 * Every interaction requires both the remote-screen and UI-automation toggles.
 */
final class MobileUiAutomation {
    private static final String ENABLE_KEY = "remoteUiAutomationEnabled";
    private static final String TASK_STATE_KEY = "remoteUiTaskSummary";
    private static final int MAX_TEXT = 500;
    private static final int MAX_STEPS = 12;
    private static final int MAX_NODES = 500;
    private static final long MAIN_TIMEOUT_MS = 6000;
    private static final long MAX_WAIT_MS = 12000;
    private static final long MAX_TASK_MS = 85000;

    private static final Object TASK_LOCK = new Object();
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor(r -> {
        Thread thread = new Thread(r, "yanzi-remote-ui-task");
        thread.setDaemon(true);
        return thread;
    });
    private static Task running;

    static boolean automationEnabled(Context context) {
        return context.getSharedPreferences("yanzi-mobile", 0).getBoolean(ENABLE_KEY, false);
    }

    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences("yanzi-mobile", 0);
    }

    static JSONObject status(Context context) throws Exception {
        MobileAccessibilityService service = MobileAccessibilityService.connectedInstance();
        KeyguardManager lock = (KeyguardManager)context.getSystemService(Context.KEYGUARD_SERVICE);
        boolean deviceLocked = lock != null && lock.isDeviceLocked();
        return new JSONObject()
                .put("screenControlEnabled", MobileRemoteControl.enabled(context))
                .put("accessibilityConnected", service != null)
                .put("automationEnabledByUser", automationEnabled(context))
                .put("deviceLocked", deviceLocked)
                .put("ready", MobileRemoteControl.enabled(context) && automationEnabled(context)
                        && service != null && !deviceLocked)
                .put("requiresUnlockedDevice", true);
    }

    private static MobileAccessibilityService require(Context context, boolean writing) {
        if (!MobileRemoteControl.enabled(context))
            throw new SecurityException("remote_screen_control_not_enabled_open_yanzi_my");
        if (writing && !automationEnabled(context))
            throw new SecurityException("remote_ui_automation_not_enabled_open_yanzi_my");
        MobileAccessibilityService service = MobileAccessibilityService.connectedInstance();
        if (service == null) throw new SecurityException("accessibility_service_not_connected");
        KeyguardManager keyguard = (KeyguardManager)context.getSystemService(Context.KEYGUARD_SERVICE);
        if (keyguard != null && keyguard.isDeviceLocked()) throw new SecurityException("device_locked");
        return service;
    }

    static JSONObject invoke(Context context, String name, JSONObject args) throws Exception {
        if (args == null) args = new JSONObject();
        if ("mobile.ui.status".equals(name)) return status(context);
        if ("mobile.ui.task.status".equals(name)) return taskStatus(context, args);
        if ("mobile.ui.task.cancel".equals(name)) {
            require(context, true);
            return cancelTask(context, args);
        }
        if ("mobile.ui.task.start".equals(name)) {
            require(context, true);
            return startTask(context, args);
        }
        boolean write = "mobile.ui.click".equals(name) || "mobile.ui.type".equals(name);
        MobileAccessibilityService service = require(context, write);
        switch (name) {
            case "mobile.ui.find": return find(service, args);
            case "mobile.ui.click": return click(service, args);
            case "mobile.ui.type": return type(service, args);
            case "mobile.ui.wait": return waitFor(context, service, args, null, MAX_WAIT_MS);
            case "mobile.ui.verify": return verify(service, args);
            default: throw new IOException("unsupported_mobile_ui_capability");
        }
    }

    private static <T> T onMain(Callable<T> body) throws Exception {
        if (Looper.myLooper() == Looper.getMainLooper()) return body.call();
        FutureTask<T> future = new FutureTask<>(body);
        new Handler(Looper.getMainLooper()).post(future);
        return future.get(MAIN_TIMEOUT_MS, TimeUnit.MILLISECONDS);
    }

    private static String bounded(String text) {
        return text == null ? "" : (text.length() > 160 ? text.substring(0, 160) : text);
    }

    static boolean matches(String candidate, String expected, boolean contains) {
        if (candidate == null) return false;
        return contains ? candidate.contains(expected) : candidate.equals(expected);
    }

    private static boolean matchesNode(AccessibilityNodeInfo node, JSONObject selector) {
        if (node.isPassword()) return false;
        boolean contains = selector.optBoolean("contains", false);
        if (selector.has("resourceId")
                && !matches(node.getViewIdResourceName(), selector.optString("resourceId"), contains)) return false;
        if (selector.has("text")
                && !matches(node.getText() == null ? null : node.getText().toString(), selector.optString("text"), contains)) return false;
        if (selector.has("description")
                && !matches(node.getContentDescription() == null ? null : node.getContentDescription().toString(),
                        selector.optString("description"), contains)) return false;
        if (selector.has("className")
                && !matches(node.getClassName() == null ? null : node.getClassName().toString(),
                        selector.optString("className"), contains)) return false;
        if (selector.optBoolean("editableOnly", false) && !node.isEditable()) return false;
        if (selector.optBoolean("clickableOnly", false) && !node.isClickable()) return false;
        return true;
    }

    private static JSONObject selector(JSONObject args) throws Exception {
        JSONObject s = args.optJSONObject("selector");
        if (s == null) throw new IllegalArgumentException("selector_required");
        boolean any = false;
        for (String key : new String[]{"resourceId", "text", "description", "className"}) {
            if (!s.has(key)) continue;
            String value = s.getString(key);
            if (value.isEmpty() || value.length() > 180) throw new IllegalArgumentException("invalid_selector_" + key);
            any = true;
        }
        if (!any) throw new IllegalArgumentException("selector_attribute_required");
        return s;
    }

    private static final class Match {
        final AccessibilityNodeInfo node;
        final int count;
        final String pkg;
        Match(AccessibilityNodeInfo node, int count, String pkg) {
            this.node = node; this.count = count; this.pkg = pkg;
        }
    }

    private static final class SearchState {
        AccessibilityNodeInfo matched;
        int count;
        int visited;
    }

    private static void walk(AccessibilityNodeInfo node, JSONObject selector, SearchState found, int depth) {
        if (node == null || depth > 20 || found.visited++ >= MAX_NODES) return;
        if (matchesNode(node, selector)) {
            if (found.count++ == 0) found.matched = AccessibilityNodeInfo.obtain(node);
        }
        for (int i = 0; i < node.getChildCount() && found.visited < MAX_NODES; i++) {
            AccessibilityNodeInfo child = node.getChild(i);
            if (child == null) continue;
            try { walk(child, selector, found, depth + 1); }
            finally { child.recycle(); }
        }
    }

    private static Match search(MobileAccessibilityService service, JSONObject args) throws Exception {
        JSONObject s = selector(args);
        AccessibilityNodeInfo root = service.getRootInActiveWindow();
        if (root == null) throw new IllegalStateException("active_window_unavailable");
        try {
            String pkg = root.getPackageName() == null ? "" : root.getPackageName().toString();
            String expected = args.optString("packageName", "");
            if (!expected.isEmpty() && !expected.equals(pkg))
                throw new IllegalStateException("foreground_package_mismatch:" + pkg);
            SearchState found = new SearchState();
            walk(root, s, found, 0);
            return new Match(found.matched, found.count, pkg);
        } finally { root.recycle(); }
    }

    private static JSONObject describe(Match match) throws Exception {
        JSONObject obj = new JSONObject().put("found", match.node != null)
                .put("matches", match.count).put("packageName", match.pkg);
        if (match.node == null) return obj;
        AccessibilityNodeInfo node = match.node;
        Rect rect = new Rect();
        node.getBoundsInScreen(rect);
        obj.put("resourceId", String.valueOf(node.getViewIdResourceName()))
                .put("className", String.valueOf(node.getClassName()))
                .put("bounds", new JSONArray().put(rect.left).put(rect.top).put(rect.right).put(rect.bottom))
                .put("clickable", node.isClickable()).put("editable", node.isEditable())
                .put("password", node.isPassword());
        if (!node.isPassword()) {
            if (node.getText() != null) obj.put("text", bounded(node.getText().toString()));
            if (node.getContentDescription() != null) obj.put("description", bounded(node.getContentDescription().toString()));
        }
        return obj;
    }

    private static JSONObject find(MobileAccessibilityService service, JSONObject args) throws Exception {
        return onMain(() -> {
            Match match = search(service, args);
            try { return describe(match); }
            finally { if (match.node != null) match.node.recycle(); }
        });
    }

    private static void requireUnique(Match match, JSONObject args) {
        if (match.node == null) throw new IllegalStateException("ui_target_not_found");
        if (match.count > 1 && !args.optBoolean("allowFirst", false))
            throw new IllegalStateException("ambiguous_ui_target:" + match.count);
    }

    private static JSONObject click(MobileAccessibilityService service, JSONObject args) throws Exception {
        return onMain(() -> {
            Match match = search(service, args);
            try {
                requireUnique(match, args);
                AccessibilityNodeInfo node = AccessibilityNodeInfo.obtain(match.node);
                try {
                    for (int depth = 0; depth < 7; depth++) {
                        if (node.isClickable() && node.isEnabled()) {
                            if (!node.performAction(AccessibilityNodeInfo.ACTION_CLICK))
                                throw new IllegalStateException("ui_click_rejected");
                            return new JSONObject().put("clicked", true).put("packageName", match.pkg);
                        }
                        AccessibilityNodeInfo parent = node.getParent();
                        if (parent == null) break;
                        node.recycle(); node = parent;
                    }
                } finally { node.recycle(); }
                throw new IllegalStateException("clickable_ancestor_not_found");
            } finally { if (match.node != null) match.node.recycle(); }
        });
    }

    private static JSONObject type(MobileAccessibilityService service, JSONObject args) throws Exception {
        String value = args.getString("value");
        if (value.length() > MAX_TEXT) throw new IllegalArgumentException("ui_text_too_long");
        return onMain(() -> {
            Match match = search(service, args);
            try {
                requireUnique(match, args);
                if (!match.node.isEditable() || !match.node.isEnabled() || match.node.isPassword())
                    throw new SecurityException("ui_target_not_editable_or_sensitive");
                if (!match.node.getActionList().stream().anyMatch(a -> a.getId() == AccessibilityNodeInfo.ACTION_SET_TEXT))
                    throw new IllegalStateException("set_text_not_supported");
                Bundle bundle = new Bundle();
                bundle.putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, value);
                if (!match.node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, bundle))
                    throw new IllegalStateException("ui_set_text_rejected");
                return new JSONObject().put("typed", true).put("characters", value.length())
                        .put("packageName", match.pkg);
            } finally { if (match.node != null) match.node.recycle(); }
        });
    }

    private static JSONObject verify(MobileAccessibilityService service, JSONObject args) throws Exception {
        JSONObject match = find(service, args);
        boolean expectFound = args.optBoolean("expectFound", true);
        if (match.getBoolean("found") != expectFound)
            throw new IllegalStateException("ui_verification_failed");
        return new JSONObject().put("verified", true).put("found", match.getBoolean("found"))
                .put("matches", match.getInt("matches")).put("packageName", match.getString("packageName"));
    }

    private static JSONObject waitFor(Context context, MobileAccessibilityService service,
            JSONObject args, Task task, long deadlineAllowance) throws Exception {
        long timeout = Math.max(300, Math.min(deadlineAllowance, args.optLong("timeoutMs", 5000)));
        boolean expectFound = args.optBoolean("expectFound", true);
        long end = SystemClock.elapsedRealtime() + timeout;
        while (true) {
            if (task != null && task.cancelled.get()) throw new InterruptedException("ui_task_cancelled");
            require(context, task != null);
            JSONObject match = find(service, args);
            if (match.getBoolean("found") == expectFound)
                return new JSONObject().put("matched", true).put("found", expectFound)
                        .put("packageName", match.getString("packageName"));
            if (SystemClock.elapsedRealtime() >= end) throw new IllegalStateException("ui_wait_timeout");
            Thread.sleep(Math.min(250L, Math.max(1, end - SystemClock.elapsedRealtime())));
        }
    }

    private static final class Task {
        final String id = UUID.randomUUID().toString();
        final AtomicBoolean cancelled = new AtomicBoolean(false);
        final String packageName;
        final JSONArray steps;
        final long startAt = SystemClock.elapsedRealtime();
        Task(JSONArray steps, String packageName) { this.steps = steps; this.packageName = packageName; }
    }

    private static JSONObject taskStatus(Context context, JSONObject args) throws Exception {
        JSONObject state = new JSONObject(prefs(context).getString(TASK_STATE_KEY, "{}"));
        String requestedId = args.optString("taskId", "");
        if (!requestedId.isEmpty() && !requestedId.equals(state.optString("taskId")))
            throw new IOException("task_not_found");
        if ("running".equals(state.optString("state"))) {
            synchronized (TASK_LOCK) {
                // Re-read state under the lock: the worker may have just completed.
                state = new JSONObject(prefs(context).getString(TASK_STATE_KEY, "{}"));
                if ("running".equals(state.optString("state"))
                        && (running == null || !running.id.equals(state.optString("taskId")))) {
                    state.put("state", "interrupted").put("reason", "process_restarted");
                    saveSummary(context, state);
                }
            }
        }
        return state;
    }

    private static void saveSummary(Context context, JSONObject data) throws Exception {
        if (!prefs(context).edit().putString(TASK_STATE_KEY, data.toString()).commit())
            throw new IOException("ui_task_state_persist_failed");
    }

    private static JSONObject startTask(Context context, JSONObject args) throws Exception {
        JSONArray steps = args.getJSONArray("steps");
        if (steps.length() == 0 || steps.length() > MAX_STEPS)
            throw new IllegalArgumentException("invalid_task_step_count");
        MobileAccessibilityService service = require(context, true);
        String pkg = onMain(() -> {
            AccessibilityNodeInfo root = service.getRootInActiveWindow();
            if (root == null) throw new IllegalStateException("active_window_unavailable");
            try { return root.getPackageName() == null ? "" : root.getPackageName().toString(); }
            finally { root.recycle(); }
        });
        String expected = args.optString("packageName", pkg);
        if (pkg.isEmpty() || !pkg.equals(expected)) throw new IllegalStateException("foreground_package_mismatch");
        JSONArray validated = new JSONArray();
        for (int i = 0; i < steps.length(); i++) {
            JSONObject step = steps.getJSONObject(i);
            String action = step.getString("action");
            if (!action.equals("find") && !action.equals("click") && !action.equals("type")
                    && !action.equals("wait") && !action.equals("verify"))
                throw new IllegalArgumentException("unsupported_ui_task_action:" + action);
            selector(step);
            if ("type".equals(action) && step.getString("value").length() > MAX_TEXT)
                throw new IllegalArgumentException("ui_text_too_long");
            validated.put(new JSONObject(step.toString()).put("packageName", pkg));
        }
        synchronized (TASK_LOCK) {
            if (running != null) throw new IllegalStateException("ui_task_already_running");
            final Task task = new Task(validated, pkg);
            JSONObject state = new JSONObject().put("taskId", task.id).put("state", "running")
                    .put("packageName", pkg).put("totalSteps", validated.length())
                    .put("completedSteps", 0).put("createdAt", System.currentTimeMillis());
            saveSummary(context, state);
            running = task;
            WORKER.submit(() -> runTask(context.getApplicationContext(), task));
            return state;
        }
    }

    private static void runTask(Context context, Task task) {
        JSONObject state = null;
        try {
            state = new JSONObject(prefs(context).getString(TASK_STATE_KEY, "{}"));
            for (int i = 0; i < task.steps.length(); i++) {
                if (task.cancelled.get()) throw new InterruptedException("ui_task_cancelled");
                if (SystemClock.elapsedRealtime() - task.startAt >= MAX_TASK_MS)
                    throw new IllegalStateException("ui_task_deadline_exceeded");
                MobileAccessibilityService service = require(context, true);
                JSONObject step = task.steps.getJSONObject(i);
                String action = step.getString("action");
                // No screen/text contents or typed strings are ever stored as job state.
                switch (action) {
                    case "find": {
                        JSONObject found = find(service, step);
                        if (!found.getBoolean("found")) throw new IllegalStateException("ui_target_not_found");
                        break;
                    }
                    case "click": click(service, step); break;
                    case "type": type(service, step); break;
                    case "wait":
                        waitFor(context, service, step, task,
                                Math.min(MAX_WAIT_MS, Math.max(300L,
                                        MAX_TASK_MS - (SystemClock.elapsedRealtime() - task.startAt)))); break;
                    case "verify": verify(service, step); break;
                    default: throw new IllegalArgumentException("unsupported_ui_task_action");
                }
                if (task.cancelled.get()) throw new InterruptedException("ui_task_cancelled");
                state.put("completedSteps", i + 1).put("updatedAt", System.currentTimeMillis());
                saveSummary(context, state);
            }
            state.put("state", "completed").put("updatedAt", System.currentTimeMillis());
        } catch (Exception ex) {
            try {
                if (state == null) state = new JSONObject().put("taskId", task.id);
                state.put("state", task.cancelled.get() || ex instanceof InterruptedException ? "cancelled" : "failed");
                // Only machine-readable error codes; no selector / UI text logged or retained.
                String reason = ex.getMessage() == null ? ex.getClass().getSimpleName() : ex.getMessage();
                state.put("reason", bounded(reason));
                state.put("updatedAt", System.currentTimeMillis());
            } catch (Exception ignored) {}
        } finally {
            synchronized (TASK_LOCK) {
                try { if (state != null) saveSummary(context, state); } catch (Exception ignored) {}
                if (running == task) running = null;
            }
        }
    }

    private static JSONObject cancelTask(Context context, JSONObject args) throws Exception {
        synchronized (TASK_LOCK) {
            if (running == null) return taskStatus(context, args);
            String requestedId = args.optString("taskId", "");
            if (!requestedId.isEmpty() && !requestedId.equals(running.id))
                throw new IOException("task_not_found");
            running.cancelled.set(true);
            return new JSONObject().put("taskId", running.id).put("cancellationRequested", true);
        }
    }
}

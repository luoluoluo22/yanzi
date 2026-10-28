package cc.luoluoluo.yanzi.mobile;

import android.graphics.Rect;
import android.os.SystemClock;
import android.view.accessibility.AccessibilityNodeInfo;
import org.json.JSONArray;
import org.json.JSONObject;

/**
 * Bounded snapshot of visible public-feed accessibility nodes for diagnostics.
 * Does not access private app data, screenshot, history or background windows.
 * Must run on the accessibility service main thread after explicit screen-control
 * authorization and device-unlocked checks (MobileRemoteControl.invoke).
 */
final class MobilePublicFeedSnapshot {
    private static final int MAX_VISITED = 260;
    private static final int MAX_RETURNED = 85;
    private static final int MAX_DEPTH = 18;
    private static final long MAX_TRAVERSAL_MS = 1750;
    private static final int MAX_FIELD = 180;

    private MobilePublicFeedSnapshot() {}

    static boolean allowedPackage(String pkg) {
        return "tv.danmaku.bili".equals(pkg)
                || "com.xingin.xhs".equals(pkg)
                || "com.ss.android.ugc.aweme".equals(pkg);
    }

    static JSONObject capture(MobileAccessibilityService service, String expectedPackage)
            throws Exception {
        if (!allowedPackage(expectedPackage)) {
            throw new SecurityException("public_feed_package_not_allowed");
        }
        AccessibilityNodeInfo root = service.getRootForControl();
        if (root == null) throw new IllegalStateException("active_window_unavailable");
        try {
            String currentPackage = root.getPackageName() == null ? "" : root.getPackageName().toString();
            if (!expectedPackage.equals(currentPackage)) {
                throw new SecurityException("foreground_package_mismatch");
            }
            final long start = SystemClock.uptimeMillis();
            final long deadline = start + MAX_TRAVERSAL_MS;
            int[] visited = {0};
            JSONArray nodes = new JSONArray();
            walk(root, nodes, visited, 0, deadline);
            return new JSONObject()
                    .put("packageName", currentPackage)
                    .put("source", "native_accessibility_root")
                    .put("visitedNodes", visited[0])
                    .put("returnedNodes", nodes.length())
                    .put("elapsedMs", SystemClock.uptimeMillis() - start)
                    .put("truncated", visited[0] >= MAX_VISITED
                            || nodes.length() >= MAX_RETURNED
                            || SystemClock.uptimeMillis() >= deadline)
                    .put("nodes", nodes);
        } finally {
            root.recycle();
        }
    }

    private static void walk(AccessibilityNodeInfo node, JSONArray out, int[] visited,
            int depth, long deadline) throws Exception {
        if (node == null || visited[0] >= MAX_VISITED || out.length() >= MAX_RETURNED
                || depth > MAX_DEPTH || SystemClock.uptimeMillis() >= deadline) {
            return;
        }
        visited[0]++;
        if (!node.isPassword()) {
            CharSequence text = node.getText();
            CharSequence desc = node.getContentDescription();
            if ((text != null && text.length() > 0) || (desc != null && desc.length() > 0)) {
                Rect bounds = new Rect();
                node.getBoundsInScreen(bounds);
                JSONObject row = new JSONObject()
                        .put("bounds", new JSONArray().put(bounds.left).put(bounds.top)
                                .put(bounds.right).put(bounds.bottom))
                        .put("depth", depth);
                if (text != null && text.length() > 0) {
                    row.put("text", truncate(text.toString()));
                }
                if (desc != null && desc.length() > 0) {
                    row.put("contentDescription", truncate(desc.toString()));
                }
                String id = node.getViewIdResourceName();
                if (id != null) row.put("resourceId", truncate(id));
                out.put(row);
            }
        }
        for (int i = 0; i < node.getChildCount(); i++) {
            if (visited[0] >= MAX_VISITED || out.length() >= MAX_RETURNED
                    || SystemClock.uptimeMillis() >= deadline) return;
            AccessibilityNodeInfo child = node.getChild(i);
            if (child == null) continue;
            try {
                walk(child, out, visited, depth + 1, deadline);
            } finally {
                child.recycle();
            }
        }
    }

    private static String truncate(String text) {
        return text.length() <= MAX_FIELD ? text : text.substring(0, MAX_FIELD);
    }
}

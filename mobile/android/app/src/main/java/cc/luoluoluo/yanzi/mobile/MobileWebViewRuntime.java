package cc.luoluoluo.yanzi.mobile;

import android.webkit.WebView;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Map;

/** Main-thread runtime ownership keeps headless runners alive until done/fail or host destruction. */
final class MobileWebViewRuntime implements AutoCloseable {
    final Map<String, WebView> headless = new HashMap<>();
    WebView active;
    void release(String id) {
        if (id != null && !id.trim().isEmpty()) destroy(headless.remove(id));
    }
    void releaseActive() {
        WebView previous = active;
        active = null;
        destroy(previous);
    }
    static void destroy(WebView runner) {
        if (runner == null) return;
        try { runner.removeJavascriptInterface("yanziMobileJsHost"); } catch (Exception ignored) { }
        try { runner.stopLoading(); } catch (Exception ignored) { }
        try { runner.destroy(); } catch (Exception ignored) { }
    }
    public void close() {
        releaseActive();
        ArrayList<WebView> runners = new ArrayList<>(headless.values());
        headless.clear();
        for (WebView runner : runners) destroy(runner);
    }
}

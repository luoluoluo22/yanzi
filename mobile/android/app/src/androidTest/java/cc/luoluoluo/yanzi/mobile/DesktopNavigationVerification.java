package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

/** Native Dev UI coverage; only the explicit sendMessage flag sends a test to the owner's PC. */
final class DesktopNavigationVerification {
    static void run(Instrumentation test, Bundle args) {
        Bundle output = new Bundle();
        try {
            if (!test.getTargetContext().getPackageName().endsWith(".dev")) throw new AssertionError("Dev only");
            MainActivity a = (MainActivity) test.startActivitySync(new android.content.Intent(test.getTargetContext(), MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
            test.waitForIdleSync();
            test.runOnMainSync(() -> {
                View tab = (View) field(a, "desktopExtensionTabButton");
                View bar = (View) field(a, "bottomTabs");
                require(!containsText((View) field(a, "desktopExtensionTabPage"), "远程操作"), "Old heading removed");
                boolean connected = (Boolean) field(a, "isDesktopConnected");
                String type = (String) field(a, "desktopConnectionType");
                String[] types = {"lan", "cloud", "reconnecting", ""};
                String[] labels = {"局域网在线", "云端在线", "局域网重连中", "离线"};
                for (int i = 0; i < types.length; i++) {
                    set(a, "isDesktopConnected", i != 3); set(a, "desktopConnectionType", types[i]); call(a, "updateConnectionUi");
                    require(tab.getContentDescription().toString().contains(labels[i]), "Tab exposes " + labels[i]);
                }
                set(a, "isDesktopConnected", connected); set(a, "desktopConnectionType", type); call(a, "updateConnectionUi");
                call(a, "beginMessageSend"); require(bar.getVisibility() == View.GONE, "Send hides navigation");
                call(a, "beginMessageSend"); call(a, "finishMessageSend"); require(bar.getVisibility() == View.GONE, "Concurrent send retains hidden navigation");
                call(a, "finishMessageSend"); require(bar.getVisibility() == View.VISIBLE, "Final completion restores navigation");
                set(a, "isAiLoading", true); call(a, "updateBottomTabsVisibility"); require(bar.getVisibility() == View.GONE, "AI send hides navigation");
                set(a, "isAiLoading", false); call(a, "updateBottomTabsVisibility"); require(bar.getVisibility() == View.VISIBLE, "AI stop restores navigation");
                call(a, "selectTab", "mobile"); tab.performClick(); require("desktop".equals(field(a, "selectedTab")), "First tap selects PC tab");
            });
            Instrumentation.ActivityMonitor monitor = test.addMonitor(LanPairingActivity.class.getName(), null, false);
            test.runOnMainSync(() -> ((View) field(a, "desktopExtensionTabButton")).performClick());
            Activity detail = test.waitForMonitorWithTimeout(monitor, 5000);
            require(detail != null, "Repeated tap opens existing connection details");
            test.runOnMainSync(() -> {
                require(containsText(detail.getWindow().getDecorView(), "电脑连接状态"), "Correct detail screen");
                require(containsText(detail.getWindow().getDecorView(), "重新检测") || containsText(detail.getWindow().getDecorView(), "正在检测…"), "Recheck control retained");
                detail.finish();
            });
            test.removeMonitor(monitor); test.waitForIdleSync();
            if ("true".equals(args.getString("sendMessage"))) {
                test.runOnMainSync(() -> {
                    call(a, "selectSubTab", 0);
                    ((android.widget.EditText) field(a, "chatInputEditText")).setText("燕子 0.2.48 导航发送回归测试");
                    call(a, "handleSendChatMessageClick");
                    require(((View) field(a, "bottomTabs")).getVisibility() == View.GONE, "Actual text send immediately hides navigation");
                });
                long deadline = android.os.SystemClock.elapsedRealtime() + 40000;
                final boolean[] done = {false};
                do {
                    Thread.sleep(300);
                    test.runOnMainSync(() -> done[0] = ((Integer) field(a, "pendingMessageSends")) == 0);
                } while (!done[0] && android.os.SystemClock.elapsedRealtime() < deadline);
                require(done[0], "Actual text send finished");
                test.runOnMainSync(() -> require(((View) field(a, "bottomTabs")).getVisibility() == View.VISIBLE, "Actual text send restores navigation"));
            }
            output.putString("stream", "DESKTOP_NAVIGATION=PASSED (4 states, repeated tap, details, concurrent sends, AI stop, actual send lifecycle)");
            test.finish(Activity.RESULT_OK, output);
        } catch (Throwable failure) {
            output.putString("stream", android.util.Log.getStackTraceString(failure)); test.finish(Activity.RESULT_CANCELED, output);
        }
    }
    private static Object field(Object a, String key) {
        try { java.lang.reflect.Field f = MainActivity.class.getDeclaredField(key); f.setAccessible(true); return f.get(a); }
        catch (Exception error) { throw new AssertionError(error); }
    }
    private static void set(Object a, String key, Object value) {
        try { java.lang.reflect.Field f = MainActivity.class.getDeclaredField(key); f.setAccessible(true); f.set(a, value); }
        catch (Exception error) { throw new AssertionError(error); }
    }
    private static void call(Object a, String key, Object... args) {
        try {
            Class<?>[] types = new Class<?>[args.length];
            for (int i = 0; i < args.length; i++) types[i] = args[i] instanceof Integer ? int.class : args[i].getClass();
            java.lang.reflect.Method m = MainActivity.class.getDeclaredMethod(key, types); m.setAccessible(true); m.invoke(a, args);
        } catch (Exception error) { throw new AssertionError(error); }
    }
    private static boolean containsText(View view, String value) {
        if (view instanceof TextView && value.contentEquals(((TextView) view).getText())) return true;
        if (view instanceof ViewGroup) { ViewGroup g = (ViewGroup) view; for (int i = 0; i < g.getChildCount(); i++) if (containsText(g.getChildAt(i), value)) return true; }
        return false;
    }
    private static void require(boolean value, String label) { if (!value) throw new AssertionError(label); }
}

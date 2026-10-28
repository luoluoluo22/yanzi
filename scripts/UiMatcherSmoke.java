package cc.luoluoluo.yanzi.mobile;

/** JVM smoke test for deterministic UI selector matching. */
public final class UiMatcherSmoke {
    private static void check(boolean expected, String candidate, String pattern, boolean contains) {
        boolean actual = MobileUiAutomation.matches(candidate, pattern, contains);
        if (actual != expected)
            throw new AssertionError("expected=" + expected + ", actual=" + actual);
    }

    public static void main(String[] args) {
        check(true, "com.android.fileexplorer:id/search", "com.android.fileexplorer:id/search", false);
        check(false, "com.android.fileexplorer:id/search", "search", false);
        check(true, "com.android.fileexplorer:id/search", "search", true);
        check(true, "搜索文件", "搜索", true);
        check(false, "文件", "搜索", true);
        check(false, null, "搜索", true);
        check(true, "android.widget.EditText", "android.widget.EditText", false);
        System.out.println("UI_MATCHER_SMOKE_OK=7");
    }
}

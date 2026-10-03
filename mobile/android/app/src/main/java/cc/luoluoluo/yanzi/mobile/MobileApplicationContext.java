package cc.luoluoluo.yanzi.mobile;

import android.content.Context;

final class MobileApplicationContext {
    private static volatile Context application;
    static void initialize(Context context) { application = context.getApplicationContext(); }
    static Context get() {
        Context context = application;
        if (context == null) throw new IllegalStateException("Application context is not initialized");
        return context;
    }
}

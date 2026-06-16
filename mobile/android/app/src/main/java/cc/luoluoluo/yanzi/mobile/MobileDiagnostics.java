package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.SharedPreferences;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

public final class MobileDiagnostics {
    private static final String PREFS_NAME = "yanzi-mobile";
    private static final String KEY_LOG = "diagnosticLog";
    private static final int MAX_LENGTH = 30000;

    private MobileDiagnostics() {
    }

    public static String append(Context context, String status) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        String existing = prefs.getString(KEY_LOG, "");
        String line = "[" + new SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(new Date()) + "] " + status;
        String combined = existing == null || existing.trim().isEmpty() ? line : existing + "\n" + line;
        if (combined.length() > MAX_LENGTH) {
            combined = combined.substring(combined.length() - MAX_LENGTH);
            int firstBreak = combined.indexOf('\n');
            if (firstBreak >= 0 && firstBreak + 1 < combined.length()) {
                combined = combined.substring(firstBreak + 1);
            }
        }
        prefs.edit().putString(KEY_LOG, combined).apply();
        return combined;
    }

    public static String get(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getString(KEY_LOG, "");
    }

    public static void clear(Context context) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit().putString(KEY_LOG, "").apply();
    }
}

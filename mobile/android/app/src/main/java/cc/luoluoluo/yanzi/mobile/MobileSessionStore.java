package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.SharedPreferences;

/** One account snapshot, compared again before applying asynchronous work. */
final class MobileSessionStore {
    static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";
    static SharedPreferences preferences(Context context) {
        return context.getApplicationContext().getSharedPreferences("yanzi-mobile", 0);
    }
    static final class Snapshot {
        final String baseUrl, token, deviceId;
        Snapshot(SharedPreferences preferences) {
            baseUrl = preferences.getString("baseUrl", DEFAULT_BASE_URL).replaceAll("/+$", "");
            token = preferences.getString("token", "");
            deviceId = preferences.getString("deviceId", "");
        }
        boolean matches(String base, String authorization) {
            return baseUrl.equals(base.replaceAll("/+$", "")) && token.equals(authorization);
        }
        void requireCurrent() throws CloudRequestRetry.SessionChanged {
            Snapshot current = snapshot(MobileApplicationContext.get());
            if (!baseUrl.equals(current.baseUrl) || !token.equals(current.token) || !deviceId.equals(current.deviceId))
                throw new CloudRequestRetry.SessionChanged();
        }
    }
    static Snapshot snapshot(Context context) { return new Snapshot(preferences(context)); }
}

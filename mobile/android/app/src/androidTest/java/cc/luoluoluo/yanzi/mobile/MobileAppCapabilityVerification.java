package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.os.Bundle;
import org.json.JSONArray;
import org.json.JSONObject;

/** Physical-device safe regression for generic mobile app capabilities. */
public final class MobileAppCapabilityVerification {
    private static void require(boolean value, String message) {
        if (!value) throw new AssertionError(message);
    }

    static void run(Instrumentation instrumentation) {
        Bundle result = new Bundle();
        try {
            Context context = instrumentation.getTargetContext();
            require(context.getPackageName().endsWith(".dev"), "Dev package only");

            JSONObject listed = MobileAppCapabilities.invoke(
                    context, "mobile.apps.list", new JSONObject().put("limit", 500));
            JSONArray apps = listed.getJSONArray("items");
            require(apps.length() >= 10, "Expected launcher app inventory");

            JSONObject wechat = MobileAppCapabilities.invoke(
                    context, "mobile.apps.info", new JSONObject().put("app", "com.tencent.mm"));
            require("com.tencent.mm".equals(wechat.getString("packageName")), "WeChat package resolution");
            require(!wechat.optString("name").isEmpty(), "WeChat label");

            JSONObject termux = MobileAppCapabilities.invoke(
                    context, "mobile.apps.info", new JSONObject().put("app", "com.termux"));
            require("com.termux".equals(termux.getString("packageName")), "Termux package resolution");

            JSONObject opened = MobileAppCapabilities.invoke(
                    context, "mobile.apps.open", new JSONObject().put("app", "com.tencent.mm"));
            require(opened.optBoolean("opened"), "WeChat open result");

            JSONArray catalog = MobileDeviceCapabilities.catalog();
            boolean listFound=false, openFound=false, shareFound=false;
            for (int i=0;i<catalog.length();i++) {
                JSONObject item=catalog.getJSONObject(i);
                if ("mobile.apps.list".equals(item.optString("name"))) listFound=item.optBoolean("readOnly");
                if ("mobile.apps.open".equals(item.optString("name"))) openFound=!item.optBoolean("readOnly");
                if ("mobile.apps.shareText".equals(item.optString("name"))) shareFound=!item.optBoolean("readOnly");
            }
            require(listFound && openFound && shareFound, "Mobile app capabilities advertised");

            result.putString("stream", "MOBILE_APP_CAPABILITIES=PASSED apps=" + apps.length()
                    + " wechat=" + wechat.optString("name") + " termux=" + termux.optString("name"));
            instrumentation.finish(Activity.RESULT_OK, result);
        } catch (Throwable failed) {
            result.putString("stream", android.util.Log.getStackTraceString(failed));
            instrumentation.finish(Activity.RESULT_CANCELED, result);
        }
    }
}

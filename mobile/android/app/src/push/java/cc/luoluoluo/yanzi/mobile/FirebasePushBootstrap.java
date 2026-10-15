package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import com.google.firebase.FirebaseApp;
import com.google.firebase.FirebaseOptions;
import com.google.firebase.messaging.FirebaseMessaging;
import org.json.*;

public final class FirebasePushBootstrap {
    public static void initialize(Context context) throws Exception {
        JSONObject config = new JSONObject(BuildConfig.FCM_CONFIG);
        JSONArray clients = config.getJSONArray("client");
        JSONObject selected = null;
        for (int i = 0; i < clients.length(); i++) {
            JSONObject client = clients.getJSONObject(i);
            if (context.getPackageName().equals(client.getJSONObject("client_info").getJSONObject("android_client_info").getString("package_name"))) selected = client;
        }
        if (selected == null) throw new IllegalArgumentException("Firebase configuration must match applicationId");
        if (FirebaseApp.getApps(context).isEmpty()) FirebaseApp.initializeApp(context, new FirebaseOptions.Builder()
                .setApplicationId(selected.getJSONObject("client_info").getString("mobilesdk_app_id"))
                .setApiKey(selected.getJSONArray("api_key").getJSONObject(0).getString("current_key"))
                .setProjectId(config.getJSONObject("project_info").getString("project_id"))
                .setGcmSenderId(config.getJSONObject("project_info").getString("project_number")).build());
        FirebaseMessaging.getInstance().getToken().addOnSuccessListener(token -> MobilePushSupport.registerToken(context, "fcm", token));
    }
}

package cc.luoluoluo.yanzi.mobile;
import android.util.Log;
import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

public final class FirebasePushService extends FirebaseMessagingService {
    @Override public void onNewToken(String token) {
        Log.i("YanziPush", "FCM refreshed token length=" + token.length());
        MobilePushSupport.registerToken(this, "fcm", token);
    }
    @Override public void onMessageReceived(RemoteMessage message) {
        Log.i("YanziPush", "FCM message received type=" + message.getData().get("type"));
        if ("messages-ready".equals(message.getData().get("type"))) MobilePushSupport.receive(this, message.getData().get("messageId"));
    }
}

package cc.luoluoluo.yanzi.mobile;
import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

public final class FirebasePushService extends FirebaseMessagingService {
    @Override public void onNewToken(String token) { MobilePushSupport.registerToken(this, "fcm", token); }
    @Override public void onMessageReceived(RemoteMessage message) {
        if ("messages-ready".equals(message.getData().get("type"))) MobilePushSupport.receive(this, message.getData().get("messageId"));
    }
}

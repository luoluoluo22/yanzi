package cc.luoluoluo.yanzi.mobile;
import android.content.Context;
import org.json.JSONArray;
import java.util.Locale;

/** Chat persistence and message policy are independent of Activity lifetime. */
final class MobileChatController {
    static void append(Context context, String deviceId, String role, String kind, String content, long time) {
        if (isDevelopmentChatArtifact(kind, content)) return;
        boolean self = "self".equals(role);
        ChatHistoryStore.append(context, ChatHistoryStore.message(role, kind, content, time, "",
            self ? deviceId : "", self ? MobileDeviceIdentity.buildDeviceDisplayName() : ""));
    }
    static JSONArray load(Context context) { return ChatHistoryStore.load(context); }
    static void clear(Context context) { ChatHistoryStore.clear(context); }
    static boolean isDevelopmentChatArtifact(String kind, String content) {
        if (content == null) return false;
        String value = content.trim();
        if ("text".equals(kind)) {
            return value.startsWith("public-chat-test-")
                    || value.startsWith("chat-e2e-test-")
                    || value.startsWith("yanzi-chat-test-");
        }
        if ("file".equals(kind) || "photo".equals(kind)) {
            String lower = value.toLowerCase(Locale.ROOT);
            return lower.endsWith("public-verification.bin")
                    || lower.endsWith("public-verification.png")
                    || lower.endsWith("phone-verification.bin")
                    || lower.endsWith("phone-verification.png")
                    || lower.endsWith("verification-attachment");
        }
        return false;
    }

}

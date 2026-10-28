package cc.luoluoluo.yanzi.mobile;

import org.json.JSONArray;
import org.json.JSONObject;
import java.util.ArrayList;
import java.util.List;

/** Local conversation identity; the server still authorizes all device routing. */
final class ChatConversation {
    static final String GROUP = "group";
    final String id, name, targetDeviceId, platform;
    final boolean agent;
    ChatConversation(String id, String name, String target, String platform, boolean agent) {
        this.id=id; this.name=name; this.targetDeviceId=target; this.platform=platform; this.agent=agent;
    }
    static ChatConversation group() { return new ChatConversation(GROUP,"群聊","","",false); }
    static List<ChatConversation> contacts(JSONArray devices, String self) {
        List<ChatConversation> result=new ArrayList<>(); result.add(group());
        if(devices==null) return result;
        for(int i=0;i<devices.length();i++) {
            JSONObject d=devices.optJSONObject(i); if(d==null) continue;
            String id=d.optString("deviceId"), platform=d.optString("platform");
            if(id.isEmpty() || id.equals(self) || !(platform.equals("desktop") || platform.equals("android"))) continue;
            String name=d.optString("displayName",id);
            if(platform.equals("desktop")) result.add(new ChatConversation("agent:antigravity:"+id,
                "反重力 · "+name,id,platform,true));
            result.add(new ChatConversation("device:"+id,name,id,platform,false));
        }
        return result;
    }
    static String incoming(JSONObject message) {
        JSONObject payload=message.optJSONObject("payload");
        String id=payload==null?"":payload.optString("conversationId");
        String source=message.optString("sourceDeviceId");
        if(id.startsWith("agent:")) return id;
        if(GROUP.equals(id)) return GROUP;
        // A server notification targets this phone for delivery, not a private chat.
        // Older server senders omitted conversationId, which hid messages under
        // device:server-* even though the server is not a selectable chat contact.
        if (id.isEmpty() && ((payload != null
                && "server".equals(payload.optString("source"))
                && "notification".equals(payload.optString("purpose")))
                || (source.startsWith("server-") && "YanziChat".equals(message.optString("title")))))
            return GROUP;
        // Compatibility with the first local adapter, which used plain chat.send.
        if((message.optString("text").startsWith("【反重力】") || message.optString("requestId").startsWith("agy-"))
                && source.startsWith("desktop-")
                && !message.optString("targetDeviceId").isEmpty() && !(payload!=null && payload.optBoolean("accountChat"))) {
            String reqId = message.optString("requestId");
            if (reqId.contains("--conv--")) {
                try {
                    String[] parts = reqId.split("--conv--");
                    if (parts.length > 1) {
                        String rawId = parts[1].split("--")[0].trim();
                        if (!rawId.isEmpty() && !rawId.equals("def")) {
                            return "agent:antigravity:" + source + ":" + rawId;
                        }
                    }
                } catch (Exception ignored) {}
            }
            try {
                android.content.Context ctx = MobileApplicationContext.get();
                if (ctx != null) {
                    String key = "chatObjects." + ChatHistoryStore.accountKey(ctx) + ".last_selected_conversation_id";
                    android.content.SharedPreferences sp = ctx.getSharedPreferences("yanzi-mobile", android.content.Context.MODE_PRIVATE);
                    String last = sp.getString(key, "");
                    if (last.isEmpty()) {
                        sp = ctx.getSharedPreferences("YanziMobilePrefs", android.content.Context.MODE_PRIVATE);
                        last = sp.getString(key, "");
                    }
                    if (last.startsWith("agent:antigravity:" + source)) return last;
                }
            } catch (Exception ignored) {}
            return "agent:antigravity:"+source;
        }
        if(id.startsWith("device:") || (!message.optString("targetDeviceId").isEmpty()
                && !(payload!=null && payload.optBoolean("accountChat"))))
            return source.isEmpty()?GROUP:"device:"+source;
        return GROUP;
    }
    JSONObject envelope(String source, String sourceName, String text) throws Exception {
        JSONObject payload=new JSONObject().put("sourceDeviceName",sourceName)
            .put("conversationId",id).put("createdAt",System.currentTimeMillis());
        JSONObject result=new JSONObject().put("sourceDeviceId",source).put("title","YanziChat")
            .put("kind","text").put("text",text).put("payload",payload);
        if(id.equals(GROUP)) result.put("routing","account-chat");
        else {
            result.put("targetDeviceId",targetDeviceId).put("targetPlatform",platform);
            if(agent) {
                payload.put("extensionId","antigravity-chat");
                result.put("kind","run-extension").put("text",new JSONObject()
                    .put("prompt",text).put("replyDeviceId",source).put("conversationId",id).toString());
            }
        }
        return result;
    }
}

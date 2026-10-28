package cc.luoluoluo.yanzi.mobile;
import android.app.*;
import android.os.Bundle;
import android.widget.EditText;
import org.json.*;

/** Explicit live Dev test: preserves the existing account and sends one prompt to its PC. */
final class ChatObjectsLiveVerification {
    static void run(Instrumentation test,Bundle args) {
        Bundle output=new Bundle();
        try {
            android.content.Context c=test.getTargetContext();
            if(!c.getPackageName().endsWith(".dev"))throw new AssertionError("Dev only");
            if(!android.os.Build.MODEL.toLowerCase().contains("sdk") && !"true".equals(args.getString("allowPhysicalDev")))
                throw new AssertionError("Physical Dev requires explicit flag");
            MobileSessionStore.Snapshot session=MobileSessionStore.snapshot(c);
            if(session.token.isEmpty())throw new AssertionError("Dev 当前未登录，未发送测试消息。");
            JSONArray devices=MobileMessageClient.readSystemFirst(session.baseUrl,"/v1/me/devices",session.token).getJSONArray("items");
            ChatConversation target=ChatConversation.contacts(devices,session.deviceId).stream()
                .filter(t->t.agent && t.targetDeviceId.equals(args.getString("desktopId"))).findFirst()
                .orElseThrow(()->new AssertionError("未找到指定电脑，未发送测试消息。"));
            String marker="YZCHAT"+System.currentTimeMillis();
            MainActivity a=(MainActivity)test.startActivitySync(new android.content.Intent(c,MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
            test.waitForIdleSync();
            test.runOnMainSync(()->{
                try {
                    java.lang.reflect.Method select=MainActivity.class.getDeclaredMethod("selectChatConversation",ChatConversation.class);select.setAccessible(true);select.invoke(a,target);
                    java.lang.reflect.Field input=MainActivity.class.getDeclaredField("chatInputEditText");input.setAccessible(true);
                    ((EditText)input.get(a)).setText("手机聊天对象接入验证。只回复 "+marker+"，不要使用工具。");
                    java.lang.reflect.Method send=MainActivity.class.getDeclaredMethod("handleSendChatMessageClick");send.setAccessible(true);send.invoke(a);
                } catch(Exception e){throw new AssertionError(e);}
            });
            long deadline=android.os.SystemClock.elapsedRealtime()+100000;
            boolean received=false;
            String failure="";
            while(android.os.SystemClock.elapsedRealtime()<deadline) {
                JSONArray history=ChatHistoryStore.load(c,target.id);
                for(int i=0;i<history.length();i++) {
                    JSONObject row=history.getJSONObject(i);
                    if(row.getString("role").equals("peer") && row.getString("content").contains(marker)) received=true;
                    if(row.getString("role").equals("system") && row.optLong("time")>System.currentTimeMillis()-100000)
                        failure=row.getString("content");
                }
                if(received || !failure.isEmpty()) break;
                Thread.sleep(1000);
            }
            if(!received)throw new AssertionError(failure.isEmpty()?"未在100秒内收到对应反重力会话的回复":failure);
            output.putString("stream","CHAT_OBJECTS_LIVE=PASSED (actual phone chat input → chosen desktop → CLI → same phone agent conversation)");
            test.finish(Activity.RESULT_OK,output);
        }catch(Throwable e){output.putString("stream",android.util.Log.getStackTraceString(e));test.finish(Activity.RESULT_CANCELED,output);}
    }
}

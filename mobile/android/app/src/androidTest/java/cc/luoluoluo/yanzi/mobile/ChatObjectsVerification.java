package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.database.sqlite.SQLiteDatabase;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.TextView;
import org.json.*;
import java.util.List;

/** Isolated emulator fixture: upgrades the old DB and exercises real Activity switching. */
final class ChatObjectsVerification {
    static void run(Instrumentation test) {
        Bundle result=new Bundle();
        try {
            Context c=test.getTargetContext();
            require(c.getPackageName().endsWith(".dev") && android.os.Build.MODEL.toLowerCase().contains("sdk"),"Dev emulator only");
            c.getSharedPreferences("yanzi-mobile",0).edit().clear().putString("token",fixtureToken("chat-objects-fixture"))
                .putString("baseUrl","http://127.0.0.1:1").commit();
            c.deleteDatabase("yanzi-chat-history.db");
            String account=ChatHistoryStore.accountKey(c);
            try(SQLiteDatabase db=c.openOrCreateDatabase("yanzi-chat-history.db",0,null)) {
                db.execSQL("CREATE TABLE chat_messages (id INTEGER PRIMARY KEY AUTOINCREMENT,account_id TEXT NOT NULL,message_id TEXT,role TEXT NOT NULL,kind TEXT NOT NULL,content TEXT NOT NULL,time_ms INTEGER NOT NULL,source_device_id TEXT,source_device_name TEXT,UNIQUE(account_id,message_id) ON CONFLICT IGNORE)");
                db.execSQL("INSERT INTO chat_messages(account_id,message_id,role,kind,content,time_ms) VALUES(?, 'legacy', 'peer', 'text', '旧群聊保留', 1)",new Object[]{account});
                db.setVersion(1);
            }
            require(ChatHistoryStore.load(c).getJSONObject(0).getString("content").equals("旧群聊保留"),"Version 1 history migrated to group");
            JSONArray devices=new JSONArray().put(new JSONObject().put("deviceId","desktop-a").put("platform","desktop").put("displayName","开发电脑"))
                .put(new JSONObject().put("deviceId","android-self").put("platform","android"))
                .put(new JSONObject().put("deviceId","android-other").put("platform","android").put("displayName","另一手机"));
            List<ChatConversation> targets=ChatConversation.contacts(devices,"android-self");
            require(targets.size()==4,"Group, agent, computer and other phone");
            ChatConversation agent=targets.get(1), computer=targets.get(2), phone=targets.get(3);
            JSONObject request=agent.envelope("android-self","我的手机","测试问题");
            require(request.getString("targetDeviceId").equals("desktop-a") && request.getString("kind").equals("run-extension"),"Agent explicitly routes to chosen PC");
            require(new JSONObject(request.getString("text")).getString("replyDeviceId").equals("android-self"),"Reply routes to initiating phone");
            require(phone.envelope("android-self","我的手机","hello").getString("targetPlatform").equals("android"),"Direct phone routing");
            require(ChatConversation.group().envelope("android-self","我的手机","hello").getString("routing").equals("account-chat"),"Group routing preserved");
            require(ChatConversation.incoming(new JSONObject().put("sourceDeviceId","desktop-a").put("targetDeviceId","android-self"))
                .equals(computer.id),"Direct receiver identifies sender");
            require(ChatConversation.incoming(new JSONObject().put("sourceDeviceId","desktop-a").put("payload",new JSONObject().put("conversationId",agent.id))).equals(agent.id),"Agent metadata selects agent thread");
            require(ChatConversation.incoming(new JSONObject().put("sourceDeviceId","desktop-a").put("targetDeviceId","android-self").put("text","【反重力】\n旧宿主回复")).equals(agent.id),"Old adapter reply compatibility");
            for(ChatConversation target:new ChatConversation[]{agent,computer,phone})
                ChatHistoryStore.append(c,ChatHistoryStore.message("peer","text",target.name+"的私有记录",2,target.id,"desktop-a","电脑").put("conversationId",target.id));
            require(ChatHistoryStore.load(c,agent.id).length()==1 && ChatHistoryStore.load(c).length()==1,"Histories are disjoint");
            require(ChatHistoryStore.unread(c,phone.id,0)==1,"Unread counts are scoped");
            MainActivity a=(MainActivity)test.startActivitySync(new android.content.Intent(c,MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
            test.waitForIdleSync();
            test.runOnMainSync(()->{
                require(containsText(a.getWindow().getDecorView(),"群聊"),"Default label is group");
                require(containsDescription(a.getWindow().getDecorView(),"切换聊天对象"),"Arrow has accessible name");
                EditText input=(EditText)field(a,"chatInputEditText"); input.setText("群聊草稿");
                call(a,"selectChatConversation",agent);
                require(input.getText().length()==0,"Agent has independent draft");
                require(containsText(a.getWindow().getDecorView(),"反重力"),"Object name replaces chat label");
                require(((View)field(a,"chatAttachBtn")).getVisibility()==View.GONE,"Agent hides unsupported attachment picker");
                input.setText("反重力草稿");
                call(a,"selectChatConversation",ChatConversation.group());
                require(input.getText().toString().equals("群聊草稿"),"Group draft restored");
                call(a,"selectChatConversation",agent);
                require(input.getText().toString().equals("反重力草稿"),"Agent draft restored");
                require(!containsText((View)field(a,"chatMessageListLayout"),"旧群聊保留"),"Group history never leaks into agent view");
                call(a,"selectChatConversation",computer);
                require(((View)field(a,"chatAttachBtn")).getVisibility()==View.VISIBLE,"Device attachment picker restored");
            });
            test.runOnMainSync(()->call(a,"selectChatConversation",agent));
            test.waitForIdleSync();
            test.runOnMainSync(()->{
                android.view.View decor=a.getWindow().getDecorView();
                android.graphics.Bitmap screenshot=android.graphics.Bitmap.createBitmap(decor.getWidth(),decor.getHeight(),android.graphics.Bitmap.Config.ARGB_8888);
                decor.draw(new android.graphics.Canvas(screenshot));
                try(java.io.FileOutputStream file=new java.io.FileOutputStream(new java.io.File(a.getFilesDir(),"chat-objects.png"))) {
                    screenshot.compress(android.graphics.Bitmap.CompressFormat.PNG,100,file);
                }catch(Exception e){throw new AssertionError(e);}
                screenshot.recycle();a.finish();
            });
            ChatHistoryStore.clearConversation(c,agent.id);
            require(ChatHistoryStore.load(c,computer.id).length()==1 && ChatHistoryStore.load(c).length()==1,"Clear only selected conversation");
            c.getSharedPreferences("yanzi-mobile",0).edit().putString("token",fixtureToken("other-account")).commit();
            require(ChatHistoryStore.load(c,computer.id).length()==0,"Account switch isolates threads");
            c.getSharedPreferences("yanzi-mobile",0).edit().clear().commit();
            result.putString("stream","CHAT_OBJECTS=PASSED (migration, group/device/agent routing, source identity, history isolation, unread, drafts, native arrow, attachment visibility, account isolation)");
            test.finish(Activity.RESULT_OK,result);
        } catch(Throwable e) { result.putString("stream",android.util.Log.getStackTraceString(e)); test.finish(Activity.RESULT_CANCELED,result); }
    }
    private static Object field(Object object,String name) {
        try { java.lang.reflect.Field f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);return f.get(object); }
        catch(Exception e){throw new AssertionError(e);}
    }
    private static void call(Object object,String name,ChatConversation argument) {
        try { java.lang.reflect.Method m=MainActivity.class.getDeclaredMethod(name,ChatConversation.class);m.setAccessible(true);m.invoke(object,argument); }
        catch(Exception e){throw new AssertionError(e);}
    }
    private static boolean containsText(View v,String text) {
        if(v instanceof TextView && ((TextView)v).getText().toString().equals(text)) return true;
        if(v instanceof ViewGroup) for(int i=0;i<((ViewGroup)v).getChildCount();i++) if(containsText(((ViewGroup)v).getChildAt(i),text)) return true;
        return false;
    }
    private static boolean containsDescription(View v,String text) {
        if(text.contentEquals(v.getContentDescription()==null?"":v.getContentDescription()))return true;
        if(v instanceof ViewGroup)for(int i=0;i<((ViewGroup)v).getChildCount();i++)if(containsDescription(((ViewGroup)v).getChildAt(i),text))return true;
        return false;
    }
    private static void require(boolean value,String label){if(!value)throw new AssertionError(label);}
    private static String fixtureToken(String account) {
        return "e30."+android.util.Base64.encodeToString(("{\"sub\":\""+account+"\"}").getBytes(java.nio.charset.StandardCharsets.UTF_8),
            android.util.Base64.URL_SAFE|android.util.Base64.NO_WRAP|android.util.Base64.NO_PADDING)+".fixture";
    }
}

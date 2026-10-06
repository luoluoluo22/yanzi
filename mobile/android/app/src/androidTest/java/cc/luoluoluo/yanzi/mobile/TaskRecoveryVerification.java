package cc.luoluoluo.yanzi.mobile;
import android.app.*;
import android.os.Bundle;
import android.content.*;
import org.json.*;
import java.util.*;

public final class TaskRecoveryVerification {
    static void require(boolean ok,String name){if(!ok)throw new AssertionError(name);}
    static String token(String user)throws Exception{return "e30."+android.util.Base64.encodeToString(new JSONObject().put("sub",user).toString().getBytes("UTF-8"),11)+".fixture";}
    static void queryOriginal(Instrumentation test,Bundle arguments) {
        Bundle result=new Bundle();try {
            require(test.getTargetContext().getPackageName().endsWith(".dev")&&"true".equals(arguments.getString("allowPhysicalDev")),"Dev only");
            MobileSessionStore.Snapshot session=MobileSessionStore.snapshot(test.getTargetContext());session.requireCurrent();
            String id=arguments.getString("messageId","");require(id.matches("msg_[a-f0-9]+"),"Existing cloud message required");
            JSONObject response=MobileMessageClient.requestWithoutQueue(session.baseUrl,"/v1/me/mobile/messages/"+id,session.token,"GET",null);
            session.requireCurrent();boolean found=false;
            for(JSONObject record:MobileTaskJournal.list(session.baseUrl,session.token)) if(id.equals(record.optString("messageId"))&&record.optString("status").equals(response.optString("status"))) found=true;
            require(found,"Original server result is persisted under original operation");
            result.putString("stream","ORIGINAL_TASK_QUERY=PASSED status="+response.optString("status")+" (GET only, same message, persisted receipt)");test.finish(Activity.RESULT_OK,result);
        }catch(Throwable error){result.putString("stream",android.util.Log.getStackTraceString(error));test.finish(Activity.RESULT_CANCELED,result);}
    }
    static void verifyUi(Instrumentation test,Bundle arguments) {
        Bundle result=new Bundle();try {
            require(test.getTargetContext().getPackageName().endsWith(".dev")&&"true".equals(arguments.getString("allowPhysicalDev")),"Explicit Dev only");
            TaskRecoveryActivity screen=(TaskRecoveryActivity)test.startActivitySync(new Intent(test.getTargetContext(),TaskRecoveryActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            test.waitForIdleSync();android.os.SystemClock.sleep(500);
            test.runOnMainSync(()->{require(PhoneWorkspaceVerification.contains(screen.getWindow().getDecorView(),"任务中心"),"Task tab");require(PhoneWorkspaceVerification.contains(screen.getWindow().getDecorView(),"数据恢复"),"Recovery tab");});
            result.putString("stream","TASK_CENTER_NATIVE_UI=PASSED (read-only Dev entry)");test.finish(Activity.RESULT_OK,result);
        }catch(Throwable error){result.putString("stream",android.util.Log.getStackTraceString(error));test.finish(Activity.RESULT_CANCELED,result);}
    }
    static void verifyRestart(Instrumentation test) {
        Bundle result=new Bundle();try {
            Context context=test.getTargetContext();require(context.getPackageName().endsWith(".dev")&&android.os.Build.MODEL.toLowerCase().contains("sdk"),"Emulator only");
            SharedPreferences fixture=context.getSharedPreferences("task-verification",0);
            require(fixture.getInt("processId",0)!=android.os.Process.myPid(),"Second process required");
            JSONObject receipt=MobileTaskJournal.list(fixture.getString("base",""),fixture.getString("account","")).get(0);
            require(receipt.getString("status").equals("completed")&&receipt.getString("result").equals("original result"),"Restart preserves original result");
            result.putString("stream","TASK_RESTART=PASSED (new process, persisted result, original task)");test.finish(Activity.RESULT_OK,result);
        } catch(Throwable error){result.putString("stream",android.util.Log.getStackTraceString(error));test.finish(Activity.RESULT_CANCELED,result);}
    }
    static void run(Instrumentation test){Bundle result=new Bundle();try{
        Context context=test.getTargetContext();require(context.getPackageName().endsWith(".dev")&&android.os.Build.MODEL.toLowerCase().contains("sdk"),"Isolated emulator only");
        String base="https://task-fixture.invalid",account=token("task-recovery-"+UUID.randomUUID()),other=token("other-"+UUID.randomUUID());
        String id=UUID.randomUUID().toString();JSONObject envelope=new JSONObject().put("clientMessageId",id).put("kind","run-extension").put("targetDeviceId","original-desktop").put("title","恢复验证任务");
        MobileTaskJournal.saved(base,account,envelope);MobileTaskJournal.saved(base,account,envelope);
        require(MobileTaskJournal.list(base,account).size()==1,"Stable operation is not duplicated");
        MobileTaskJournal.transportError(base,account,envelope,new java.io.IOException("timeout"));
        require(MobileTaskJournal.list(base,account).get(0).getString("status").equals("unknown"),"Timeout is uncertain, not execution failure");
        MobileTaskJournal.accepted(base,account,envelope,new JSONObject().put("messageId","msg_fixture"));
        MobileTaskJournal.observed(base,account,"msg_fixture",new JSONObject().put("status","completed").put("payload",new JSONObject().put("executionResult",new JSONObject().put("output","original result"))));
        MobileTaskJournal.accepted(base,account,envelope,new JSONObject().put("messageId","msg_other").put("status","pending"));
        JSONObject persisted=MobileTaskJournal.list(base,account).get(0);
        require(persisted.getString("status").equals("completed")&&persisted.getString("messageId").equals("msg_fixture"),"Late receipt cannot regress terminal task");
        context.getSharedPreferences("task-verification",0).edit().putString("base",base).putString("account",account).putInt("processId",android.os.Process.myPid()).commit();
        require(persisted.getString("target").equals("original-desktop")&&persisted.getString("result").equals("original result"),"Original target and result survive disk reload");
        require(MobileTaskJournal.list(base,other).isEmpty()&&MobileTaskJournal.list("https://other-server.invalid",account).isEmpty(),"Account and server isolation");
        final int[] writes={0};final long[] observedRevision={7};final List<String> calls=new ArrayList<>();
        MobileDataRecovery recovery=new MobileDataRecovery((method,path,body)->{
            calls.add(method+" "+path);
            if(path.equals("/v1/sync/objects"))return new JSONObject().put("ok",true).put("userId","fixture").put("hasMore",true).put("cursorRevision",7).put("objects",new JSONArray().put(new JSONObject().put("objectId","extensionData.fixture").put("revision",7).put("deleted",true)));
            if(path.startsWith("/v1/sync/changes"))return new JSONObject().put("ok",true).put("userId","fixture").put("hasMore",false).put("objects",new JSONArray().put(new JSONObject().put("objectId","yanm.layout").put("revision",8)));
            if(path.contains("/history"))return new JSONObject().put("versions",new JSONArray().put(new JSONObject().put("revision",4).put("deleted",false).put("payload",new JSONObject().put("content","old note"))));
            if(method.equals("POST")){writes[0]++;require(body.getLong("expectedRevision")==7&&body.getLong("restoreRevision")==4,"Restore preserves previewed CAS revision");if(observedRevision[0]!=7)throw new MobileMessageClient.HttpFailure(409);return new JSONObject().put("object",new JSONObject().put("revision",9).put("deleted",false).put("payload",new JSONObject().put("content","old note")));}
            return new JSONObject().put("object",new JSONObject().put("revision",7).put("deleted",true));
        });
        require(recovery.objects().size()==2,"Recovery includes tombstones and paginated objects");
        require(recovery.history("extensionData.fixture",0).getJSONArray("versions").getJSONObject(0).getJSONObject("payload").getString("content").equals("old note"),"History includes content");
        require(recovery.restore("extensionData.fixture",7,4,"fixture-phone").getLong("revision")==9,"Deleted data restored as a new version");
        observedRevision[0]=8;try{recovery.restore("extensionData.fixture",7,4,"fixture-phone");throw new AssertionError("Conflict must reject restore");}catch(MobileMessageClient.HttpFailure expected){require(expected.status==409,"409 conflict preserved");}
        require(writes[0]==2,"No automatic restore retry on conflict");
        require(!CloudRequestRetry.safe("POST","/v1/sync/objects/x/restore",false),"Ambiguous restore cannot auto-retry");
        SharedPreferences prefs=MobileSessionStore.preferences(context);String old=prefs.getString("token","");
        MobileSessionStore.Snapshot session=MobileSessionStore.snapshot(context);prefs.edit().putString("token","changed-fixture").commit();
        try{session.requireCurrent();throw new AssertionError("Account change must block recovery");}catch(CloudRequestRetry.SessionChanged expected){}finally{prefs.edit().putString("token",old).commit();}
        TaskRecoveryActivity screen=(TaskRecoveryActivity)test.startActivitySync(new Intent(context,TaskRecoveryActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));test.waitForIdleSync();
        test.runOnMainSync(()->{require(PhoneWorkspaceVerification.contains(screen.getWindow().getDecorView(),"任务中心"),"Task center UI");require(PhoneWorkspaceVerification.contains(screen.getWindow().getDecorView(),"数据恢复"),"Recovery UI");screen.finish();});
        result.putString("stream","TASK_RECOVERY=PASSED (durability, deduplication, terminal receipts, account/server isolation, pagination, tombstones, history contents, CAS, no retry, session fence, native UI)");test.finish(Activity.RESULT_OK,result);
    }catch(Throwable error){result.putString("stream",android.util.Log.getStackTraceString(error));test.finish(Activity.RESULT_CANCELED,result);}}
}

package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Intent;
import android.os.Bundle;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONObject;

/** UI-only fixture: no downloads, installs, preference changes or account mutations. */
public final class ApplicationStoreVerification {
    static Object field(Object owner,String name)throws Exception{
        java.lang.reflect.Field f=owner.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(owner);
    }
    static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    static void run(Instrumentation instrumentation){
        Bundle result=new Bundle();Activity activity=null;
        try{
            android.content.Context context=instrumentation.getTargetContext();
            require(context.getPackageName().endsWith(".dev"),"Dev only");
            require(android.os.Build.HARDWARE.contains("ranchu")||android.os.Build.HARDWARE.contains("goldfish"),"Emulator only");
            require(context.getSharedPreferences("yanzi-mobile",0).getString("token","").isEmpty(),"Requires signed-out emulator; will not change account");
            activity=instrumentation.startActivitySync(new Intent(context,ApplicationCatalogActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            final Activity screen=activity;
            final Object store=field(screen,"store");
            final Throwable[] failure=new Throwable[1];
            instrumentation.runOnMainSync(()->{try{
                JSONArray apps=new JSONArray();
                String[] ids={"yanzi-files","yanzi-stream","yanzi-cards","yanzi-notes"};
                String[] names={"文件","燕流","燕记","笔记"};
                for(int i=0;i<ids.length;i++)apps.put(new JSONObject().put("applicationId",ids[i]).put("name",names[i]).put("description",i==0?"目录浏览与多选管理":"图文创作与草稿").put("kind","android-apk").put("packageName","fixture.not.installed."+i).put("version","0.1.0").put("versionCode",1).put("size",24000));
                JSONArray selections=new JSONArray().put(new JSONObject().put("applicationId","yanzi-cards").put("enabled",true));
                java.lang.reflect.Method render=ApplicationCatalogView.class.getDeclaredMethod("render",JSONArray.class,JSONArray.class);render.setAccessible(true);render.invoke(store,apps,selections);
                LinearLayout list=(LinearLayout)field(store,"list");
                require(list.getChildAt(0) instanceof android.widget.HorizontalScrollView,"Recommendation rail");
                require(list.getChildCount()==10,"Four compact list rows plus rail/heading");
                @SuppressWarnings("unchecked") java.util.Map<String,TextView> tabs=(java.util.Map<String,TextView>)field(store,"tabs");
                tabs.get("工具").performClick();require(list.getChildCount()==3&&((TextView)list.getChildAt(0)).getText().toString().contains("1"),"Tools filters actual rows");
                tabs.get("图文").performClick();require(list.getChildCount()==7,"Content filters three rows");
                EditText search=(EditText)field(store,"search");search.setText("燕记");require(list.getChildCount()==3,"Search combines category");
                search.setText("不存在");require(list.getChildCount()==1&&((TextView)list.getChildAt(0)).getText().toString().contains("没有找到"),"Search empty state");
                search.setText("");tabs.get("我的").performClick();require(list.getChildCount()==3,"Library selection filters acquired apps");
                tabs.get("推荐").performClick();require(list.getChildCount()==10,"Return restores rail and all rows");
            }catch(Throwable e){failure[0]=e;}});
            if(failure[0]!=null)throw new AssertionError(failure[0]);
            result.putString("stream","APPLICATION_STORE=PASSED (recommendations, categories, search, empty state, library)");
            instrumentation.runOnMainSync(activity::finish);
            instrumentation.finish(Activity.RESULT_OK,result);
        }catch(Throwable e){if(activity!=null){final Activity failed=activity;instrumentation.runOnMainSync(failed::finish);}result.putString("stream",android.util.Log.getStackTraceString(e));instrumentation.finish(Activity.RESULT_CANCELED,result);}
    }
}

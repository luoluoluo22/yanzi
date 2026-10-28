package cc.luoluoluo.yanzi.mobile;

import android.app.*;
import android.content.*;
import android.os.Bundle;
import android.widget.*;
import org.json.*;
import java.util.concurrent.*;

/** Re-fetch pending requests with the owner's session; notification extras never authorize access. */
public final class ExternalAccessActivity extends Activity {
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private SharedPreferences prefs;private String token,base,id;private TextView status;
    private final java.util.List<CheckBox> choices=new java.util.ArrayList<>();
    private JSONArray requestedScopes=new JSONArray();
    @Override public void onCreate(Bundle state){super.onCreate(state);
        prefs=getSharedPreferences("yanzi-mobile",MODE_PRIVATE);token=prefs.getString("token","");base=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd");id=getIntent().getStringExtra("requestId");
        status=new TextView(this);status.setPadding(32,32,32,32);status.setText("读取待确认授权…");setContentView(status);
        worker.execute(()->{try{JSONObject result=MobileMessageClient.request(base,"/v1/applications/access-requests",token,"GET",null);check();
            JSONArray rows=result.getJSONArray("requests");JSONObject found=null;for(int i=0;i<rows.length();i++)if(id.equals(rows.getJSONObject(i).optString("requestId")))found=rows.getJSONObject(i);
            if(found==null)throw new IllegalStateException("申请已处理或过期");final JSONObject row=found;
            runOnUiThread(()->{if(isFinishing())return;
                LinearLayout list=new LinearLayout(this);list.setOrientation(1);requestedScopes=row.optJSONArray("scopes");if(requestedScopes==null)requestedScopes=new JSONArray();
                for(int i=0;i<requestedScopes.length();i++){JSONObject scope=requestedScopes.optJSONObject(i);if(scope==null)continue;CheckBox choice=new CheckBox(this);choice.setChecked(true);choice.setText(scope.optString("name",scope.optString("extensionId"))+" / "+scope.optString("key")+" · "+("read-write".equals(scope.optString("access"))?"增删改查":"只读"));choices.add(choice);list.addView(choice);}
                ScrollView scroll=new ScrollView(this);scroll.addView(list);
                new AlertDialog.Builder(this).setTitle("是否允许外部应用访问？")
                    .setMessage("申请方（自行填写）："+row.optString("clientName")+"\n核对码："+row.optString("userCode")+"\n小程序："+row.optString("extensionId")+"\n数据："+row.optString("dataKey")+"\n权限："+("read-write".equals(row.optString("access"))?"读取、新增、修改、删除":"仅读取")+"\n有效期：1 小时。请与 AI 返回的核对码核对。")
                    .setView(scroll).setPositiveButton("允许所选",(d,w)->decide(true)).setNegativeButton("拒绝",(d,w)->decide(false)).setOnCancelListener(d->finish()).show();
            });
        }catch(Exception e){runOnUiThread(()->status.setText("无法处理："+e.getMessage()));}});
    }
    private void check(){if(!token.equals(prefs.getString("token","")))throw new IllegalStateException("账号已切换，请重新打开授权申请");}
    private void decide(boolean allow){JSONArray selected=new JSONArray();for(int i=0;i<choices.size();i++)if(choices.get(i).isChecked())selected.put(requestedScopes.optJSONObject(i));worker.execute(()->{try{check();MobileMessageClient.request(base,"/v1/applications/access-requests/"+id+"/decision",token,"POST",new JSONObject().put("approve",allow).put("scopes",selected));check();runOnUiThread(()->{Toast.makeText(this,allow?"已允许，AI 可领取限时授权":"已拒绝",Toast.LENGTH_LONG).show();finish();});}
        catch(Exception e){runOnUiThread(()->status.setText("授权未完成："+e.getMessage()));}});}
    @Override public void onDestroy(){worker.shutdown();super.onDestroy();}
}

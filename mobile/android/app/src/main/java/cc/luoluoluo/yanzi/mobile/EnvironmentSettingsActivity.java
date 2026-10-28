package cc.luoluoluo.yanzi.mobile;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.os.*;
import android.provider.Settings;
import android.widget.*;
import org.json.*;

/** Generic native settings surface for an extension-owned environment task. */
public final class EnvironmentSettingsActivity extends Activity {
    private String id,accountScope;
    private TextView status;
    private boolean rendering;
    private interface Task { void run() throws Exception; }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        id=getIntent().getStringExtra("extensionId");
        try { accountScope=MobileEnvironment.scope(this,id); if(!MobileEnvironment.permitted(this,id))throw new IllegalStateException("小程序未声明环境权限"); render(); }
        catch(Exception e) { Toast.makeText(this,e.getMessage(),Toast.LENGTH_LONG).show(); finish(); }
    }
    private void validate() throws Exception { if(!accountScope.equals(MobileEnvironment.scope(this,id))) throw new IllegalStateException("账号已切换，请重新打开小程序"); }
    private void work(Task action) {
        work(action, null);
    }
    private void work(Task action, BusyButton button) {
        status.setText("处理中…定位最多等待 20 秒");
        MobileEnvironment.WORK.execute(()->{
            try { validate(); action.run(); runOnUiThread(this::refresh); }
            catch(Exception e) { runOnUiThread(()->{if(!isFinishing()){refresh();status.append("\n"+e.getMessage());}}); }
            finally { if(button!=null)runOnUiThread(button::finish); }
        });
    }
    private void taskButton(LinearLayout root,String title,Task action) {
        Button b=new Button(this);b.setText(title);BusyButton busy=new BusyButton(b);
        b.setOnClickListener(v->{if(busy.begin("正在检测…"))work(action,busy);});root.addView(b);
    }
    private void button(LinearLayout root,String title,Runnable action) {
        Button b=new Button(this); b.setText(title);b.setOnClickListener(v->action.run());root.addView(b);
    }
    private void render() throws Exception {
        rendering=true;
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setPadding(24,24,24,32);
        TextView title=new TextView(this);title.setText("位置与环境 · "+id);title.setTextSize(22);root.addView(title);
        TextView hint=new TextView(this);hint.setText("省电模式：目标每 15 分钟检测，系统可能延迟。仅同账号设备可读；默认不共享坐标。定位不能说明正在使用手机。");root.addView(hint);
        status=new TextView(this);status.setPadding(0,18,0,18);root.addView(status);
        JSONObject cfg=MobileEnvironment.config(this,id);
        Switch enabled=new Switch(this);enabled.setText("开启环境上报");enabled.setChecked(cfg.optBoolean("enabled"));root.addView(enabled);
        enabled.setOnCheckedChangeListener((b,on)->{if(rendering)return;
            try {validate();JSONObject settings=MobileEnvironment.config(this,id);settings.put("enabled",on);MobileEnvironment.saveConfig(this,id,settings);
                if(on && !MobileEnvironment.permission(this,false)) requestPermissions(new String[]{Manifest.permission.ACCESS_COARSE_LOCATION,Manifest.permission.ACCESS_FINE_LOCATION},8101);
                if(on) work(()->MobileEnvironment.collect(this,id,false,false)); else work(()->MobileEnvironment.disable(this,id));
            }catch(Exception e){status.setText(e.getMessage());}
        });
        Switch coordinates=new Switch(this);coordinates.setText("向同账号电脑共享精确坐标（可关闭）");coordinates.setChecked(cfg.optBoolean("shareCoordinates"));root.addView(coordinates);
        coordinates.setOnCheckedChangeListener((b,on)->{if(rendering)return;try{validate();JSONObject settings=MobileEnvironment.config(this,id);settings.put("shareCoordinates",on);MobileEnvironment.saveConfig(this,id,settings);
            // Replace any previously shared coordinates immediately; do not wait for a fresh location.
            if(!on && settings.optBoolean("enabled"))work(()->{JSONObject sample=new JSONObject().put("enabled",true).put("observedAt",java.time.Instant.now().toString())
                .put("place","unknown").put("confidence","unknown").put("availability","sharing_changed").put("network",MobileEnvironment.network(this));
                MobileEnvironment.queue(this,id,sample);MobileEnvironment.flush(this,id,accountScope,getSharedPreferences("yanzi-mobile",0).getString("token",""));});
        }catch(Exception e){status.setText(e.getMessage());}});
        button(root,"授权前台定位",()->requestPermissions(new String[]{Manifest.permission.ACCESS_COARSE_LOCATION,Manifest.permission.ACCESS_FINE_LOCATION},8101));
        button(root,"允许后台检测",()->{
            if(!MobileEnvironment.permission(this,false)){status.setText("请先授权前台定位");return;}
            new AlertDialog.Builder(this).setTitle("后台定位说明").setMessage("启用后，位置小程序在后台尝试每 15 分钟检测环境。可随时关闭；系统可能延迟。请在系统权限页面选择始终允许定位。")
                .setPositiveButton("打开系统设置",(d,w)->startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,android.net.Uri.parse("package:"+getPackageName())))).setNegativeButton("取消",null).show();
        });
        taskButton(root,"立即检测并上报",()->MobileEnvironment.collect(this,id,false,false));
        taskButton(root,"将当前位置设为家",()->MobileEnvironment.collect(this,id,false,true));
        EditText radius=new EditText(this);radius.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);radius.setHint("家的范围（米，50—5000）");radius.setText(String.valueOf(cfg.optInt("radius",200)));root.addView(radius);
        button(root,"保存家的范围",()->{try{validate();int metres=Integer.parseInt(radius.getText().toString());if(metres<50||metres>5000)throw new IllegalArgumentException("范围须为 50—5000 米");JSONObject settings=MobileEnvironment.config(this,id);settings.put("radius",metres);MobileEnvironment.saveConfig(this,id,settings);refresh();}catch(Exception e){status.setText(e.getMessage());}});
        button(root,"删除家的位置",()->{try{validate();JSONObject settings=MobileEnvironment.config(this,id);settings.remove("homeLatitude");settings.remove("homeLongitude");MobileEnvironment.saveConfig(this,id,settings);refresh();}catch(Exception e){status.setText(e.getMessage());}});
        button(root,"停止并清除共享状态",()->{try{validate();JSONObject settings=MobileEnvironment.config(this,id);settings.put("enabled",false);MobileEnvironment.saveConfig(this,id,settings);work(()->MobileEnvironment.disable(this,id));enabled.setChecked(false);}catch(Exception e){status.setText(e.getMessage());}});
        button(root,"返回",this::finish);
        ScrollView scroll=new ScrollView(this);scroll.addView(root);setContentView(scroll);rendering=false;refresh();
    }
    private void refresh() {
        if(isFinishing())return;
        try{validate();JSONObject s=MobileEnvironment.status(this,id),sample=s.getJSONObject("sample"),cfg=s.getJSONObject("config");
            String place=sample.optString("place","unknown");
            boolean stale=sample.has("observedAt") && System.currentTimeMillis()-java.time.Instant.parse(sample.getString("observedAt")).toEpochMilli()>30*60*1000L;
            if(stale)place="unknown";
            status.setText("当前环境："+(place.equals("home")?"在家":place.equals("away")?"外出":"未知")
                +"\n最近采集："+sample.optString("observedAt","尚未采集")+"\n最近上报："+s.optString("reportedAt","尚未上报")
                +"\n前台权限："+s.optBoolean("foregroundPermission")+" · 后台权限："+s.optBoolean("backgroundPermission")
                +"\n家的位置："+(cfg.has("homeLatitude")?"已设置":"未设置")+"\n网络："+sample.optJSONObject("network")
                +"\n待同步："+s.optBoolean("pending")+"\n"+s.optString("error",""));
            if(stale)status.append("\n样本已过期，当前环境按未知处理");
        }catch(Exception e){status.setText(e.getMessage());}
    }
    @Override public void onRequestPermissionsResult(int r,String[] permissions,int[] results){super.onRequestPermissionsResult(r,permissions,results);refresh();try{if(MobileEnvironment.permission(this,false)&&MobileEnvironment.config(this,id).optBoolean("enabled"))work(()->MobileEnvironment.collect(this,id,false,false));}catch(Exception ignored){}}
    @Override public void onResume(){super.onResume();if(status!=null)refresh();}
}

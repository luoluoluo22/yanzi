package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.widget.*;
import androidx.core.content.FileProvider;
import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.concurrent.*;

/** Account selections are shared. APK installations remain device-local and user-confirmed. */
public final class ApplicationCatalogActivity extends Activity {
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private SharedPreferences prefs; private LinearLayout list; private TextView status;
    private String accountId,baseUrl,token; private File pendingApk;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);MobileNetworkRouting.initialize(this);
        prefs=getSharedPreferences("yanzi-mobile",MODE_PRIVATE);
        baseUrl=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$","");token=prefs.getString("token","");
        LinearLayout root=new LinearLayout(this);root.setOrientation(1);root.setPadding(24,24,24,24);
        TextView title=new TextView(this);title.setText("应用中心");title.setTextSize(24);root.addView(title);
        status=new TextView(this);status.setText("账号应用跨设备可见；安装由这台手机确认。");root.addView(status);
        Button refresh=new Button(this);refresh.setText("刷新应用");refresh.setOnClickListener(v->load());root.addView(refresh);
        Button authorize=new Button(this);authorize.setText("授权开发者应用访问数据");authorize.setOnClickListener(v->authorize());root.addView(authorize);
        ScrollView scroll=new ScrollView(this);list=new LinearLayout(this);list.setOrientation(1);scroll.addView(list);root.addView(scroll);setContentView(root);
        try{accountId=ExtensionStorageProvider.accountId(token);load();}catch(Exception e){status.setText("请先登录燕子账号。");}
    }
    private void checkAccount()throws Exception{
        if(!accountId.equals(ExtensionStorageProvider.accountId(prefs.getString("token",""))))throw new IllegalStateException("账号已切换，请重新打开应用中心");
    }
    private void authorize(){
        LinearLayout form=new LinearLayout(this);form.setOrientation(1);
        EditText extension=new EditText(this);extension.setHint("小程序 ID，例如 quick-notes");form.addView(extension);
        EditText client=new EditText(this);client.setHint("应用名称，例如我的网页便签");form.addView(client);
        CheckBox writable=new CheckBox(this);writable.setText("允许修改数据（默认只读）");form.addView(writable);
        new android.app.AlertDialog.Builder(this).setTitle("授权指定小程序数据").setMessage("此授权有效 1 小时，只开放所填写的小程序。确认后复制访问令牌给你信任的应用。").setView(form)
            .setNegativeButton("取消",null).setPositiveButton("授权",(dialog,which)->{
                String id=extension.getText().toString().trim(),clientName=client.getText().toString().trim(),access=writable.isChecked()?"read-write":"read";
                work(()->{
                JSONObject input=new JSONObject().put("userConsent",true).put("clientName",clientName).put("access",access);
                JSONObject grant=json("/v1/applications/"+Uri.encode(id)+"/grants",input,true,"POST");checkAccount();
                ui(()->new android.app.AlertDialog.Builder(this).setTitle("授权已创建").setMessage("有效期 1 小时。可复制专用令牌，或立即撤销这次授权。").setPositiveButton("复制专用令牌",(d,w)->{
                    android.content.ClipboardManager clipboard=(android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
                    android.content.ClipData clip=android.content.ClipData.newPlainText("燕子应用授权",grant.optString("accessToken"));
                    android.os.PersistableBundle extras=new android.os.PersistableBundle();extras.putBoolean("android.content.extra.IS_SENSITIVE",true);clip.getDescription().setExtras(extras);
                    clipboard.setPrimaryClip(clip);
                }).setNegativeButton("撤销",(d,w)->work(()->{json("/v1/applications/"+Uri.encode(id)+"/grants/"+grant.getString("grantId"),null,true,"DELETE");ui(()->status.setText("授权已撤销"));})).show());
            });}).show();
    }
    private interface Task{void run()throws Exception;}
    private void work(Task task){executor.execute(()->{try{checkAccount();task.run();}catch(Exception e){ui(()->status.setText("操作失败："+e.getMessage()));}});}
    private void ui(Runnable action){runOnUiThread(()->{if(!isFinishing()&&!isDestroyed())action.run();});}
    private void load(){if(accountId==null)return;work(()->{
        JSONObject catalog=json("/v1/applications/catalog",null,false),library=json("/v1/applications/library",null,true);
        checkAccount();ui(()->render(catalog.optJSONArray("applications"),library.optJSONArray("applications")));
    });}
    private void render(JSONArray apps,JSONArray selections){
        list.removeAllViews();status.setText("内嵌应用跨设备获取；独立应用在每台手机确认安装。");
        if(apps==null||apps.length()==0){status.setText("暂无已发布应用");return;}
        for(int i=0;i<apps.length();i++){
            JSONObject app=apps.optJSONObject(i);if(app==null)continue;
            String id=app.optString("applicationId"),kind=app.optString("kind");JSONObject selected=null;
            if(selections!=null)for(int j=0;j<selections.length();j++){JSONObject item=selections.optJSONObject(j);if(item!=null&&id.equals(item.optString("applicationId")))selected=item;}
            final JSONObject selection=selected;boolean enabled=selected!=null&&selected.optBoolean("enabled");
            TextView text=new TextView(this);text.setPadding(0,24,0,4);text.setText(app.optString("name")+" · "+app.optString("version")+"\n"+app.optString("description")+"\n"+(enabled?"已加入账号":"未加入账号"));list.addView(text);
            Intent launch="android-apk".equals(kind)?getPackageManager().getLaunchIntentForPackage(app.optString("packageName")):null;
            boolean current=false;if(launch!=null)try{current=getPackageManager().getPackageInfo(app.optString("packageName"),0).getLongVersionCode()>=app.optLong("versionCode");}catch(Exception ignored){}
            final boolean installedCurrent=current;final Intent launchIntent=launch;
            Button action=new Button(this);action.setText("mobile-js".equals(kind)?(enabled?"获取 / 更新应用":"获取并加入账号"):(current?"打开应用":"下载并安装"));
            action.setOnClickListener(v->{if(installedCurrent){startActivity(launchIntent);return;}action.setEnabled(false);work(()->{try{
                if("mobile-js".equals(kind))installDefinition(app);else if("android-apk".equals(kind))downloadApk(app);else throw new IllegalStateException("不支持的应用类型");
                if(!enabled)json("/v1/applications/library/"+id,new JSONObject().put("enabled",true).put("expectedRevision",selection==null?0:selection.optLong("revision")),true);
                checkAccount();ui(()->status.setText("已加入账号，其他设备刷新后可见。"));
                if("mobile-js".equals(kind))load();
            }finally{ui(()->action.setEnabled(true));}});});list.addView(action);
        }
    }
    private void installDefinition(JSONObject app)throws Exception{
        JSONObject definition=new JSONObject(new String(download(app,1024*1024),StandardCharsets.UTF_8));
        if(!app.getString("applicationId").equals(definition.optString("id")))throw new IllegalStateException("应用标识不匹配");checkAccount();
        MobileExtensionDefinitionSyncClient.upsert(baseUrl,token,prefs.getString("deviceId",""),android.os.Build.MODEL,definition);checkAccount();
        JSONArray local=new JSONArray(prefs.getString("mobileExtensions","[]")),next=new JSONArray();
        for(int i=0;i<local.length();i++){JSONObject item=local.optJSONObject(i);if(item!=null&&!definition.optString("id").equals(item.optString("id")))next.put(item);}
        next.put(definition);prefs.edit().putString("mobileExtensions",next.toString()).apply();
    }
    private void downloadApk(JSONObject app)throws Exception{
        if(BuildConfig.VERSION_CODE<app.optInt("minHostVersionCode",0))throw new IllegalStateException("请先更新燕子主应用");
        ui(()->status.setText("正在下载并校验…"));HttpURLConnection c=connection(assetUrl(app));
        File target=new File(getCacheDir(),"application-"+app.getString("applicationId")+".apk");MessageDigest digest=MessageDigest.getInstance("SHA-256");long count=0;
        try{
            if(c.getResponseCode()!=200)throw new IOException("下载服务不可用");
            try(InputStream in=c.getInputStream();FileOutputStream out=new FileOutputStream(target)){
                byte[] b=new byte[65536];int n;while((n=in.read(b))!=-1){count+=n;if(count>200L*1024*1024)throw new IOException("安装包过大");digest.update(b,0,n);out.write(b,0,n);}
            }
            if(count!=app.getLong("size")||!hex(digest.digest()).equals(app.getString("sha256")))throw new IOException("下载校验失败");
            PackageInfo apk=getPackageManager().getPackageArchiveInfo(target.getAbsolutePath(),android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES);
            if(apk==null||!apk.packageName.equals(app.getString("packageName"))||apk.getLongVersionCode()!=app.getLong("versionCode"))throw new IOException("安装包身份不匹配");
            String signer=hex(MessageDigest.getInstance("SHA-256").digest(apk.signingInfo.getApkContentsSigners()[0].toByteArray()));
            if(!signer.equals(app.getString("certificateSha256")))throw new IOException("应用签名不匹配");
            checkAccount();ui(()->install(target));
        }catch(Exception e){target.delete();throw e;}finally{c.disconnect();}
    }
    private void install(File apk){pendingApk=apk;
        if(!getPackageManager().canRequestPackageInstalls()){
            status.setText("请允许燕子安装应用，返回后继续系统安装确认。");startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName())));return;
        }
        pendingApk=null;Uri uri=FileProvider.getUriForFile(this,BuildConfig.APPLICATION_ID+".fileprovider",apk);
        startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
    }
    @Override protected void onResume(){super.onResume();if(pendingApk!=null&&getPackageManager().canRequestPackageInstalls())install(pendingApk);}
    @Override protected void onDestroy(){executor.shutdownNow();super.onDestroy();}
    private URL assetUrl(JSONObject app)throws Exception{
        URL url=new URL(new URL(baseUrl),app.getString("downloadPath")),base=new URL(baseUrl);
        if(!url.getProtocol().equals("https")||!url.getHost().equals(base.getHost())||url.getPort()!=base.getPort()||!url.getPath().startsWith("/downloads/applications/"))throw new IOException("下载地址无效");return url;
    }
    private static HttpURLConnection connection(URL url)throws Exception{
        HttpURLConnection c=MobileNetworkRouting.openCloudConnection(url);c.setInstanceFollowRedirects(false);c.setConnectTimeout(15000);c.setReadTimeout(30000);return c;
    }
    private byte[] download(JSONObject app,int limit)throws Exception{
        HttpURLConnection c=connection(assetUrl(app));try{if(c.getResponseCode()!=200)throw new IOException("下载失败");byte[] b=read(c.getInputStream(),limit);
            if(b.length!=app.getLong("size")||!hex(MessageDigest.getInstance("SHA-256").digest(b)).equals(app.getString("sha256")))throw new IOException("应用校验失败");return b;
        }finally{c.disconnect();}
    }
    private JSONObject json(String path,JSONObject input,boolean authenticated)throws Exception{
        return json(path,input,authenticated,input==null?"GET":"PUT");
    }
    private JSONObject json(String path,JSONObject input,boolean authenticated,String method)throws Exception{
        HttpURLConnection c=connection(new URL(baseUrl+path));if(authenticated)c.setRequestProperty("Authorization","Bearer "+token);
        c.setRequestMethod(method);
        try{if(input!=null){c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");try(OutputStream out=c.getOutputStream()){out.write(input.toString().getBytes(StandardCharsets.UTF_8));}}
            int code=c.getResponseCode();if(code!=200)throw new IOException(code==401?"登录已过期，请返回燕子重新登录":"服务返回 "+code);
            return new JSONObject(new String(read(c.getInputStream(),128*1024),StandardCharsets.UTF_8));
        }finally{c.disconnect();}
    }
    private static byte[] read(InputStream stream,int limit)throws Exception{
        try(InputStream in=stream;ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))!=-1){if(out.size()+n>limit)throw new IOException("响应过大");out.write(b,0,n);}return out.toByteArray();}
    }
    private static String hex(byte[] b){StringBuilder s=new StringBuilder();for(byte v:b)s.append(String.format(java.util.Locale.ROOT,"%02x",v&255));return s.toString();}
}

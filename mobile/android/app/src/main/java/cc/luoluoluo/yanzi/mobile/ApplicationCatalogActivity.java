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
    private BusyButton refreshBusy; private boolean working;
    private final java.util.List<TextView> actionButtons=new java.util.ArrayList<>();
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        MobileNetworkRouting.initialize(this);
        prefs=getSharedPreferences("yanzi-mobile",MODE_PRIVATE);
        baseUrl=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$","");
        token=prefs.getString("token","");

        LinearLayout root=new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(YanziUiKit.dp(this,16),YanziUiKit.dp(this,16),YanziUiKit.dp(this,16),YanziUiKit.dp(this,24));
        root.setBackgroundColor(YanziUiKit.BG);

        LinearLayout top=new LinearLayout(this);top.setOrientation(LinearLayout.HORIZONTAL);top.setGravity(android.view.Gravity.CENTER_VERTICAL);
        TextView back=YanziUiKit.secondaryButton(this,"‹",this::finish);
        top.addView(back,new LinearLayout.LayoutParams(YanziUiKit.dp(this,42),YanziUiKit.dp(this,42)));
        LinearLayout titleCopy=new LinearLayout(this);titleCopy.setOrientation(LinearLayout.VERTICAL);titleCopy.setPadding(YanziUiKit.dp(this,12),0,0,0);
        titleCopy.addView(YanziUiKit.text(this,"应用中心",22,YanziUiKit.TEXT,true));
        titleCopy.addView(YanziUiKit.text(this,"连接、获取并管理燕子应用",12,YanziUiKit.SECONDARY,false));
        top.addView(titleCopy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView refresh=YanziUiKit.link(this,"刷新",this::load);refreshBusy=new BusyButton(refresh);top.addView(refresh);
        root.addView(top,YanziUiKit.cardLp(this));

        status=YanziUiKit.text(this,"正在读取应用目录…",11,YanziUiKit.MUTED,false);
        status.setPadding(YanziUiKit.dp(this,3),0,0,YanziUiKit.dp(this,8));
        root.addView(status);

        root.addView(YanziUiKit.sectionLabel(this,"可获取应用"));
        list=new LinearLayout(this);list.setOrientation(LinearLayout.VERTICAL);
        root.addView(list);

        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.addView(root);setContentView(scroll);
        try{accountId=ExtensionStorageProvider.accountId(token);load();}catch(Exception e){status.setText("请先登录燕子账号。");}
    }
    private void checkAccount()throws Exception{
        if(!accountId.equals(ExtensionStorageProvider.accountId(prefs.getString("token",""))))throw new IllegalStateException("账号已切换，请重新打开应用中心");
    }
    private interface Task { void run() throws Exception; }
    private void work(Task task, BusyButton action, String message){
        if(working)return;
        working=true;setActionsEnabled(false);refreshBusy.begin("正在处理…");if(action!=null)action.begin(message);
        status.setText(message);
        executor.execute(()->{try{checkAccount();task.run();}catch(Exception e){ui(()->status.setText("操作失败："+e.getMessage()));}
            finally{ui(()->{working=false;setActionsEnabled(true);refreshBusy.finish();if(action!=null)action.finish();});}});
    }
    private void setActionsEnabled(boolean enabled){for(TextView button:actionButtons){button.setEnabled(enabled);button.setAlpha(enabled?1f:0.55f);}}
    private void ui(Runnable action){runOnUiThread(()->{if(!isFinishing()&&!isDestroyed())action.run();});}
    private void load(){if(accountId==null)return;work(this::readCatalog,null,"正在刷新目录…");}
    private void readCatalog()throws Exception{
        JSONObject catalog=json("/v1/applications/catalog",null,false),library=json("/v1/applications/library",null,true);
        checkAccount();ui(()->render(catalog.optJSONArray("applications"),library.optJSONArray("applications")));
    }
    private void render(JSONArray apps,JSONArray selections){
        list.removeAllViews();
        actionButtons.clear();
        status.setText("目录已刷新 · 内嵌应用可跨设备获取，独立应用在本机确认安装");
        if(apps==null||apps.length()==0){
            TextView empty=YanziUiKit.text(this,"暂无已发布应用",13,YanziUiKit.MUTED,false);
            empty.setGravity(android.view.Gravity.CENTER);empty.setPadding(0,YanziUiKit.dp(this,28),0,YanziUiKit.dp(this,28));list.addView(empty);return;
        }
        for(int i=0;i<apps.length();i++){
            JSONObject app=apps.optJSONObject(i);if(app==null)continue;
            String id=app.optString("applicationId"),kind=app.optString("kind");JSONObject selected=null;
            if(selections!=null)for(int j=0;j<selections.length();j++){JSONObject item=selections.optJSONObject(j);if(item!=null&&id.equals(item.optString("applicationId")))selected=item;}
            final JSONObject selection=selected;boolean enabled=selected!=null&&selected.optBoolean("enabled");
            Intent launch="android-apk".equals(kind)?getPackageManager().getLaunchIntentForPackage(app.optString("packageName")):null;
            boolean current=false;if(launch!=null)try{current=getPackageManager().getPackageInfo(app.optString("packageName"),0).getLongVersionCode()>=app.optLong("versionCode");}catch(Exception ignored){}
            final boolean installedCurrent=current;final Intent launchIntent=launch;

            LinearLayout card=YanziUiKit.card(this);
            LinearLayout head=new LinearLayout(this);head.setOrientation(LinearLayout.HORIZONTAL);head.setGravity(android.view.Gravity.CENTER_VERTICAL);
            FrameLayout iconBox=new FrameLayout(this);iconBox.setBackground(YanziUiKit.bg(android.graphics.Color.rgb(22,40,62),15,YanziUiKit.STROKE,1));
            iconBox.addView(YanziUiKit.icon(this,"apps",YanziUiKit.BLUE,24),new FrameLayout.LayoutParams(YanziUiKit.dp(this,24),YanziUiKit.dp(this,24),android.view.Gravity.CENTER));
            LinearLayout.LayoutParams iconLp=new LinearLayout.LayoutParams(YanziUiKit.dp(this,48),YanziUiKit.dp(this,48));iconLp.rightMargin=YanziUiKit.dp(this,12);head.addView(iconBox,iconLp);
            LinearLayout copy=new LinearLayout(this);copy.setOrientation(LinearLayout.VERTICAL);
            copy.addView(YanziUiKit.text(this,app.optString("name")+"  "+app.optString("version"),16,YanziUiKit.TEXT,true));
            copy.addView(YanziUiKit.text(this,app.optString("description"),12,YanziUiKit.SECONDARY,false));
            String state=enabled?"已加入账号":(installedCurrent?"已安装":"可获取");
            TextView stateTv=YanziUiKit.text(this,state,11,enabled?YanziUiKit.GREEN:YanziUiKit.MUTED,true);
            copy.addView(stateTv);
            head.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
            card.addView(head);

            String actionText="mobile-js".equals(kind)?(enabled?"获取 / 更新":"获取并加入账号"):(current?"打开应用":"下载并安装");
            TextView action=YanziUiKit.primaryButton(this,actionText,null);
            actionButtons.add(action);if(working){action.setEnabled(false);action.setAlpha(0.55f);}
            BusyButton actionBusy=new BusyButton(action);
            action.setOnClickListener(v->{if(installedCurrent){startActivity(launchIntent);return;}work(()->{
                if("mobile-js".equals(kind))installDefinition(app);else if("android-apk".equals(kind))downloadApk(app);else throw new IllegalStateException("不支持的应用类型");
                if(!enabled)json("/v1/applications/library/"+id,new JSONObject().put("enabled",true).put("expectedRevision",selection==null?0:selection.optLong("revision")),true);
                checkAccount();ui(()->status.setText("已加入账号，其他设备刷新后可见。"));
                if("mobile-js".equals(kind))readCatalog();
            },actionBusy,"mobile-js".equals(kind)?"正在获取…":"正在下载…");});
            LinearLayout.LayoutParams ap=new LinearLayout.LayoutParams(-1,YanziUiKit.dp(this,40));ap.topMargin=YanziUiKit.dp(this,12);card.addView(action,ap);
            list.addView(card,YanziUiKit.cardLp(this));
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
        ui(()->status.setText("正在下载并校验…"));
        File target=new File(getCacheDir(),"application-"+app.getString("applicationId")+".apk");
        try{
            String hash=CloudRequestRetry.systemFirst(true,systemRoute->{
                checkAccount();HttpURLConnection c=connection(assetUrl(app),systemRoute);
                MessageDigest digest=MessageDigest.getInstance("SHA-256");long count=0;
                try{
                    if(c.getResponseCode()!=200)throw new IllegalStateException("下载失败，HTTP "+c.getResponseCode());
                    try(InputStream in=c.getInputStream();FileOutputStream out=new FileOutputStream(target)){
                        byte[] b=new byte[65536];int n;while((n=in.read(b))!=-1){count+=n;if(count>200L*1024*1024)throw new HttpResponseBody.TooLarge();digest.update(b,0,n);out.write(b,0,n);}
                    }
                    checkAccount();return hex(digest.digest());
                }finally{c.disconnect();}
            });
            if(target.length()!=app.getLong("size")||!hash.equals(app.getString("sha256")))throw new IOException("下载校验失败");
            PackageInfo apk=getPackageManager().getPackageArchiveInfo(target.getAbsolutePath(),android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES);
            if(apk==null||!apk.packageName.equals(app.getString("packageName"))||apk.getLongVersionCode()!=app.getLong("versionCode"))throw new IOException("安装包身份不匹配");
            String signer=hex(MessageDigest.getInstance("SHA-256").digest(apk.signingInfo.getApkContentsSigners()[0].toByteArray()));
            if(!signer.equals(app.getString("certificateSha256")))throw new IOException("应用签名不匹配");
            checkAccount();ui(()->install(target));
        }catch(Exception e){target.delete();throw e;}
    }
    private void install(File apk){pendingApk=apk;
        if(!getPackageManager().canRequestPackageInstalls()){
            status.setText("请允许燕子安装应用，返回后继续系统安装确认。");startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName())));return;
        }
        pendingApk=null;Uri uri=FileProvider.getUriForFile(this,BuildConfig.APPLICATION_ID+".fileprovider",apk);
        startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
    }
    @Override protected void onResume(){super.onResume();ExternalAccessManager.foreground(this);if(pendingApk!=null&&getPackageManager().canRequestPackageInstalls())install(pendingApk);}
    @Override protected void onDestroy(){executor.shutdownNow();super.onDestroy();}
    private URL assetUrl(JSONObject app)throws Exception{
        URL url=new URL(new URL(baseUrl),app.getString("downloadPath")),base=new URL(baseUrl);
        if(!url.getProtocol().equals("https")||!url.getHost().equals(base.getHost())||url.getPort()!=base.getPort()||!url.getPath().startsWith("/downloads/applications/"))throw new IOException("下载地址无效");return url;
    }
    private static HttpURLConnection connection(URL url,boolean systemRoute)throws Exception{
        HttpURLConnection c=systemRoute?(HttpURLConnection)url.openConnection():MobileNetworkRouting.openCloudConnection(url);c.setInstanceFollowRedirects(false);c.setConnectTimeout(15000);c.setReadTimeout(30000);c.setRequestProperty("User-Agent","YanziClient-Mobile/"+BuildConfig.VERSION_NAME);return c;
    }
    private byte[] download(JSONObject app,int limit)throws Exception{
        URL url=assetUrl(app);
        byte[] b=CloudRequestRetry.systemFirst(true,systemRoute->{checkAccount();HttpURLConnection c=connection(url,systemRoute);
            try{if(c.getResponseCode()!=200)throw new IllegalStateException("下载失败，HTTP "+c.getResponseCode());return read(c.getInputStream(),limit);}finally{c.disconnect();}});
        checkAccount();if(b.length!=app.getLong("size")||!hex(MessageDigest.getInstance("SHA-256").digest(b)).equals(app.getString("sha256")))throw new IOException("应用校验失败");return b;
    }
    private JSONObject json(String path,JSONObject input,boolean authenticated)throws Exception{
        return json(path,input,authenticated,input==null?"GET":"PUT");
    }
    private JSONObject json(String path,JSONObject input,boolean authenticated,String method)throws Exception{
        return CloudRequestRetry.systemFirst("GET".equals(method),systemRoute->jsonOnce(path,input,authenticated,method,systemRoute));
    }
    private JSONObject jsonOnce(String path,JSONObject input,boolean authenticated,String method,boolean systemRoute)throws Exception{
        checkAccount();HttpURLConnection c=connection(new URL(baseUrl+path),systemRoute);if(authenticated)c.setRequestProperty("Authorization","Bearer "+token);
        c.setRequestMethod(method);
        try{if(input!=null){c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");try(OutputStream out=c.getOutputStream()){out.write(input.toString().getBytes(StandardCharsets.UTF_8));}}
            int code=c.getResponseCode();if(code!=200)throw new IllegalStateException(code==401?"登录已过期，请返回燕子重新登录":"服务返回 "+code);
            JSONObject value=new JSONObject(new String(read(c.getInputStream(),128*1024),StandardCharsets.UTF_8));checkAccount();return value;
        }finally{c.disconnect();}
    }
    private static byte[] read(InputStream stream,int limit)throws Exception{
        try(InputStream in=stream;ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))!=-1){if(out.size()+n>limit)throw new HttpResponseBody.TooLarge();out.write(b,0,n);}return out.toByteArray();}
    }
    private static String hex(byte[] b){StringBuilder s=new StringBuilder();for(byte v:b)s.append(String.format(java.util.Locale.ROOT,"%02x",v&255));return s.toString();}
}

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
        root.addView(top,YanziUiKit.cardLp(this));

        LinearLayout hero=YanziUiKit.tintedCard(this,android.graphics.Color.rgb(13,35,67),android.graphics.Color.rgb(38,76,124));
        hero.addView(YanziUiKit.header(this,"一个入口，连接你的工具","账号应用跨设备可见；独立应用仍由每台手机确认安装","apps",YanziUiKit.BLUE));
        status=YanziUiKit.text(this,"正在读取应用目录…",12,YanziUiKit.SECONDARY,false);
        status.setPadding(0,YanziUiKit.dp(this,10),0,0);
        hero.addView(status);
        root.addView(hero,YanziUiKit.cardLp(this));

        LinearLayout actions=new LinearLayout(this);actions.setOrientation(LinearLayout.HORIZONTAL);
        actions.addView(YanziUiKit.actionTile(this,"refresh",YanziUiKit.GREEN,"刷新","更新目录",this::load),new LinearLayout.LayoutParams(0,-2,1f));
        LinearLayout.LayoutParams a2=new LinearLayout.LayoutParams(0,-2,1f);a2.leftMargin=YanziUiKit.dp(this,7);
        actions.addView(YanziUiKit.actionTile(this,"database-outline",YanziUiKit.PURPLE,"AI 数据","精确授权",()->startActivity(new Intent(this,AiDataAccessActivity.class))),a2);
        LinearLayout.LayoutParams a3=new LinearLayout.LayoutParams(0,-2,1f);a3.leftMargin=YanziUiKit.dp(this,7);
        actions.addView(YanziUiKit.actionTile(this,"key-outline",YanziUiKit.ORANGE,"开发者授权","1 小时令牌",this::authorize),a3);
        root.addView(actions,YanziUiKit.cardLp(this));

        root.addView(YanziUiKit.sectionLabel(this,"全部应用"));
        list=new LinearLayout(this);list.setOrientation(LinearLayout.VERTICAL);
        root.addView(list);

        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.addView(root);setContentView(scroll);
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
    private void createInvitation(){
        LinearLayout form=new LinearLayout(this);form.setOrientation(1);
        EditText extension=new EditText(this);extension.setText("taskbar-calendar");extension.setHint("小程序 ID");form.addView(extension);
        EditText key=new EditText(this);key.setText("calendar.v1.json");key.setHint("数据文件 key");form.addView(key);
        CheckBox writable=new CheckBox(this);writable.setText("允许申请增删改查（默认只读）");form.addView(writable);
        new android.app.AlertDialog.Builder(this).setTitle("创建在线接入地址").setMessage("地址有效 7 天。把地址交给 AI，它申请后仍需你逐次确认；确认前不能访问数据。").setView(form)
            .setNegativeButton("取消",null).setPositiveButton("创建",(d,w)->work(()->{
                JSONObject input=new JSONObject().put("extensionId",extension.getText().toString().trim()).put("key",key.getText().toString().trim()).put("access",writable.isChecked()?"read-write":"read");
                JSONObject result=json("/v1/applications/access-invites",input,true,"POST");checkAccount();
                ui(()->new android.app.AlertDialog.Builder(this).setTitle("接入地址已生成").setMessage(result.optString("address"))
                    .setPositiveButton("复制地址",(dialog,which)->{android.content.ClipData clip=android.content.ClipData.newPlainText("燕子接入地址",result.optString("address"));android.os.PersistableBundle extras=new android.os.PersistableBundle();extras.putBoolean("android.content.extra.IS_SENSITIVE",true);clip.getDescription().setExtras(extras);((android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(clip);})
                    .setNegativeButton("撤销地址",(dialog,which)->work(()->{json("/v1/applications/access-invites/"+result.getString("address").substring(result.getString("address").lastIndexOf('/')+1),null,true,"DELETE");ui(()->status.setText("接入地址已撤销"));})).show());
            })).show();
    }
    private void createMultiInvitation(){work(()->{
        JSONArray resources=json("/v1/applications/access-resources",null,true).getJSONArray("resources");checkAccount();
        ui(()->{
            LinearLayout form=new LinearLayout(this);form.setOrientation(1);java.util.List<CheckBox> choices=new java.util.ArrayList<>();
            CheckBox all=new CheckBox(this);all.setText("全选当前清单");all.setChecked(true);form.addView(all);
            LinearLayout rows=new LinearLayout(this);rows.setOrientation(1);ScrollView scroll=new ScrollView(this);scroll.addView(rows);form.addView(scroll,new LinearLayout.LayoutParams(-1,(int)(200*getResources().getDisplayMetrics().density)));
            for(int i=0;i<resources.length();i++){JSONObject scope=resources.optJSONObject(i);CheckBox choice=new CheckBox(this);choice.setChecked(true);choice.setText(scope.optString("name")+" · "+scope.optString("key"));choices.add(choice);rows.addView(choice);}
            all.setOnClickListener(v->{for(CheckBox c:choices)c.setChecked(all.isChecked());});
            CheckBox writable=new CheckBox(this);writable.setText("允许申请增删改查（默认只读）");form.addView(writable);
            new android.app.AlertDialog.Builder(this).setTitle("AI 可申请的数据清单").setMessage("只提供所选数据的目录。AI 可申请部分或全部，仍需你再次确认。地址有效 7 天。").setView(form).setNegativeButton("取消",null)
                .setPositiveButton("生成提示词",(d,w)->{JSONArray selected=new JSONArray();for(int i=0;i<choices.size();i++)if(choices.get(i).isChecked())selected.put(resources.optJSONObject(i));work(()->{
                    JSONObject result=json("/v1/applications/access-invites",new JSONObject().put("resources",selected).put("access",writable.isChecked()?"read-write":"read"),true,"POST");checkAccount();
                    String address=result.getString("address"),prompt="请访问燕子数据接入地址："+address+"\n先 GET 地址和 resourceList 获取数据清单。根据我的任务只申请必要的 scopes；仅在我明确要求全部时申请 scopes=all。提交 clientName，告诉我核对码，等待手机或电脑确认，按 poll 领取限时授权，再调用返回的 resources 数据接口。修改前读取最新版本；未经我明确要求不要删除数据。";
                    ui(()->new android.app.AlertDialog.Builder(this).setTitle("接入提示词已生成").setMessage(prompt).setPositiveButton("复制提示词",(dialog,which)->{android.content.ClipData clip=android.content.ClipData.newPlainText("燕子 AI 接入",prompt);android.os.PersistableBundle extras=new android.os.PersistableBundle();extras.putBoolean("android.content.extra.IS_SENSITIVE",true);clip.getDescription().setExtras(extras);((android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(clip);})
                        .setNegativeButton("撤销地址",(dialog,which)->work(()->{json("/v1/applications/access-invites/"+address.substring(address.lastIndexOf('/')+1),null,true,"DELETE");ui(()->status.setText("接入地址已撤销"));})).show());
                });}).show();
        });
    });}
    @Override protected void onPause(){ExternalAccessManager.background(this);super.onPause();}
    private interface Task{void run()throws Exception;}
    private void work(Task task){executor.execute(()->{try{checkAccount();task.run();}catch(Exception e){ui(()->status.setText("操作失败："+e.getMessage()));}});}
    private void ui(Runnable action){runOnUiThread(()->{if(!isFinishing()&&!isDestroyed())action.run();});}
    private void load(){if(accountId==null)return;work(()->{
        JSONObject catalog=json("/v1/applications/catalog",null,false),library=json("/v1/applications/library",null,true);
        checkAccount();ui(()->render(catalog.optJSONArray("applications"),library.optJSONArray("applications")));
    });}
    private void render(JSONArray apps,JSONArray selections){
        list.removeAllViews();
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
            action.setOnClickListener(v->{if(installedCurrent){startActivity(launchIntent);return;}action.setEnabled(false);work(()->{try{
                if("mobile-js".equals(kind))installDefinition(app);else if("android-apk".equals(kind))downloadApk(app);else throw new IllegalStateException("不支持的应用类型");
                if(!enabled)json("/v1/applications/library/"+id,new JSONObject().put("enabled",true).put("expectedRevision",selection==null?0:selection.optLong("revision")),true);
                checkAccount();ui(()->status.setText("已加入账号，其他设备刷新后可见。"));
                if("mobile-js".equals(kind))load();
            }finally{ui(()->action.setEnabled(true));}});});
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
    @Override protected void onResume(){super.onResume();ExternalAccessManager.foreground(this);if(pendingApk!=null&&getPackageManager().canRequestPackageInstalls())install(pendingApk);}
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

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
final class ApplicationCatalogView {
    private final Activity activity;
    private final boolean embedded;
    private boolean closed;
    private boolean reloadAfterWork;
    private android.view.View content;
    ApplicationCatalogView(Activity activity,boolean embedded){this.activity=activity;this.embedded=embedded;}
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private SharedPreferences prefs; private LinearLayout list; private TextView status;
    private String accountId,baseUrl,token; private File pendingApk;
    private BusyButton refreshBusy; private boolean working;
    private final java.util.List<TextView> actionButtons=new java.util.ArrayList<>();
    private JSONArray catalogApps, libraryApps;
    private EditText search;
    private String category="推荐";
    private final java.util.Map<String,TextView> tabs=new java.util.LinkedHashMap<>();
    private static final int INK=0xff202624, SUB=0xff79847e, ACCENT=0xff159775, LINE=0xffe9efec;
    android.view.View createView() {
        MobileNetworkRouting.initialize(activity);
        prefs=activity.getSharedPreferences("yanzi-mobile",android.content.Context.MODE_PRIVATE);
        baseUrl=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$","");
        token=prefs.getString("token","");

        LinearLayout root=new LinearLayout(activity);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(YanziUiKit.dp(activity,16),YanziUiKit.dp(activity,16),YanziUiKit.dp(activity,16),YanziUiKit.dp(activity,24));
        root.setBackgroundColor(android.graphics.Color.WHITE);
        if(!embedded){
        activity.getWindow().setStatusBarColor(android.graphics.Color.WHITE);
        activity.getWindow().setNavigationBarColor(android.graphics.Color.WHITE);
        activity.getWindow().getDecorView().setSystemUiVisibility(android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        }

        LinearLayout top=new LinearLayout(activity);top.setOrientation(LinearLayout.HORIZONTAL);top.setGravity(android.view.Gravity.CENTER_VERTICAL);
        TextView back=YanziUiKit.secondaryButton(activity,"‹",activity::finish);
        back.setTextColor(INK);back.setBackground(YanziUiKit.bg(0xfff5f8f6,14,LINE,1));back.setContentDescription("返回");
        if(!embedded)top.addView(back,new LinearLayout.LayoutParams(YanziUiKit.dp(activity,42),YanziUiKit.dp(activity,42)));
        LinearLayout titleCopy=new LinearLayout(activity);titleCopy.setOrientation(LinearLayout.VERTICAL);titleCopy.setPadding(YanziUiKit.dp(activity,12),0,0,0);
        titleCopy.addView(YanziUiKit.text(activity,"应用商店",22,INK,true));
        titleCopy.addView(YanziUiKit.text(activity,"发现好用的燕子应用",12,SUB,false));
        top.addView(titleCopy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView refresh=YanziUiKit.text(activity,"刷新",13,ACCENT,true);refresh.setGravity(android.view.Gravity.CENTER);refresh.setPadding(YanziUiKit.dp(activity,8),0,YanziUiKit.dp(activity,8),0);refresh.setOnClickListener(v->load());refreshBusy=new BusyButton(refresh);top.addView(refresh,new LinearLayout.LayoutParams(-2,YanziUiKit.dp(activity,44)));
        root.addView(top,YanziUiKit.cardLp(activity));

        LinearLayout searchRow=new LinearLayout(activity);searchRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        searchRow.setPadding(YanziUiKit.dp(activity,12),0,YanziUiKit.dp(activity,8),0);
        searchRow.setBackground(YanziUiKit.bg(0xfff5f8f6,16,LINE,1));
        searchRow.addView(YanziUiKit.icon(activity,"search",SUB,22));
        search=new EditText(activity);search.setSingleLine(true);search.setTextSize(15);search.setTextColor(INK);search.setHintTextColor(SUB);
        search.setHint("搜索应用、功能");search.setBackgroundColor(android.graphics.Color.TRANSPARENT);
        search.setPadding(YanziUiKit.dp(activity,10),0,0,0);search.setContentDescription("搜索应用");
        searchRow.addView(search,new LinearLayout.LayoutParams(0,YanziUiKit.dp(activity,48),1f));root.addView(searchRow,YanziUiKit.cardLp(activity));
        search.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){} public void onTextChanged(CharSequence s,int a,int b,int c){renderStore();} public void afterTextChanged(android.text.Editable e){}});
        LinearLayout nav=new LinearLayout(activity);
        for(String name:new String[]{"推荐","工具","图文","我的"}){
            TextView tab=YanziUiKit.text(activity,name,16,SUB,false);tab.setGravity(android.view.Gravity.CENTER);
            tab.setOnClickListener(v->{category=name;renderStore();});tabs.put(name,tab);
            nav.addView(tab,new LinearLayout.LayoutParams(0,YanziUiKit.dp(activity,48),1f));
        }
        root.addView(nav,YanziUiKit.cardLp(activity));

        status=YanziUiKit.text(activity,"正在读取应用目录…",11,SUB,false);
        status.setPadding(YanziUiKit.dp(activity,3),0,0,YanziUiKit.dp(activity,8));
        root.addView(status);

        list=new LinearLayout(activity);list.setOrientation(LinearLayout.VERTICAL);
        root.addView(list);

        ScrollView scroll=new ScrollView(activity);scroll.setFillViewport(true);scroll.addView(root);content=scroll;
        renderStore();
        if(!embedded)show();
        return content;
    }
    private void checkAccount()throws Exception{
        if(!accountId.equals(ExtensionStorageProvider.accountId(prefs.getString("token",""))))throw new IllegalStateException("账号已切换，请重新打开应用商店");
    }
    private interface Task { void run() throws Exception; }
    private void work(Task task, BusyButton action, String message){
        if(working)return;
        working=true;setActionsEnabled(false);refreshBusy.begin("刷新中");if(action!=null)action.begin(message);
        status.setText(message);
        executor.execute(()->{try{checkAccount();task.run();}catch(Exception e){ui(()->status.setText("操作失败："+e.getMessage()));}
            finally{ui(()->{working=false;setActionsEnabled(true);refreshBusy.finish();if(action!=null)action.finish();if(reloadAfterWork){reloadAfterWork=false;show();}});}});
    }
    private void setActionsEnabled(boolean enabled){for(TextView button:actionButtons){button.setEnabled(enabled);button.setAlpha(enabled?1f:0.55f);}}
    private void ui(Runnable action){activity.runOnUiThread(()->{if(!closed&&!activity.isFinishing()&&!activity.isDestroyed())action.run();});}
    private void load(){if(accountId==null)return;work(this::readCatalog,null,"正在刷新目录…");}
    private void readCatalog()throws Exception{
        JSONObject catalog=json("/v1/applications/catalog",null,false),library=json("/v1/applications/library",null,true);
        checkAccount();ui(()->render(catalog.optJSONArray("applications"),library.optJSONArray("applications")));
    }
    private void render(JSONArray apps,JSONArray selections){
        catalogApps=apps;libraryApps=selections;
        status.setText("目录已刷新 · 安装需经系统确认");
        renderStore();
    }
    private boolean acquired(JSONObject app){
        if(activity.getPackageManager().getLaunchIntentForPackage(app.optString("packageName"))!=null)return true;
        if(libraryApps!=null)for(int i=0;i<libraryApps.length();i++){JSONObject item=libraryApps.optJSONObject(i);if(item!=null&&app.optString("applicationId").equals(item.optString("applicationId"))&&item.optBoolean("enabled"))return true;}
        return false;
    }
    private boolean matches(JSONObject app,String query){
        String id=app.optString("applicationId");
        boolean content=id.equals("yanzi-stream")||id.equals("yanzi-cards")||id.equals("yanzi-notes")||id.equals("yanzi-album")||id.equals("quick-notes");
        if("工具".equals(category)&&content||"图文".equals(category)&&!content||"我的".equals(category)&&!acquired(app))return false;
        return (app.optString("name")+" "+app.optString("description")).toLowerCase(java.util.Locale.ROOT).contains(query);
    }
    private android.view.View appIcon(JSONObject app,int size){
        String id=app.optString("applicationId"),icon="apps";int color=YanziUiKit.BLUE;
        if(id.contains("files")){icon="folder";color=0xffe5a533;}
        else if(id.contains("calendar")){icon="calendar";color=0xff5f80db;}
        else if(id.contains("album")){icon="image";color=0xffdf7a9a;}
        else if(id.contains("stream")){icon="play";color=0xff18a990;}
        else if(id.contains("cards")){icon="view-grid-outline";color=0xff8870cf;}
        else if(id.contains("notes")){icon="file-document-outline";color=0xffe59b56;}
        FrameLayout box=new FrameLayout(activity);box.setBackground(YanziUiKit.bg(color,16,0,0));
        box.addView(YanziUiKit.icon(activity,icon,android.graphics.Color.WHITE,30),new FrameLayout.LayoutParams(YanziUiKit.dp(activity,30),YanziUiKit.dp(activity,30),android.view.Gravity.CENTER));
        box.setLayoutParams(new LinearLayout.LayoutParams(YanziUiKit.dp(activity,size),YanziUiKit.dp(activity,size)));return box;
    }
    private TextView storeAction(JSONObject app,JSONArray selections){
        String id=app.optString("applicationId"),kind=app.optString("kind");JSONObject selected=null;
        if(selections!=null)for(int j=0;j<selections.length();j++){JSONObject item=selections.optJSONObject(j);if(item!=null&&id.equals(item.optString("applicationId")))selected=item;}
        final JSONObject selection=selected;boolean enabled=selected!=null&&selected.optBoolean("enabled");
        Intent launch="android-apk".equals(kind)?activity.getPackageManager().getLaunchIntentForPackage(app.optString("packageName")):null;
        boolean current=false;if(launch!=null)try{current=androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(activity.getPackageManager().getPackageInfo(app.optString("packageName"),0))>=app.optLong("versionCode");}catch(Exception ignored){}
        final boolean installedCurrent=current;final Intent launchIntent=launch;
        String label="mobile-js".equals(kind)?(enabled?"更新":"获取"):(current?"打开":launch!=null?"更新":"安装");
        TextView action=YanziUiKit.text(activity,label,14,ACCENT,true);action.setGravity(android.view.Gravity.CENTER);
        action.setBackground(YanziUiKit.bg(0xffffffff,22,0xffa2d4c3,1));action.setClickable(true);action.setFocusable(true);
        action.setContentDescription(app.optString("name")+"："+label);actionButtons.add(action);action.setEnabled(!working);action.setAlpha(working?0.55f:1f);
        BusyButton actionBusy=new BusyButton(action);
        action.setOnClickListener(v->{if(installedCurrent){activity.startActivity(launchIntent);return;}work(()->{
            if("mobile-js".equals(kind))installDefinition(app);else if("android-apk".equals(kind))downloadApk(app);else throw new IllegalStateException("不支持的应用类型");
            if(!enabled)json("/v1/applications/library/"+id,new JSONObject().put("enabled",true).put("expectedRevision",selection==null?0:selection.optLong("revision")),true);
            checkAccount();ui(()->status.setText("mobile-js".equals(kind)?"已加入账号，其他设备刷新后可见。":"已校验安装包，请在系统界面确认安装。"));
            if("mobile-js".equals(kind))readCatalog();
        },actionBusy,"mobile-js".equals(kind)?"获取中":"下载中");});
        return action;
    }
    private void details(JSONObject app){
        new android.app.AlertDialog.Builder(activity).setTitle(app.optString("name"))
            .setMessage(app.optString("description")+"\n\n版本 "+app.optString("version")+" · "+sizeLabel(app.optLong("size")))
            .setPositiveButton("知道了",null).show();
    }
    private static String sizeLabel(long bytes){return bytes>=1024*1024?String.format(java.util.Locale.ROOT,"%.1f MB",bytes/(1024.0*1024)):String.format(java.util.Locale.ROOT,"%.0f KB",bytes/1024.0);}
    private void renderStore(){
        if(list==null)return;
        list.removeAllViews();
        actionButtons.clear();
        for(java.util.Map.Entry<String,TextView> entry:tabs.entrySet()){
            boolean active=entry.getKey().equals(category);TextView tab=entry.getValue();tab.setTextColor(active?INK:SUB);
            tab.setTypeface(active?android.graphics.Typeface.DEFAULT_BOLD:android.graphics.Typeface.DEFAULT);
            tab.setBackground(YanziUiKit.bg(active?0xffeaf6f0:0xffffffff,12,0,0));tab.setSelected(active);
        }
        if(catalogApps==null)return;
        String query=search.getText().toString().trim().toLowerCase(java.util.Locale.ROOT);
        java.util.List<JSONObject> visible=new java.util.ArrayList<>();
        for(int i=0;i<catalogApps.length();i++){JSONObject app=catalogApps.optJSONObject(i);if(app!=null&&matches(app,query))visible.add(app);}
        if(visible.isEmpty()){
            TextView empty=YanziUiKit.text(activity,query.isEmpty()?("我的".equals(category)?"还没有获取应用，去推荐页看看吧":"暂无已发布应用"):"没有找到相关应用，试试其他关键词",13,SUB,false);
            empty.setGravity(android.view.Gravity.CENTER);empty.setPadding(0,YanziUiKit.dp(activity,28),0,YanziUiKit.dp(activity,28));list.addView(empty);return;
        }
        if("推荐".equals(category)&&query.isEmpty()){
            HorizontalScrollView rail=new HorizontalScrollView(activity);rail.setHorizontalScrollBarEnabled(false);
            LinearLayout tiles=new LinearLayout(activity);
            java.util.List<JSONObject> featured=new java.util.ArrayList<>();
            for(String id:new String[]{"yanzi-files","yanzi-stream","yanzi-cards","yanzi-album","yanzi-notes"})for(JSONObject app:visible)if(id.equals(app.optString("applicationId")))featured.add(app);
            if(featured.isEmpty())featured.addAll(visible.subList(0,Math.min(5,visible.size())));
            int width=Math.max(80,Math.min(100,(int)(activity.getResources().getDisplayMetrics().widthPixels/activity.getResources().getDisplayMetrics().density-(embedded?64:32))/4));
            for(JSONObject app:featured){
                LinearLayout tile=new LinearLayout(activity);tile.setOrientation(LinearLayout.VERTICAL);tile.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
                android.view.View icon=appIcon(app,60);icon.setOnClickListener(v->details(app));tile.addView(icon);
                TextView name=YanziUiKit.text(activity,app.optString("name"),13,INK,false);name.setGravity(android.view.Gravity.CENTER);name.setSingleLine(true);name.setEllipsize(android.text.TextUtils.TruncateAt.END);
                name.setPadding(0,YanziUiKit.dp(activity,8),0,YanziUiKit.dp(activity,8));name.setOnClickListener(v->details(app));tile.addView(name,new LinearLayout.LayoutParams(-1,-2));
                tile.addView(storeAction(app,libraryApps),new LinearLayout.LayoutParams(YanziUiKit.dp(activity,72),YanziUiKit.dp(activity,44)));
                tiles.addView(tile,new LinearLayout.LayoutParams(YanziUiKit.dp(activity,width),-2));
            }
            rail.addView(tiles);LinearLayout.LayoutParams railLp=new LinearLayout.LayoutParams(-1,-2);railLp.bottomMargin=YanziUiKit.dp(activity,22);list.addView(rail,railLp);
        }
        TextView heading=YanziUiKit.text(activity,("推荐".equals(category)?"发现好应用":category+"应用")+" · "+visible.size(),18,INK,true);
        heading.setPadding(0,0,0,YanziUiKit.dp(activity,10));list.addView(heading);
        for(JSONObject app:visible){
            LinearLayout row=new LinearLayout(activity);row.setGravity(android.view.Gravity.CENTER_VERTICAL);row.setPadding(0,YanziUiKit.dp(activity,12),0,YanziUiKit.dp(activity,12));
            android.view.View icon=appIcon(app,56);icon.setOnClickListener(v->details(app));row.addView(icon);
            LinearLayout copy=new LinearLayout(activity);copy.setOrientation(LinearLayout.VERTICAL);copy.setPadding(YanziUiKit.dp(activity,12),0,YanziUiKit.dp(activity,10),0);copy.setOnClickListener(v->details(app));
            TextView name=YanziUiKit.text(activity,app.optString("name"),16,INK,true);name.setSingleLine(true);name.setEllipsize(android.text.TextUtils.TruncateAt.END);copy.addView(name);
            TextView desc=YanziUiKit.text(activity,app.optString("description"),12,SUB,false);desc.setSingleLine(true);desc.setEllipsize(android.text.TextUtils.TruncateAt.END);desc.setPadding(0,YanziUiKit.dp(activity,6),0,YanziUiKit.dp(activity,5));copy.addView(desc);
            copy.addView(YanziUiKit.text(activity,"v"+app.optString("version")+" · "+sizeLabel(app.optLong("size"))+(acquired(app)?" · 已获取":""),10,SUB,false));
            row.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));row.addView(storeAction(app,libraryApps),new LinearLayout.LayoutParams(YanziUiKit.dp(activity,76),YanziUiKit.dp(activity,44)));list.addView(row);
            android.view.View line=new android.view.View(activity);line.setBackgroundColor(LINE);list.addView(line,new LinearLayout.LayoutParams(-1,YanziUiKit.dp(activity,1)));
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
        File target=new File(activity.getCacheDir(),"application-"+app.getString("applicationId")+".apk");
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
            int flags=android.os.Build.VERSION.SDK_INT>=28?android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES:android.content.pm.PackageManager.GET_SIGNATURES;
            PackageInfo apk=activity.getPackageManager().getPackageArchiveInfo(target.getAbsolutePath(),flags);
            if(apk==null||!apk.packageName.equals(app.getString("packageName"))||androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(apk)!=app.getLong("versionCode"))throw new IOException("安装包身份不匹配");
            android.content.pm.Signature[] signers=android.os.Build.VERSION.SDK_INT>=28&&apk.signingInfo!=null?apk.signingInfo.getApkContentsSigners():apk.signatures;
            if(signers==null||signers.length!=1)throw new IOException("应用签名不匹配");
            String signer=hex(MessageDigest.getInstance("SHA-256").digest(signers[0].toByteArray()));
            if(!signer.equals(app.getString("certificateSha256")))throw new IOException("应用签名不匹配");
            checkAccount();ui(()->install(target));
        }catch(Exception e){target.delete();throw e;}
    }
    private void install(File apk){pendingApk=apk;
        if(!activity.getPackageManager().canRequestPackageInstalls()){
            status.setText("请允许燕子安装应用，返回后继续系统安装确认。");activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+activity.getPackageName())));return;
        }
        pendingApk=null;Uri uri=FileProvider.getUriForFile(activity,BuildConfig.APPLICATION_ID+".fileprovider",apk);
        activity.startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
    }
    void show(){
        String currentToken=prefs.getString("token","");
        String currentBase=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd").replaceAll("/+$","");
        if(working&&(!currentToken.equals(token)||!currentBase.equals(baseUrl))){reloadAfterWork=true;catalogApps=null;libraryApps=null;renderStore();status.setText("账号已切换，正在重新加载…");return;}
        if(accountId==null||!currentToken.equals(token)||!currentBase.equals(baseUrl)){
            token=currentToken;baseUrl=currentBase;catalogApps=null;libraryApps=null;renderStore();
            try{accountId=ExtensionStorageProvider.accountId(token);load();}catch(Exception e){accountId=null;status.setText("请先登录燕子账号。");}
        }else if(catalogApps==null)load();else renderStore();
    }
    void resume(){if(pendingApk!=null&&activity.getPackageManager().canRequestPackageInstalls())install(pendingApk);else if(catalogApps!=null)renderStore();}
    void close(){closed=true;executor.shutdownNow();}
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

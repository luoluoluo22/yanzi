package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.os.Bundle;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.graphics.Color;
import android.view.Gravity;
import android.view.View;
import android.webkit.*;
import android.widget.*;
import android.text.InputType;
import java.net.URI;

/** Secure browser client for the self-hosted MeshCentral remote-control gateway.
 * No app-owned session cookies or device secrets are synchronized.
 */
public final class RemoteDesktopActivity extends Activity {
    private static final String PREFS = "yanzi-remote-desktop";
    private LinearLayout root;
    private WebView viewer;
    private TextView status;
    private String origin;
    private String url;

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        getWindow().setStatusBarColor(0xff101827);
        getWindow().setNavigationBarColor(0xff101827);
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(0xff101827);
        setContentView(root);
        String remembered = getSharedPreferences(PREFS,MODE_PRIVATE).getString("gateway","");
        if (remembered.isEmpty()) configure();
        else open(remembered);
    }

    private TextView action(String text, Runnable task) {
        TextView v = new TextView(this);
        v.setText(text); v.setTextColor(Color.WHITE); v.setTextSize(14);
        v.setGravity(Gravity.CENTER);
        v.setPadding(dp(14),dp(12),dp(14),dp(12));
        v.setOnClickListener(w->task.run());
        return v;
    }
    private int dp(int n){return Math.round(getResources().getDisplayMetrics().density*n);}
    private void shell(){
        root.removeAllViews();
        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        TextView back = action("‹ 返回",this::finish);
        TextView options = action("连接设置",this::configure);
        TextView close = action("断开",this::disconnect);
        bar.addView(back,new LinearLayout.LayoutParams(0,dp(52),1f));
        bar.addView(options,new LinearLayout.LayoutParams(0,dp(52),1f));
        bar.addView(close,new LinearLayout.LayoutParams(0,dp(52),1f));
        root.addView(bar);
        status = new TextView(this);
        status.setTextColor(0xffa7b8cc);
        status.setPadding(dp(12),dp(5),dp(12),dp(9));
        status.setTextSize(12);
        root.addView(status);
    }
    private static String validated(String supplied) throws Exception {
        URI uri = new URI(supplied.trim());
        if (!"https".equalsIgnoreCase(uri.getScheme()) || uri.getHost()==null
            || uri.getRawUserInfo()!=null || uri.getPort()!=-1
            || uri.getRawQuery()!=null || uri.getRawFragment()!=null)
            throw new IllegalArgumentException("请填写网关的 HTTPS 域名，不允许账号、端口、参数或片段");
        if (!uri.getPath().isEmpty() && !"/".equals(uri.getPath()))
            throw new IllegalArgumentException("只允许 HTTPS 网关根地址");
        return "https://"+uri.getHost().toLowerCase(java.util.Locale.ROOT)+"/";
    }
    private void configure(){
        disconnectView();
        shell();
        status.setText("首次连接需要填写燕子远控网关地址。账户登录及二次验证在网关页面完成。");
        EditText input = new EditText(this);
        input.setSingleLine(true);
        input.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        input.setText(getSharedPreferences(PREFS,MODE_PRIVATE).getString("gateway",""));
        input.setHint("https://remote.example.com");
        input.setTextColor(Color.WHITE);
        input.setHintTextColor(0xff8192a6);
        root.addView(input);
        TextView save = action("保存并连接",()->{
            try{
                String destination=validated(input.getText().toString());
                getSharedPreferences(PREFS,MODE_PRIVATE).edit().putString("gateway",destination).apply();
                open(destination);
            }catch(Exception ex){ status.setText("地址无效："+ex.getMessage());}
        });
        root.addView(save);
    }
    private void open(String destination){
        try { url=validated(destination); }
        catch(Exception ex){configure();return;}
        origin=url.substring(0,url.length()-1);
        disconnectView();
        shell();
        status.setText("正在安全连接 · "+origin);
        viewer = new WebView(this);
        viewer.setBackgroundColor(0xff101827);
        viewer.getSettings().setJavaScriptEnabled(true);
        viewer.getSettings().setDomStorageEnabled(true);
        viewer.getSettings().setSupportZoom(true);
        viewer.getSettings().setBuiltInZoomControls(true);
        viewer.getSettings().setDisplayZoomControls(false);
        viewer.getSettings().setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        viewer.getSettings().setAllowFileAccess(false);
        viewer.getSettings().setAllowContentAccess(false);
        viewer.getSettings().setJavaScriptCanOpenWindowsAutomatically(false);
        viewer.getSettings().setSupportMultipleWindows(false);
        WebView.setWebContentsDebuggingEnabled(false);
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(viewer,false);
        viewer.setWebViewClient(new WebViewClient(){
            private boolean allowed(String raw){
                try{
                    URI target = new URI(raw);
                    return "https".equalsIgnoreCase(target.getScheme())
                        && origin.equalsIgnoreCase(target.getScheme()+"://"+target.getAuthority());
                }catch(Exception ignored){return false;}
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view,WebResourceRequest req){
                if(allowed(req.getUrl().toString()))return false;
                status.setText("已拦截离开远控网关的跳转");
                return true;
            }
            @Override public void onReceivedSslError(WebView view,SslErrorHandler h,android.net.http.SslError e){
                h.cancel();status.setText("TLS 证书校验失败，连接已终止");
            }
            @Override public void onPageFinished(WebView view,String u){
                if(allowed(u))status.setText("远控网关已打开 · 选择已授权的电脑");
            }
            @Override public void onReceivedError(WebView view,WebResourceRequest req,WebResourceError err){
                if(req.isForMainFrame())status.setText("网关连接失败，请检查网络及服务状态");
            }
        });
        root.addView(viewer,new LinearLayout.LayoutParams(-1,0,1f));
        viewer.loadUrl(url);
    }
    /** Keep the live WebView and its desktop WebSocket across phone rotation. */
    @Override public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        if (viewer != null) {
            viewer.requestLayout();
            viewer.invalidate();
            // Do not reloadUrl / restoreState here: rotation must retain the live remote session.
        }
    }

    private void disconnectView(){
        if(viewer!=null){
            viewer.stopLoading();
            viewer.loadUrl("about:blank");
            root.removeView(viewer);
            viewer.destroy();
            viewer=null;
        }
    }
    private void disconnect(){disconnectView();shell();status.setText("已断开远控会话");}
    @Override public void onBackPressed(){
        if(viewer!=null && viewer.canGoBack())viewer.goBack();
        else {disconnectView();super.onBackPressed();}
    }
    @Override protected void onDestroy(){disconnectView();super.onDestroy();}
}

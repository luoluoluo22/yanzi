package cc.luoluoluo.yanzi.mobile;

import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.os.*;
import android.text.format.DateFormat;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.util.*;
import java.util.concurrent.*;

public final class AiDataAccessActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private SharedPreferences prefs;
    private String token;
    private String base;
    private LinearLayout resourcesBox;
    private LinearLayout requestsBox;
    private LinearLayout grantsBox;
    private TextView requestsLabel;
    private TextView grantsLabel;
    private TextView status;
    private Switch writableSwitch;
    private final List<CheckBox> resourceChecks = new ArrayList<>();
    private JSONArray resources = new JSONArray();

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        MobileNetworkRouting.initialize(this);
        prefs = getSharedPreferences("yanzi-mobile", MODE_PRIVATE);
        token = prefs.getString("token", "");
        base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd").replaceAll("/+$", "");

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(YanziUiKit.dp(this,16), YanziUiKit.dp(this,16), YanziUiKit.dp(this,16), YanziUiKit.dp(this,24));
        root.setBackgroundColor(YanziUiKit.BG);

        LinearLayout top = new LinearLayout(this);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        TextView back = YanziUiKit.secondaryButton(this, "‹", this::finish);
        top.addView(back, new LinearLayout.LayoutParams(YanziUiKit.dp(this,42),YanziUiKit.dp(this,42)));
        LinearLayout titleCopy = new LinearLayout(this); titleCopy.setOrientation(LinearLayout.VERTICAL);
        titleCopy.setPadding(YanziUiKit.dp(this,12),0,0,0);
        titleCopy.addView(YanziUiKit.text(this,"AI 数据接入",22,YanziUiKit.TEXT,true));
        titleCopy.addView(YanziUiKit.text(this,"精确选择 AI 能看到和修改的数据",12,YanziUiKit.SECONDARY,false));
        top.addView(titleCopy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView refresh = YanziUiKit.link(this,"刷新",this::loadAll);
        top.addView(refresh);
        root.addView(top,YanziUiKit.cardLp(this));

        LinearLayout dataCard = YanziUiKit.card(this);
        LinearLayout dataHeader = YanziUiKit.header(this,"AI 可申请的数据","默认全部选中，你可以逐项取消","database-outline",YanziUiKit.BLUE);
        dataCard.addView(dataHeader);
        resourcesBox = new LinearLayout(this); resourcesBox.setOrientation(LinearLayout.VERTICAL);
        resourcesBox.setPadding(0,YanziUiKit.dp(this,10),0,0);
        dataCard.addView(resourcesBox);
        writableSwitch = new Switch(this);
        writableSwitch.setText("允许申请增删改查（关闭时只读）");
        writableSwitch.setTextColor(YanziUiKit.SECONDARY);
        writableSwitch.setPadding(0,YanziUiKit.dp(this,8),0,YanziUiKit.dp(this,8));
        dataCard.addView(writableSwitch);
        TextView generate = YanziUiKit.primaryButton(this,"生成 AI 接入提示词",this::createInvitation);
        dataCard.addView(generate,new LinearLayout.LayoutParams(-1,YanziUiKit.dp(this,44)));
        root.addView(dataCard,YanziUiKit.cardLp(this));

        requestsLabel = YanziUiKit.sectionLabel(this,"待确认申请");
        root.addView(requestsLabel);
        requestsBox = new LinearLayout(this); requestsBox.setOrientation(LinearLayout.VERTICAL);
        root.addView(requestsBox,YanziUiKit.cardLp(this));

        grantsLabel = YanziUiKit.sectionLabel(this,"当前有效授权");
        root.addView(grantsLabel);
        grantsBox = new LinearLayout(this); grantsBox.setOrientation(LinearLayout.VERTICAL);
        root.addView(grantsBox,YanziUiKit.cardLp(this));

        status = YanziUiKit.text(this,"正在读取数据清单…",11,YanziUiKit.MUTED,false);
        status.setGravity(Gravity.CENTER);
        root.addView(status);

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.addView(root);
        setContentView(scroll);
        loadAll();
    }

    @Override protected void onResume() {
        super.onResume();
        ExternalAccessManager.foreground(this);
        if (resourcesBox != null) loadAll();
    }

    @Override protected void onPause() {
        ExternalAccessManager.background(this);
        super.onPause();
    }

    private void loadAll() {
        if (token == null || token.trim().isEmpty()) {
            status.setText("请先登录燕子账号");
            return;
        }
        status.setText("正在刷新授权状态…");
        worker.execute(() -> {
            try {
                JSONObject r = MobileMessageClient.request(base,"/v1/applications/access-resources",token,"GET",null);
                JSONObject p = MobileMessageClient.request(base,"/v1/applications/access-requests",token,"GET",null);
                JSONObject g = MobileMessageClient.request(base,"/v1/applications/access-grants",token,"GET",null);
                JSONArray rr = r.optJSONArray("resources"); if (rr == null) rr = new JSONArray();
                JSONArray pp = p.optJSONArray("requests"); if (pp == null) pp = new JSONArray();
                JSONArray gg = g.optJSONArray("grants"); if (gg == null) gg = new JSONArray();
                final JSONArray fr=rr, fp=pp, fg=gg;
                runOnUiThread(() -> {
                    if (isFinishing() || isDestroyed()) return;
                    resources = fr;
                    renderResources(fr);
                    renderRequests(fp);
                    renderGrants(fg);
                    status.setText("数据清单 " + fr.length() + " 项 · 待确认 " + fp.length() + " 项 · 有效授权 " + fg.length() + " 项");
                });
            } catch (Exception e) {
                runOnUiThread(() -> status.setText("加载失败：" + e.getMessage()));
            }
        });
    }

    private void renderResources(JSONArray rows) {
        resourcesBox.removeAllViews();
        resourceChecks.clear();
        if (rows.length()==0) {
            TextView empty=YanziUiKit.text(this,"暂无可授权数据",13,YanziUiKit.MUTED,false);
            empty.setPadding(YanziUiKit.dp(this,8),YanziUiKit.dp(this,12),0,YanziUiKit.dp(this,12));
            resourcesBox.addView(empty); return;
        }
        for(int i=0;i<rows.length();i++){
            JSONObject scope=rows.optJSONObject(i); if(scope==null)continue;
            CheckBox box=new CheckBox(this);
            box.setChecked(true);
            String extensionId = scope.optString("extensionId", "").trim();
            String resourceName = friendlyResourceName(extensionId);
            String providedName = scope.optString("name", "").trim();
            if (resourceName.equals(extensionId) && !providedName.isEmpty()) resourceName = providedName;
            box.setText(resourceName + "\n" + friendlyResourceDetail(extensionId, scope.optString("key")));
            box.setTextColor(YanziUiKit.TEXT);
            box.setTextSize(13f);
            box.setPadding(YanziUiKit.dp(this,8),YanziUiKit.dp(this,8),YanziUiKit.dp(this,8),YanziUiKit.dp(this,8));
            box.setBackground(YanziUiKit.bg(YanziUiKit.CARD_ALT,14,YanziUiKit.STROKE,1));
            LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.bottomMargin=YanziUiKit.dp(this,7);
            resourcesBox.addView(box,lp); resourceChecks.add(box);
        }
    }

    private void renderRequests(JSONArray rows) {
        requestsBox.removeAllViews();
        boolean visible = rows.length() > 0;
        requestsLabel.setVisibility(visible ? View.VISIBLE : View.GONE);
        requestsBox.setVisibility(visible ? View.VISIBLE : View.GONE);
        if(!visible)return;
        for(int i=0;i<rows.length();i++){
            JSONObject row=rows.optJSONObject(i); if(row==null)continue;
            JSONArray scopes=row.optJSONArray("scopes");
            String detail="核对码 "+row.optString("userCode")+" · "+(scopes==null?1:scopes.length())+" 项数据";
            LinearLayout item=YanziUiKit.row(this,"shield-alert-outline",YanziUiKit.ORANGE,row.optString("clientName","外部 AI"),detail,()->{
                Intent intent=new Intent(this,ExternalAccessActivity.class);
                intent.putExtra("requestId",row.optString("requestId"));
                startActivity(intent);
            });
            LinearLayout.LayoutParams lp=YanziUiKit.cardLp(this);requestsBox.addView(item,lp);
        }
    }

    private void renderGrants(JSONArray rows) {
        grantsBox.removeAllViews();
        boolean visible = rows.length() > 0;
        grantsLabel.setVisibility(visible ? View.VISIBLE : View.GONE);
        grantsBox.setVisibility(visible ? View.VISIBLE : View.GONE);
        if(!visible)return;
        for(int i=0;i<rows.length();i++){
            JSONObject row=rows.optJSONObject(i); if(row==null)continue;
            JSONArray scopes=row.optJSONArray("scopes");
            long expires=row.optLong("expiresAt",0L)*1000L;
            String until=expires>0?DateFormat.format("HH:mm",expires).toString():"—";
            LinearLayout card=YanziUiKit.card(this);
            LinearLayout line=new LinearLayout(this);line.setOrientation(LinearLayout.HORIZONTAL);line.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout copy=new LinearLayout(this);copy.setOrientation(LinearLayout.VERTICAL);
            copy.addView(YanziUiKit.text(this,row.optString("clientName","外部应用"),14,YanziUiKit.TEXT,true));
            copy.addView(YanziUiKit.text(this,(scopes==null?0:scopes.length())+" 项数据 · 有效至 "+until,11,YanziUiKit.MUTED,false));
            line.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
            TextView revoke=YanziUiKit.secondaryButton(this,"撤销",()->revoke(row.optString("requestId")));
            revoke.setTextColor(YanziUiKit.RED);
            line.addView(revoke,new LinearLayout.LayoutParams(YanziUiKit.dp(this,72),YanziUiKit.dp(this,38)));
            card.addView(line);
            grantsBox.addView(card,YanziUiKit.cardLp(this));
        }
    }

    private String friendlyResourceName(String extensionId) {
        if ("quick-notes".equals(extensionId)) return "便签";
        if ("taskbar-calendar".equals(extensionId)) return "日历";
        if ("clipboard-history".equals(extensionId)) return "剪贴板收藏";
        return extensionId == null || extensionId.trim().isEmpty() ? "小程序数据" : extensionId;
    }

    private String friendlyResourceDetail(String extensionId, String key) {
        if ("quick-notes".equals(extensionId)) return "便签数据";
        if ("taskbar-calendar".equals(extensionId)) return "日历数据";
        if ("clipboard-history".equals(extensionId)) return "收藏与分组数据";
        return key == null || key.trim().isEmpty() ? "小程序数据" : key;
    }

    private void createInvitation() {
        JSONArray selected=new JSONArray();
        for(int i=0;i<resourceChecks.size();i++) if(resourceChecks.get(i).isChecked()) selected.put(resources.optJSONObject(i));
        if(selected.length()==0){Toast.makeText(this,"请至少选择一项数据",Toast.LENGTH_SHORT).show();return;}
        final String access=writableSwitch.isChecked()?"read-write":"read";
        status.setText("正在生成限时接入地址…");
        worker.execute(() -> {
            try {
                JSONObject body=new JSONObject().put("resources",selected).put("access",access);
                JSONObject result=MobileMessageClient.request(base,"/v1/applications/access-invites",token,"POST",body);
                String address=result.getString("address");
                String prompt="请访问燕子数据接入地址："+address+
                    "\n先 GET 地址和 resourceList 获取数据清单。根据我的任务只申请必要的 scopes；仅在我明确要求全部时申请 scopes=all。"+
                    "提交 clientName，告诉我核对码，等待手机或电脑确认，按 poll 领取限时授权，再调用返回的 resources 数据接口。"+
                    "修改前读取最新版本；未经我明确要求不要删除数据。";
                runOnUiThread(() -> showPrompt(address,prompt,selected.length(),access));
            } catch(Exception e){runOnUiThread(()->status.setText("生成失败："+e.getMessage()));}
        });
    }

    private void showPrompt(String address,String prompt,int count,String access) {
        LinearLayout box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(YanziUiKit.dp(this,12),YanziUiKit.dp(this,8),YanziUiKit.dp(this,12),0);
        TextView summary=YanziUiKit.text(this,count+" 项数据 · "+("read-write".equals(access)?"可增删改查":"只读"),13,YanziUiKit.GREEN,true);box.addView(summary);
        TextView body=YanziUiKit.text(this,prompt,12,YanziUiKit.TEXT,false);body.setTextIsSelectable(true);body.setPadding(0,YanziUiKit.dp(this,12),0,YanziUiKit.dp(this,8));box.addView(body);
        new AlertDialog.Builder(this)
            .setTitle("AI 接入提示词")
            .setView(box)
            .setPositiveButton("复制提示词",(d,w)->{
                android.content.ClipboardManager cm=(android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
                ClipData clip=ClipData.newPlainText("燕子 AI 接入",prompt);
                PersistableBundle extras=new PersistableBundle();extras.putBoolean("android.content.extra.IS_SENSITIVE",true);clip.getDescription().setExtras(extras);cm.setPrimaryClip(clip);
                Toast.makeText(this,"已复制，发送给你信任的 AI",Toast.LENGTH_SHORT).show();
            })
            .setNeutralButton("复制地址",(d,w)->{
                ((android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("燕子接入地址",address));
            })
            .setNegativeButton("撤销地址",(d,w)->revokeInvite(address))
            .show();
        status.setText("接入地址已生成，等待 AI 发起申请");
    }

    private void revokeInvite(String address) {
        String secret=address.substring(address.lastIndexOf('/')+1);
        worker.execute(()->{
            try{MobileMessageClient.request(base,"/v1/applications/access-invites/"+secret,token,"DELETE",null);
                runOnUiThread(()->{status.setText("接入地址已撤销");loadAll();});
            }catch(Exception e){runOnUiThread(()->status.setText("撤销失败："+e.getMessage()));}
        });
    }

    private void revoke(String requestId) {
        worker.execute(()->{
            try{MobileMessageClient.request(base,"/v1/applications/access-grants/"+requestId,token,"DELETE",null);
                runOnUiThread(()->{Toast.makeText(this,"授权已撤销",Toast.LENGTH_SHORT).show();loadAll();});
            }catch(Exception e){runOnUiThread(()->status.setText("撤销失败："+e.getMessage()));}
        });
    }

    @Override protected void onDestroy(){worker.shutdownNow();super.onDestroy();}
}

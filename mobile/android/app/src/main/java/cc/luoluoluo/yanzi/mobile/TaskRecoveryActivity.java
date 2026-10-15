package cc.luoluoluo.yanzi.mobile;

import android.app.*;
import android.os.Bundle;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.util.*;
import java.util.concurrent.*;

/** Account-scoped task receipts and guarded cloud history recovery. */
public final class TaskRecoveryActivity extends Activity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private MobileSessionStore.Snapshot session;
    private MobileDataRecovery recovery;
    private LinearLayout rows;
    private TextView status;
    private boolean busy, recovering;
    private String objectId;
    private long expectedRevision, before;
    private final List<JSONObject> versions = new ArrayList<>();
    @Override public void onCreate(Bundle state) {
        super.onCreate(state); session = MobileSessionStore.snapshot(this); recovery = MobileDataRecovery.forSession(session);
        LinearLayout page = new LinearLayout(this); page.setOrientation(1); page.setPadding(dp(16),dp(12),dp(16),0); page.setBackgroundColor(YanziUiKit.BG);
        LinearLayout header = new LinearLayout(this); header.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(action("‹ 返回",this::finish));
        header.addView(YanziUiKit.text(this,"任务与恢复",20,YanziUiKit.TEXT,true),new LinearLayout.LayoutParams(0,-2,1));
        header.addView(action("刷新",this::refresh)); page.addView(header);
        LinearLayout tabs = new LinearLayout(this); tabs.addView(action("任务中心",() -> { if(busy)return; recovering=false; objectId=null; refresh(); }),new LinearLayout.LayoutParams(0,dp(48),1));
        tabs.addView(action("数据恢复",() -> { if(busy)return; recovering=true; objectId=null; refresh(); }),new LinearLayout.LayoutParams(0,dp(48),1)); page.addView(tabs);
        status = YanziUiKit.text(this,"",13,YanziUiKit.SECONDARY,false); status.setPadding(0,dp(10),0,dp(10)); page.addView(status);
        ScrollView scroll = new ScrollView(this); rows = new LinearLayout(this); rows.setOrientation(1); scroll.addView(rows); page.addView(scroll,new LinearLayout.LayoutParams(-1,0,1)); setContentView(page); refresh();
    }
    private int dp(int n) { return YanziUiKit.dp(this,n); }
    private TextView action(String text,Runnable run) { TextView v=YanziUiKit.secondaryButton(this,text,run); v.setPadding(dp(12),dp(12),dp(12),dp(12)); return v; }
    private void run(String message, Callable<Runnable> operation) {
        if(busy) return;
        if(session.token.isEmpty()) {status.setText("请先登录燕子账号"); return;}
        busy=true; status.setText(message);
        worker.execute(() -> {
            try { session.requireCurrent(); Runnable result=operation.call(); session.requireCurrent(); runOnUiThread(() -> { busy=false; if(!isFinishing()&&!isDestroyed()) { try {session.requireCurrent();result.run();} catch(Exception changed) {status.setText("账号已改变，请重新打开此页面。");} } }); }
            catch(Exception error) { runOnUiThread(() -> { busy=false; if(!isDestroyed()) status.setText(error instanceof MobileMessageClient.HttpFailure && ((MobileMessageClient.HttpFailure)error).status==409 ? "其他设备已经修改了数据，请刷新历史并重新预览。" : "未完成：" + error.getMessage()); }); }
        });
    }
    private void refresh() {
        if(recovering) { if(objectId==null) loadObjects(); else loadHistory(objectId,false); }
        else run("读取任务记录…",() -> { List<JSONObject> data=MobileTaskJournal.list(session.baseUrl,session.token); return () -> renderTasks(data); });
    }
    private void renderTasks(List<JSONObject> data) {
        rows.removeAllViews(); status.setText("共 " + data.size() + " 条本机任务记录；查询原任务不会重新执行。");
        if(data.isEmpty()) rows.addView(YanziUiKit.text(this,"尚无记录。新版发送的消息、文件及电脑执行请求会显示在这里。",14,YanziUiKit.SECONDARY,false));
        for(JSONObject record:data) {
            LinearLayout card=YanziUiKit.card(this);
            card.addView(YanziUiKit.text(this,record.optString("title") + " · " + MobileTaskJournal.label(record.optString("status")),15,YanziUiKit.TEXT,true));
            String date=java.text.DateFormat.getDateTimeInstance().format(new Date(record.optLong("createdAt")));
            card.addView(YanziUiKit.text(this,date+"\n"+record.optString("kind")+" · "+(record.optString("target").isEmpty()?"账号设备":record.optString("target")),12,YanziUiKit.SECONDARY,false));
            card.addView(action("查看详情",() -> taskDetail(record)));
            rows.addView(card,YanziUiKit.cardLp(this));
        }
    }
    private void taskDetail(JSONObject record) {
        AlertDialog.Builder dialog=new AlertDialog.Builder(this).setTitle(MobileTaskJournal.label(record.optString("status")))
            .setMessage("操作标识："+record.optString("id")+"\n消息标识："+record.optString("messageId","尚未收到回执")+"\n目标："+record.optString("target")+"\n\n"+record.optString("error")+"\n"+record.optString("result"))
            .setNegativeButton("关闭",null);
        if(!record.optString("messageId").isEmpty()&&!MobileTaskJournal.terminal(record.optString("status"))&&!"lan".equals(record.optString("transport")))
            dialog.setPositiveButton("查询原任务",(d,w) -> run("查询原任务…",() -> {
                MobileMessageClient.requestWithoutQueue(session.baseUrl,"/v1/me/mobile/messages/"+MobileApiClient.encodePath(record.getString("messageId")),session.token,"GET",null);
                List<JSONObject> data=MobileTaskJournal.list(session.baseUrl,session.token); return () -> renderTasks(data);
            }));
        if(record.has("companionJobId")&&!MobileTaskJournal.terminal(record.optString("status"))) dialog.setPositiveButton("查询原任务",(d,w)->run("读取原文件处理任务…",()->{
            CompanionTransferProvider.queryExistingTask(this,record.getString("extensionId"),record.getString("companionJobId"));
            List<JSONObject> data=MobileTaskJournal.list(session.baseUrl,session.token);return ()->renderTasks(data);
        }));
        dialog.show();
    }
    private void loadObjects() {
        run("读取云端数据…",() -> { List<JSONObject> objects=recovery.objects(); return () -> {
            rows.removeAllViews(); status.setText("选择数据查看历史。恢复生成新版本；历史中的文件引用不代表原文件仍存在。");
            if(objects.isEmpty()) rows.addView(YanziUiKit.text(this,"尚无云端历史数据",14,YanziUiKit.SECONDARY,false));
            for(JSONObject object:objects) {
                String id=object.optString("objectId"); LinearLayout card=YanziUiKit.card(this);
                card.addView(YanziUiKit.text(this,objectTitle(object)+(object.optBoolean("deleted")?" · 已删除":""),15,YanziUiKit.TEXT,true));
                card.addView(YanziUiKit.text(this,"版本 "+object.optLong("revision")+" · "+object.optString("updatedAtUtc"),12,YanziUiKit.SECONDARY,false));
                card.addView(action("查看历史",() -> loadHistory(id,false))); rows.addView(card,YanziUiKit.cardLp(this));
            }
        }; });
    }
    private String objectTitle(JSONObject object) {
        JSONObject payload=object.optJSONObject("payload");
        if(payload!=null&&!payload.optString("extensionId").isEmpty()) {
            String app=payload.optString("extensionId"); if(app.equals("yanzi-notes"))app="笔记"; if(app.equals("yanzi-album"))app="叶子相册";
            return app+" · "+payload.optString("key","应用数据");
        }
        String id=object.optString("objectId"); return id.equals("yanm.layout")?"燕幕布局":id;
    }
    private void loadHistory(String id,boolean more) {
        run("读取历史版本…",() -> {
            JSONObject current=more?null:recovery.current(id);
            JSONObject response=recovery.history(id,more?before:0);
            JSONArray entries=response.getJSONArray("versions"); List<JSONObject> page=new ArrayList<>(); for(int i=0;i<entries.length();i++) page.add(entries.getJSONObject(i));
            return () -> {
                objectId=id;
                if(!more) {expectedRevision=current.optLong("revision"); versions.clear();}
                versions.addAll(page); if(!page.isEmpty()) before=page.get(page.size()-1).optLong("revision");
                rows.removeAllViews(); status.setText(id+" · 当前版本 "+expectedRevision+"\n先预览内容，再确认恢复。");
                rows.addView(action("‹ 返回数据列表",() -> {objectId=null;loadObjects();}));
                for(JSONObject version:versions) {
                    LinearLayout card=YanziUiKit.card(this); card.addView(YanziUiKit.text(this,"版本 "+version.optLong("revision")+(version.optBoolean("deleted")?" · 删除记录":""),15,YanziUiKit.TEXT,true));
                    card.addView(YanziUiKit.text(this,version.optString("updatedAtUtc")+" · "+version.optString("updatedByDeviceName"),12,YanziUiKit.SECONDARY,false));
                    card.addView(action("预览",() -> preview(id,expectedRevision,version))); rows.addView(card,YanziUiKit.cardLp(this));
                }
                if(response.optBoolean("hasMore")&&!page.isEmpty()) rows.addView(action("加载更早版本",() -> loadHistory(id,true)));
            };
        });
    }
    private void preview(String id,long expected,JSONObject version) {
        long revision=version.optLong("revision");
        TextView content=YanziUiKit.text(this,MobileTaskJournal.clip(version.optJSONObject("payload")==null?"{}":version.optJSONObject("payload").toString()),13,YanziUiKit.TEXT,false);
        content.setTextIsSelectable(true); content.setPadding(dp(16),dp(12),dp(16),dp(12)); ScrollView scroll=new ScrollView(this); scroll.setBackgroundColor(YanziUiKit.BG); scroll.addView(content);
        AlertDialog.Builder dialog=new AlertDialog.Builder(this).setTitle("历史版本 "+revision).setView(scroll).setNegativeButton("关闭",null);
        if(!version.optBoolean("deleted")&&revision!=expected&&!indexed(id)) dialog.setPositiveButton("恢复此版本",(d,w) -> new AlertDialog.Builder(this)
            .setTitle("确认恢复").setMessage("将整个数据对象恢复到版本 "+revision+"？对象可能包含多条笔记或设置。会生成新版本。如果数据已经改变，本次恢复会被拒绝。")
            .setNegativeButton("取消",null).setPositiveButton("确认恢复",(a,b) -> run("正在恢复，请勿重复操作…",() -> {
                JSONObject restored=recovery.restore(id,expected,revision,session.deviceId);
                return () -> { rows.removeAllViews(); status.setText("已恢复，生成版本 "+restored.optLong("revision")+"。返回数据列表可继续查看。其他设备会通过正常同步获取新版本。"); rows.addView(action("返回数据列表",() -> {objectId=null;loadObjects();})); };
            })).show());
        if(indexed(id)) dialog.setNeutralButton("关联布局说明",(d,w) -> new AlertDialog.Builder(this).setMessage("此对象属于布局索引，请从电脑燕窝的数据恢复入口恢复，以同时修复关联索引。").setPositiveButton("知道了",null).show());
        dialog.show();
    }
    private boolean indexed(String id) { return id.startsWith("yanm.componentState.") || id.startsWith("quickPanel.globalGroup.") || id.startsWith("quickPanel.contextGroup.") || id.startsWith("radialMenu.page."); }
    @Override protected void onResume() {
        super.onResume();
        if(session==null||session.token.isEmpty())return;
        worker.execute(()->{
            try { session.requireCurrent(); MobileDeviceSession.requireRegistered(session.baseUrl,session.token,session.deviceId); }
            catch(MobileApiClient.MissingSourceDeviceException removed) {
                try { session.requireCurrent();MobileDeviceSession.clearRemovedLogin(this);
                    runOnUiThread(()->{if(!isDestroyed()){rows.removeAllViews();status.setText("本机已从账号中删除，请返回我的重新登录。");}});
                } catch(Exception changed) { }
            } catch(Exception offline) { /* Retain local task history while offline. */ }
        });
    }
    @Override protected void onDestroy(){worker.shutdownNow();super.onDestroy();}
}

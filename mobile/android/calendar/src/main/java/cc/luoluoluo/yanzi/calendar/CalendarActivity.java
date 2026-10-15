package cc.luoluoluo.yanzi.calendar;

import android.app.*;
import android.os.*;
import android.content.Intent;
import android.graphics.Color;
import android.widget.*;
import org.json.*;
import java.time.*;
import java.util.concurrent.*;

public final class CalendarActivity extends Activity {
    private CalendarStore store;
    private LinearLayout list;
    private TextView status, heading;
    private String selected = LocalDate.now().toString();
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean running, active;
    private final Runnable refresh = new Runnable() { public void run() {
        if (!active) return; sync(); handler.postDelayed(this, 10000);
    }};
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root = new LinearLayout(this); root.setOrientation(1); root.setPadding(24, 32, 24, 12);
        root.setBackgroundColor(Color.rgb(247,249,252));
        TextView title = new TextView(this); title.setText("燕子日历"); title.setTextSize(26); root.addView(title);
        status = new TextView(this); status.setText("正在连接燕子…"); root.addView(status);
        LinearLayout tools = new LinearLayout(this); root.addView(tools);
        button(tools, "同步", v -> sync());
        button(tools, "打开燕子", v -> {
            Intent launch = getPackageManager().getLaunchIntentForPackage(BuildConfig.HOST_PACKAGE);
            if (launch == null) status.setText("请先安装同一版本通道的燕子 APP"); else startActivity(launch);
        });
        button(tools, "新增", v -> editor(null));
        CalendarView calendar = new CalendarView(this); root.addView(calendar);
        calendar.setOnDateChangeListener((view, y, m, d) -> { selected = LocalDate.of(y,m+1,d).toString(); render(); });
        heading = new TextView(this); heading.setTextSize(20); root.addView(heading);
        ScrollView scroll = new ScrollView(this); root.addView(scroll, new LinearLayout.LayoutParams(-1,0,1));
        list = new LinearLayout(this); list.setOrientation(1); scroll.addView(list);
        setContentView(root);
        try { store = new CalendarStore(this); render(); } catch (Exception e) { status.setText("本地日历读取失败，未覆盖文件"); }
    }
    private void button(LinearLayout parent, String text, android.view.View.OnClickListener action) {
        Button b = new Button(this); b.setText(text); b.setOnClickListener(action); parent.addView(b);
    }
    @Override protected void onResume() { super.onResume(); active = true; handler.post(refresh); }
    @Override protected void onPause() { active = false; handler.removeCallbacks(refresh); super.onPause(); }
    @Override protected void onDestroy() { handler.removeCallbacksAndMessages(null); worker.shutdown(); super.onDestroy(); }
    private void sync() {
        if (store == null || running || worker.isShutdown()) return;
        running = true; status.setText("同步中…");
        worker.execute(() -> {
            String message;
            try { message = store.sync(); }
            catch (SecurityException e) { message = "燕子与日历签名或版本通道不一致"; }
            catch (Exception e) { message = "暂未同步：" + e.getMessage() + "；本地修改已保留"; }
            String result = message;
            runOnUiThread(() -> { running = false; if (!isDestroyed()) { status.setText(result); render(); } });
        });
    }
    private void render() {
        if (store == null) return; heading.setText(selected); list.removeAllViews();
        try {
            JSONArray items = store.visible(selected);
            if (items.length() == 0) { TextView empty = new TextView(this); empty.setText("当天没有事项，点击「新增」添加"); list.addView(empty); }
            for (int i=0; i<items.length(); i++) {
                JSONObject item = items.getJSONObject(i); String id = item.getString("Id");
                Button row = new Button(this); row.setAllCaps(false); row.setGravity(3);
                String alarm = item.optString("AlarmTime", "");
                row.setText(item.optString("Title") + (item.optBoolean("IsAlarm") && alarm.length() >= 16 ? "\n" + alarm.substring(11,16) + " · 闹钟" : " · 待办")
                        + (store.conflicted(id) ? "\n待同步 / 长按处理冲突" : ""));
                row.setOnClickListener(v -> editor(item));
                row.setOnLongClickListener(v -> { new AlertDialog.Builder(this).setTitle("事项操作")
                    .setItems(new String[]{"删除", "使用云端版本", "保留本地版本并重新同步"}, (d, choice) -> {
                        if (choice == 0) new AlertDialog.Builder(this).setMessage("删除此事项并同步到电脑？")
                            .setNegativeButton("取消", null).setPositiveButton("删除", (dialog, which) -> mutate(() -> store.edit(id,item,true))).show();
                        else worker.execute(() -> { try { if (choice == 1) store.useCloud(id); else store.keepLocal(id);
                            runOnUiThread(this::sync); } catch(Exception e) { runOnUiThread(() -> status.setText("处理失败，修改已保留")); } });
                    }).show(); return true; });
                list.addView(row);
            }
        } catch (Exception e) { status.setText("事项读取失败，未删除原数据"); }
    }
    private interface Mutation { void run() throws Exception; }
    private void mutate(Mutation operation) {
        try { operation.run(); render(); sync(); } catch (Exception e) { status.setText(e.getMessage()); }
    }
    private void editor(JSONObject original) {
        if (store == null || !store.ready()) { status.setText("请先打开燕子登录，再同步一次"); return; }
        LinearLayout form = new LinearLayout(this); form.setOrientation(1); form.setPadding(32,16,32,8);
        EditText title = new EditText(this); title.setHint("事项标题"); title.setText(original == null ? "" : original.optString("Title")); form.addView(title);
        EditText date = new EditText(this); date.setHint("日期 YYYY-MM-DD"); date.setText(original == null ? selected : original.optString("TargetDate").substring(0,10)); form.addView(date);
        CheckBox alarm = new CheckBox(this); alarm.setText("同步为闹钟（电脑端响铃）"); alarm.setChecked(original != null && original.optBoolean("IsAlarm")); form.addView(alarm);
        EditText time = new EditText(this); time.setHint("时间 HH:mm");
        String oldTime = original == null ? "" : original.optString("AlarmTime", "");
        time.setText(oldTime.length() >=19 ? oldTime.substring(11,19) : oldTime.length() >=16 ? oldTime.substring(11,16) : "09:00"); form.addView(time);
        AlertDialog dialog = new AlertDialog.Builder(this).setTitle(original == null ? "新增事项" : "编辑事项")
            .setView(form).setNegativeButton("取消",null).setPositiveButton("保存",null).create();
        dialog.setOnShowListener(d -> dialog.getButton(-1).setOnClickListener(v -> {
            try {
                if (title.getText().toString().trim().isEmpty()) { title.setError("请输入标题"); return; }
                LocalDate day = LocalDate.parse(date.getText().toString().trim());
                LocalTime at = alarm.isChecked() ? LocalTime.parse(time.getText().toString().trim()) : null;
                JSONObject item = original == null ? new JSONObject() : new JSONObject(original.toString());
                String id = item.optString("Id", java.util.UUID.randomUUID().toString().replace("-", ""));
                String alarmValue = at == null ? "" : day+"T"+at.format(java.time.format.DateTimeFormatter.ofPattern("HH:mm:ss"));
                boolean alreadyTriggered = original != null && original.optBoolean("IsTriggered")
                        && original.optBoolean("IsAlarm") == alarm.isChecked() && oldTime.equals(alarmValue);
                item.put("Id",id).put("Title",title.getText().toString().trim())
                    .put("TargetDate",day+"T00:00:00").put("IsAlarm",alarm.isChecked())
                    .put("AlarmTime", at == null ? JSONObject.NULL : alarmValue)
                    .put("IsTriggered",alreadyTriggered);
                if (!item.has("CreatedTime")) item.put("CreatedTime",LocalDateTime.now().toString());
                store.edit(id,item,false); dialog.dismiss(); selected = day.toString(); render(); sync();
            } catch (Exception e) { date.setError("请检查日期和时间格式：" + e.getMessage()); }
        })); dialog.show();
    }
}

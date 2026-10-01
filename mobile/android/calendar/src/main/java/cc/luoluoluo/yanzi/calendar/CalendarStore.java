package cc.luoluoluo.yanzi.calendar;

import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import org.json.*;
import java.util.*;

/** Durable per-account outbox and record-level optimistic concurrency. */
final class CalendarStore {
    static final String KEY = "calendar.v1.json";
    private final Context context;
    private final SharedPreferences prefs;
    interface Transport { JSONObject call(String method, String content, long revision) throws Exception; }
    private final Transport transport;
    private String account;
    JSONObject records = new JSONObject(), pending = new JSONObject();
    CalendarStore(Context c) throws Exception { this(c, null); }
    CalendarStore(Context c, Transport transport) throws Exception {
        this.transport = transport;
        context = c; prefs = c.getSharedPreferences("calendar", 0);
        account = prefs.getString("account", ""); load();
    }
    synchronized boolean ready() { return !account.isEmpty(); }
    private void load() throws Exception {
        JSONObject cache = new JSONObject(prefs.getString("cache." + account, "{}"));
        records = cache.optJSONObject("records"); if (records == null) records = new JSONObject();
        pending = cache.optJSONObject("pending"); if (pending == null) pending = new JSONObject();
    }
    private void save() throws Exception {
        if (!prefs.edit().putString("cache." + account,
                new JSONObject().put("records", records).put("pending", pending).toString()).commit())
            throw new IllegalStateException("本地保存失败");
    }
    synchronized void edit(String id, JSONObject item, boolean deleted) throws Exception {
        if (!ready()) throw new IllegalStateException("请先连接燕子并同步一次");
        JSONObject prior = records.optJSONObject(id), oldChange = pending.optJSONObject(id);
        String expected = oldChange == null ? (prior == null ? "" : prior.optString("version")) : oldChange.optString("expected");
        JSONObject record = new JSONObject().put("version", UUID.randomUUID().toString()).put("deleted", deleted).put("item", item);
        pending.put(id, new JSONObject().put("expected", expected).put("record", record));
        records.put(id, record); save();
    }
    synchronized JSONArray visible(String date) throws Exception {
        JSONArray out = new JSONArray();
        Iterator<String> keys = records.keys();
        while (keys.hasNext()) {
            JSONObject record = records.getJSONObject(keys.next());
            JSONObject item = record.optJSONObject("item");
            if (!record.optBoolean("deleted") && item != null && item.optString("TargetDate").startsWith(date))
                out.put(new JSONObject(item.toString()));
        }
        return out;
    }
    private JSONObject call(String method, String content, long revision) throws Exception {
        if (transport != null) return transport.call(method, content, revision);
        Bundle args = new Bundle(); args.putString("key", KEY);
        args.putString("content", content); args.putLong("expectedRevision", revision);
        args.putString("accountId", account);
        Bundle out = context.getContentResolver().call(Uri.parse("content://" + BuildConfig.HOST_PACKAGE
                + ".extension-storage"), method, "taskbar-calendar", args);
        if (out == null) throw new IllegalStateException("燕子版本过旧，请更新燕子");
        JSONObject result = new JSONObject(out.getString("result", "{}"));
        if (!result.optBoolean("ok") && !result.optBoolean("conflict"))
            throw new IllegalStateException(result.optString("error", "连接失败"));
        return result;
    }
    String sync() throws Exception {
        JSONObject read = call("read", "", 0);
        String remoteAccount = read.optString("accountId");
        if (remoteAccount.isEmpty()) throw new IllegalStateException("无法确认账号");
        JSONObject changes;
        synchronized (this) {
            if (!remoteAccount.equals(account)) {
                account = remoteAccount; prefs.edit().putString("account", account).commit(); load();
            }
            changes = new JSONObject(pending.toString());
        }
        JSONObject document = read.optBoolean("exists") ? new JSONObject(read.getString("content"))
                : new JSONObject().put("schemaVersion", 1).put("records", new JSONObject());
        if (document.optInt("schemaVersion") != 1 || document.optJSONObject("records") == null)
            throw new IllegalStateException("日历数据版本不支持，未覆盖数据");
        JSONObject remote = document.getJSONObject("records");
        List<String> accepted = new ArrayList<>(); int conflicts = 0;
        Iterator<String> keys = changes.keys();
        while (keys.hasNext()) {
            String id = keys.next(); JSONObject change = changes.getJSONObject(id);
            JSONObject current = remote.optJSONObject(id), record = change.getJSONObject("record");
            String version = current == null ? "" : current.optString("version");
            if (version.equals(record.getString("version"))) { accepted.add(id); continue; }
            if (!version.equals(change.optString("expected"))) { conflicts++; continue; }
            remote.put(id, record); accepted.add(id);
        }
        if (!accepted.isEmpty()) {
            JSONObject write = call("write", document.toString(), read.optLong("revision"));
            if (write.optBoolean("conflict")) return "同步遇到新版本，稍后重试；本地修改已保留";
            if (!account.equals(write.optString("accountId"))) return "账号已切换，修改已保留在原账号";
        }
        synchronized (this) {
            for (String id : accepted) {
                JSONObject current = pending.optJSONObject(id), done = changes.getJSONObject(id);
                if (current == null) continue;
                if (current.getJSONObject("record").getString("version").equals(done.getJSONObject("record").getString("version"))) pending.remove(id);
                else if (current.optString("expected").equals(done.optString("expected")))
                    current.put("expected", done.getJSONObject("record").getString("version"));
            }
            records = new JSONObject(remote.toString());
            keys = pending.keys(); while (keys.hasNext()) {
                String id = keys.next(); records.put(id, pending.getJSONObject(id).getJSONObject("record"));
            }
            save();
        }
        return conflicts > 0 ? conflicts + " 项有冲突，本地修改已保留；长按事项处理"
                : "已同步电脑日历 · " + new java.text.SimpleDateFormat("HH:mm:ss", Locale.CHINA).format(new Date());
    }
    synchronized boolean conflicted(String id) { return pending.has(id); }
    synchronized void useCloud(String id) throws Exception { pending.remove(id); save(); }
    void keepLocal(String id) throws Exception {
        JSONObject read = call("read", "", 0);
        if (!account.equals(read.optString("accountId"))) throw new IllegalStateException("账号已切换");
        JSONObject remote = new JSONObject(read.getString("content")).getJSONObject("records");
        synchronized (this) {
            JSONObject current = remote.optJSONObject(id), change = pending.optJSONObject(id);
            if (change != null) { change.put("expected", current == null ? "" : current.optString("version")); save(); }
        }
    }
}

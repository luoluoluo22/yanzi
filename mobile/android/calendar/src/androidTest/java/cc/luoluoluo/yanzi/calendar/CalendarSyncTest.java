package cc.luoluoluo.yanzi.calendar;

import android.test.InstrumentationTestCase;
import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import org.json.*;

public final class CalendarSyncTest extends InstrumentationTestCase {
    private Context context;
    private JSONObject call(String op, String ext, String text, long rev) throws Exception {
        Bundle args = new Bundle(); args.putString("key", CalendarStore.KEY);
        args.putString("content",text); args.putLong("expectedRevision",rev);
        args.putString("accountId",((android.test.InstrumentationTestRunner)getInstrumentation()).getArguments().getString("accountId"));
        Bundle value = context.getContentResolver().call(Uri.parse("content://"+BuildConfig.HOST_PACKAGE
                +".extension-storage"),op,ext,args);
        return new JSONObject(value.getString("result"));
    }
    public void testDesktopMobileRoundtripConflictAndDeletion() throws Exception {
        context=getInstrumentation().getTargetContext();
        CalendarStore store=new CalendarStore(context);
        assertTrue(store.sync().startsWith("已同步"));
        assertEquals("desktop-seed",store.visible("2080-01-02").getJSONObject(0).getString("Title"));
        JSONObject item=new JSONObject(store.visible("2080-01-02").getJSONObject(0).toString());
        String id=item.getString("Id"); item.put("Title","mobile-edit");
        store.edit(id,item,false); assertTrue(store.sync().startsWith("已同步"));
        JSONObject remote=call("read","taskbar-calendar","",0);
        assertEquals("mobile-edit",new JSONObject(remote.getString("content")).getJSONObject("records")
                .getJSONObject(id).getJSONObject("item").getString("Title"));
        JSONObject stale=call("write","taskbar-calendar",remote.getString("content"),0);
        assertTrue(stale.getBoolean("conflict"));
        CalendarStore offline = new CalendarStore(context, (method, text, revision) -> {
            throw new java.io.IOException("network unavailable");
        });
        item.put("Title", "offline-draft"); offline.edit(id,item,false);
        try { offline.sync(); fail("Offline transport must fail"); } catch(java.io.IOException expected) {}
        store = new CalendarStore(context);
        assertTrue(store.pending.has(id));
        assertEquals("offline-draft", store.visible("2080-01-02").getJSONObject(0).getString("Title"));
        assertTrue(store.sync().startsWith("已同步"));
        assertFalse(store.pending.has(id));
        remote=call("read","taskbar-calendar","",0);
        boolean denied=false;
        try {call("read","another-extension","",0);}catch(SecurityException expected){denied=true;}
        assertTrue("Companion may not read other extensions",denied);
        // Simulate a concurrent desktop change after the phone read its base version.
        JSONObject doc=new JSONObject(remote.getString("content"));
        JSONObject record=doc.getJSONObject("records").getJSONObject(id);
        record.put("version","desktop-concurrent"); record.getJSONObject("item").put("Title","desktop-concurrent");
        assertTrue(call("write","taskbar-calendar",doc.toString(),remote.getLong("revision")).getBoolean("ok"));
        item.put("Title","mobile-conflicting-draft"); store.edit(id,item,false);
        assertTrue(store.sync().contains("冲突")); assertTrue(store.pending.has(id));
        assertEquals("mobile-conflicting-draft",new CalendarStore(context).visible("2080-01-02").getJSONObject(0).getString("Title"));
        assertEquals("desktop-concurrent",new JSONObject(call("read","taskbar-calendar","",0).getString("content"))
                .getJSONObject("records").getJSONObject(id).getJSONObject("item").getString("Title"));
        store.keepLocal(id); assertTrue(store.sync().startsWith("已同步")); assertFalse(store.pending.has(id));
        store.edit(id,item,true); assertTrue(store.sync().startsWith("已同步"));
        assertEquals(0,store.visible("2080-01-02").length());
        assertTrue(new JSONObject(call("read","taskbar-calendar","",0).getString("content"))
                .getJSONObject("records").getJSONObject(id).getBoolean("deleted"));
        // Add a second fixture for the Windows reader to validate the return path.
        JSONObject back=new JSONObject(item.toString()).put("Id","mobile-return").put("Title","mobile-return")
                .put("TargetDate","2080-01-03T00:00:00");
        store.edit("mobile-return",back,false); assertTrue(store.sync().startsWith("已同步"));
    }
}

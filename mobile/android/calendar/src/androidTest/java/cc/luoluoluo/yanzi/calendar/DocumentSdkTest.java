package cc.luoluoluo.yanzi.calendar;
import android.test.AndroidTestCase;
import android.content.SharedPreferences;
import cc.luoluoluo.yanzi.sdk.*;
import org.json.JSONObject;
import java.io.IOException;
import java.util.concurrent.*;

public final class DocumentSdkTest extends AndroidTestCase {
    private SharedPreferences preferences(){SharedPreferences p=getContext().getSharedPreferences("document-sdk-tests",0);p.edit().clear().commit();return p;}
    private static class Storage implements YanziStorage {
        boolean online=true;long revision=0;String content="";
        public JSONObject read(String key)throws Exception{if(!online)throw new IOException("offline");return new JSONObject().put("accountId","a").put("revision",revision).put("content",content).put("ok",true);}
        public JSONObject write(String key,String value,long expected,String account)throws Exception{
            if(expected!=revision)return new JSONObject().put("ok",false).put("conflict",true);
            content=value;revision++;return read(key);
        }
    }
    public void testOfflineRecoveryAndConflict()throws Exception{
        SharedPreferences p=preferences();Storage storage=new Storage();
        YanziDocument doc=new YanziDocument(storage,p,"example","data","a");doc.save("draft");storage.online=false;
        try{doc.sync();fail("Offline request succeeded");}catch(IOException expected){}
        doc=new YanziDocument(storage,p,"example","data","a");assertEquals("draft",doc.content());storage.online=true;assertEquals("synced",doc.sync());
        doc.save("local");storage.content="remote";storage.revision++;
        assertEquals("conflict",doc.sync());assertEquals("local",doc.content());doc.resolve(false);doc.sync();assertEquals("local",storage.content);
        YanziDocument other=new YanziDocument(storage,p,"example","data","b");assertEquals("",other.content());
        try{other.sync();fail("Account switch accepted");}catch(IllegalStateException expected){}
    }
    public void testEditDuringUploadIsRetained()throws Exception{
        SharedPreferences p=preferences();CountDownLatch started=new CountDownLatch(1),finish=new CountDownLatch(1);
        Storage storage=new Storage(){public JSONObject write(String key,String value,long expected,String account)throws Exception{started.countDown();if(!finish.await(10,TimeUnit.SECONDS))throw new IOException("timeout");return super.write(key,value,expected,account);}};
        YanziDocument doc=new YanziDocument(storage,p,"example","data","a");doc.save("first");
        String[] result={null};Throwable[] error={null};Thread thread=new Thread(()->{try{result[0]=doc.sync();}catch(Throwable e){error[0]=e;}});thread.start();
        assertTrue(started.await(10,TimeUnit.SECONDS));doc.save("second");finish.countDown();thread.join(10000);
        assertNull(error[0]);assertEquals("pending",result[0]);assertEquals("second",doc.content());assertEquals("synced",doc.sync());assertEquals("second",storage.content);
    }
}

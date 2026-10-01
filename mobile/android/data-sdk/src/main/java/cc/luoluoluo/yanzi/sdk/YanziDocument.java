package cc.luoluoluo.yanzi.sdk;
import android.content.SharedPreferences;
import org.json.JSONObject;
/** Persistent, account-isolated snapshot outbox. Invoke sync on a background thread. */
public final class YanziDocument {
    private final YanziStorage storage;private final SharedPreferences prefs;private final String cacheKey,key,account;
    private JSONObject state;
    private final Object syncGate=new Object();
    public YanziDocument(YanziStorage storage,SharedPreferences prefs,String extensionId,String key,String accountId)throws Exception{
        this.storage=storage;this.prefs=prefs;this.key=key;this.account=accountId;
        this.cacheKey="document.v1."+new org.json.JSONArray().put(accountId).put(extensionId).put(key).toString();
        state=new JSONObject(prefs.getString(cacheKey,"{\"revision\":0,\"content\":\"\",\"dirty\":false,\"generation\":0}"));
    }
    private void persist(){if(!prefs.edit().putString(cacheKey,state.toString()).commit())throw new IllegalStateException("Draft could not be saved");}
    public synchronized String content(){return state.optString("content");}
    public synchronized void save(String content)throws Exception{if(content==null)throw new IllegalArgumentException("Content must be text");state.put("content",content).put("dirty",true).put("generation",state.optLong("generation")+1);persist();}
    public String sync()throws Exception{synchronized(syncGate){
        JSONObject remote=storage.read(key);
        if(!account.equals(remote.optString("accountId")))throw new IllegalStateException("Account changed");
        String content;long generation;
        synchronized(this){
        if(!state.optBoolean("dirty")||state.optString("content").equals(remote.optString("content"))){state.put("revision",remote.getLong("revision")).put("content",remote.optString("content")).put("dirty",false);state.remove("conflict");persist();return "synced";}
        if(remote.getLong("revision")!=state.optLong("revision")){state.put("conflict",remote);persist();return "conflict";}
        content=state.getString("content");generation=state.optLong("generation");
        }
        JSONObject written=storage.write(key,content,remote.getLong("revision"),account);
        if(written.optBoolean("conflict"))return "retry";
        if(!account.equals(written.optString("accountId")))throw new IllegalStateException("Account changed");
        synchronized(this){boolean pending=state.optLong("generation")!=generation;
        state.put("revision",written.getLong("revision")).put("dirty",pending);state.remove("conflict");persist();return pending?"pending":"synced";}
    }}
    public synchronized void resolve(boolean useCloud)throws Exception{
        JSONObject remote=state.getJSONObject("conflict");state.put("revision",remote.getLong("revision"));
        if(useCloud)state.put("content",remote.optString("content")).put("dirty",false);
        state.put("generation",state.optLong("generation")+1);state.remove("conflict");persist();
    }
}

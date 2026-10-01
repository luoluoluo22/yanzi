package cc.luoluoluo.yanzi.sdk;
import android.content.Context;
import android.net.Uri;
import android.os.Bundle;
import org.json.JSONObject;
/** Same-signature companion bridge; host credentials never leave the host. */
public final class HostStorage implements YanziStorage {
    private final Context context; private final Uri uri; private final String extensionId;
    public HostStorage(Context context,String hostPackage,String extensionId){
        this.context=context.getApplicationContext();this.uri=Uri.parse("content://"+hostPackage+".extension-storage");this.extensionId=extensionId;
    }
    public JSONObject read(String key)throws Exception{return call("read",key,"",0,"");}
    public JSONObject write(String key,String content,long revision,String accountId)throws Exception{return call("write",key,content,revision,accountId);}
    private JSONObject call(String method,String key,String content,long revision,String account)throws Exception{
        Bundle args=new Bundle();args.putString("key",key);args.putString("content",content);args.putLong("expectedRevision",revision);args.putString("accountId",account);
        Bundle out=context.getContentResolver().call(uri,method,extensionId,args);
        if(out==null)throw new IllegalStateException("Host unavailable");
        JSONObject result=new JSONObject(out.getString("result","{}"));
        if(!result.optBoolean("ok")&&!result.optBoolean("conflict"))throw new IllegalStateException(result.optString("error","Host request failed"));
        return result;
    }
}

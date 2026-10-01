package cc.luoluoluo.yanzi.sdk;
import org.json.JSONObject;
/** All transports share the same revision, account and content envelope. */
public interface YanziStorage {
    JSONObject read(String key) throws Exception;
    JSONObject write(String key,String content,long revision,String accountId) throws Exception;
}

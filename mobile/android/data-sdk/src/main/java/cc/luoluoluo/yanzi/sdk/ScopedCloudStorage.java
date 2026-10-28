package cc.luoluoluo.yanzi.sdk;
import org.json.JSONObject;
import java.net.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
/** For independently signed apps. TokenSource returns a user-approved scoped token. */
public final class ScopedCloudStorage implements YanziStorage {
    public interface TokenSource {String get() throws Exception;}
    private final String baseUrl,extensionId;private final TokenSource token;
    public ScopedCloudStorage(String baseUrl,String extensionId,TokenSource token){
        if(!baseUrl.startsWith("https://"))throw new IllegalArgumentException("HTTPS required");
        this.baseUrl=baseUrl.replaceAll("/+$","");this.extensionId=extensionId;this.token=token;
    }
    public JSONObject read(String key)throws Exception{return call(key,null);}
    public JSONObject write(String key,String content,long revision,String accountId)throws Exception{
        return call(key,new JSONObject().put("content",content).put("expectedRevision",revision).put("accountId",accountId));
    }
    private JSONObject call(String key,JSONObject input)throws Exception{
        URL url=new URL(baseUrl+"/v1/extension-data/"+URLEncoder.encode(extensionId,"UTF-8")+"?key="+URLEncoder.encode(key,"UTF-8"));
        HttpURLConnection c=(HttpURLConnection)url.openConnection();c.setInstanceFollowRedirects(false);c.setConnectTimeout(15000);c.setReadTimeout(20000);
        c.setRequestProperty("Authorization","Bearer "+token.get());
        try{
            if(input!=null){c.setRequestMethod("PUT");c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json");try(OutputStream out=c.getOutputStream()){out.write(input.toString().getBytes(StandardCharsets.UTF_8));}}
            int status=c.getResponseCode();InputStream stream=status<400?c.getInputStream():c.getErrorStream();
            if(stream==null)throw new IOException("HTTP "+status);
            JSONObject result;
            try(InputStream in=stream;ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
                byte[] b=new byte[8192];int n;while((n=in.read(b))!=-1){if(bytes.size()+n>2*1024*1024)throw new IOException("Response too large");bytes.write(b,0,n);}
                result=new JSONObject(bytes.toString("UTF-8"));
            }
            if(status==409&&"sync_revision_conflict".equals(result.optString("error")))return result.put("ok",false).put("conflict",true);
            if(status!=200)throw new IOException("HTTP "+status+": "+result.optString("error"));return result;
        }finally{c.disconnect();}
    }
}

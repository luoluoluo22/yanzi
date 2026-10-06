package cc.luoluoluo.yanzi.mobile;
import android.content.*;
import android.content.pm.*;
import android.net.Uri;
import android.os.*;
import android.database.Cursor;
import org.json.*;
import cc.luoluoluo.yanzi.sdk.DeviceTargets;
import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.*;

/** Signature-scoped companion files/workflows. The host retains all account credentials. */
public final class CompanionTransferProvider extends ContentProvider {
    private static final ExecutorService WORK=Executors.newSingleThreadExecutor();
    private static final Set<String> RUNNING=ConcurrentHashMap.newKeySet();
    public boolean onCreate(){MobileNetworkRouting.initialize(getContext());return true;}
    private void authorize(String ext)throws Exception {
        Context c=getContext();c.enforceCallingOrSelfPermission(c.getPackageName()+".permission.EXTENSION_STORAGE","Companion permission required");
        String caller=getCallingPackage();
        if(caller==null || c.getPackageManager().checkSignatures(caller,c.getPackageName())!=PackageManager.SIGNATURE_MATCH)throw new SecurityException("Untrusted companion");
        ApplicationInfo app=c.getPackageManager().getApplicationInfo(caller,PackageManager.GET_META_DATA);
        if(!ext.matches("[a-z0-9-]{1,80}") || app.metaData==null || !Arrays.asList(app.metaData.getString("yanzi.extensionScopes","").split(",")).contains(ext))throw new SecurityException("Companion scope denied");
    }
    private static File root(Context c)throws Exception {
        String account=SecureLanConnection.currentAccount(c);if(account.equals("local"))throw new IOException("LOGIN_REQUIRED");
        String hash=MobileAttachmentClient.hashString(c.getSharedPreferences("yanzi-mobile",0).getString("baseUrl","")+"\n"+account);
        File root=new File(c.getFilesDir(),"companion-transfers/"+hash);root.mkdirs();return root;
    }
    private static File job(Context c,String ext,String id)throws Exception {if(!ext.matches("[a-z0-9-]{1,80}")||!id.matches("[a-f0-9]{32}"))throw new IOException("INVALID_JOB");return new File(root(c),ext+"-"+id+".json");}
    private static synchronized void save(File file,JSONObject value)throws Exception {
        File temp=new File(file+".tmp");try(FileOutputStream out=new FileOutputStream(temp)){out.write(value.toString().getBytes("UTF-8"));out.getFD().sync();}
        if(!temp.renameTo(file))throw new IOException("JOB_COMMIT_FAILED");
    }
    private static JSONObject read(File file)throws Exception {try(FileInputStream in=new FileInputStream(file);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[]b=new byte[4096];int n;while((n=in.read(b))>0){out.write(b,0,n);if(out.size()>65536)throw new IOException("JOB_TOO_LARGE");}return new JSONObject(out.toString("UTF-8"));}}
    public synchronized Bundle call(String method,String ext,Bundle args){
        try {
            authorize(ext);Context c=getContext();android.content.SharedPreferences prefs=c.getSharedPreferences("yanzi-mobile",0);
            String token=prefs.getString("token",""),base=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd");
            if(token.isEmpty())throw new IOException("LOGIN_REQUIRED");
            JSONObject value;
            if(method.equals("devices")){value=MobileMessageClient.requestWithoutQueue(base,"/v1/me/devices",token,"GET",null);value.put("accountId",SecureLanConnection.currentAccount(c));}
            else if(method.equals("handoff")||method.equals("handoff-status")) {
                ApplicationInfo app=c.getPackageManager().getApplicationInfo(getCallingPackage(),PackageManager.GET_META_DATA);
                if(app.metaData==null||!app.metaData.getBoolean("yanzi.handoff",false))throw new SecurityException("Handoff scope denied");
                String account=SecureLanConnection.currentAccount(c);
                if(!account.equals(args.getString("accountId","")))throw new IOException("ACCOUNT_CHANGED");
                if(method.equals("handoff-status")){
                    String id=args.getString("messageId","");if(!id.matches("[a-zA-Z0-9_-]{1,100}"))throw new IOException("INVALID_MESSAGE");
                    value=MobileMessageClient.requestWithoutQueue(base,"/v1/me/mobile/messages/"+id,token,"GET",null);
                    JSONObject payload=value.optJSONObject("payload");
                    if(!"extension.handoff".equals(value.optString("kind"))||payload==null||!ext.equals(payload.optString("extensionId"))||!account.equals(payload.optString("accountId")))throw new SecurityException("Handoff scope denied");
                } else {
                    String input=args.getString("input","");
                    if(input.isEmpty()||input.length()>4096)throw new IOException("INVALID_INPUT");
                    JSONArray devices=MobileMessageClient.requestWithoutQueue(base,"/v1/me/devices",token,"GET",null).getJSONArray("items");
                    String target=resolveDesktopTarget(devices,args.getString("targetDeviceId",""));
                    if(!account.equals(SecureLanConnection.currentAccount(c)))throw new IOException("ACCOUNT_CHANGED");
                    MobileApplicationContext.initialize(c);String id=java.util.UUID.randomUUID().toString();
                    JSONObject envelope=new JSONObject().put("kind","extension.handoff").put("sourceDeviceId",prefs.getString("deviceId",""))
                        .put("targetDeviceId",target).put("targetPlatform","desktop").put("clientMessageId",id).put("expiresAt",java.time.Instant.now().plusSeconds(86400).toString())
                        .put("payload",new JSONObject().put("extensionId",ext).put("input",input).put("accountId",account).put("clientOperationId",id));
                    java.io.File queued=MobileMessageOutbox.save(base,token,envelope);
                    try{value=MobileMessageClient.requestWithoutQueue(base,"/v1/me/mobile/messages",token,"POST",envelope);MobileMessageOutbox.complete(queued);value.put("queued",true);}
                    catch(Exception network){value=new JSONObject().put("queued",true).put("localOnly",true).put("clientMessageId",id);}
                }
            }
            else if(method.equals("submit")) {
                String id=args.getString("jobId","");File record=job(c,ext,id);
                String capability=args.getString("capability","");
                ApplicationInfo app=c.getPackageManager().getApplicationInfo(getCallingPackage(),PackageManager.GET_META_DATA);
                if(!Arrays.asList(app.metaData.getString("yanzi.workflowCapabilities","").split(",")).contains(capability))throw new SecurityException("Workflow scope denied");
                JSONArray devices=MobileMessageClient.requestWithoutQueue(base,"/v1/me/devices",token,"GET",null).getJSONArray("items");
                String target=resolveDesktopTarget(devices,args.getString("targetDeviceId",""));
                if(record.exists()){value=read(record);if(!value.getString("capability").equals(capability)||!value.getString("targetDeviceId").equals(target)||!value.getJSONObject("parameters").toString().equals(new JSONObject(args.getString("parameters","{}")).toString()))throw new IOException("JOB_ID_CONFLICT");schedule(c,record);}
                else {
                    Uri uri=Uri.parse(args.getString("uri",""));if(!uri.getScheme().equals("content"))throw new IOException("CONTENT_URI_REQUIRED");
                    File snapshot=new File(root(c),ext+"-"+id+".input");long total=0;
                    try(InputStream in=c.getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(snapshot)){byte[]b=new byte[65536];int n;while((n=in.read(b))>0){total+=n;if(total>MobileAttachmentClient.LIMIT)throw new IOException("FILE_LIMIT_30_MB");out.write(b,0,n);}}
                    if(total==0)throw new IOException("EMPTY_FILE");
                    value=new JSONObject().put("jobId",id).put("extensionId",ext).put("accountId",SecureLanConnection.currentAccount(c)).put("targetDeviceId",target)
                        .put("capability",capability).put("requestedTransport",args.getString("transport","auto")).put("parameters",new JSONObject(args.getString("parameters","{}"))).put("inputPath",snapshot.getAbsolutePath()).put("state","queued").put("createdAt",java.time.Instant.now().toString());
                    save(record,value);schedule(c,record);
                }
            } else if(method.equals("status")){File record=job(c,ext,args.getString("jobId",""));value=read(record);schedule(c,record);}
            else if(method.equals("jobs")){JSONArray items=new JSONArray();File[] files=root(c).listFiles();if(files!=null)for(File f:files)if(f.getName().startsWith(ext+"-")&&f.getName().endsWith(".json")){JSONObject item=read(f);item.remove("inputPath");item.remove("resultPath");items.put(item);schedule(c,f);}value=new JSONObject().put("items",items).put("accountId",SecureLanConnection.currentAccount(c));}
            else throw new IOException("INVALID_METHOD");
            // Do not expose host file-system paths.
            value.remove("inputPath");value.remove("resultPath");value.put("ok",true);
            Bundle out=new Bundle();out.putString("result",value.toString());return out;
        }catch(SecurityException error){throw error;}catch(Exception error){Bundle out=new Bundle();out.putString("result",new JSONObject().toString());try{out.putString("result",new JSONObject().put("ok",false).put("error",error.getMessage()==null?error.getClass().getSimpleName():error.getMessage()).toString());}catch(Exception ignored){}return out;}
    }
    private static String resolveDesktopTarget(JSONArray devices,String requested)throws Exception {
        if(requested!=null&&!requested.isEmpty()){
            for(int n=0;n<devices.length();n++){JSONObject peer=devices.optJSONObject(n);if(peer!=null&&requested.equals(peer.optString("deviceId"))&&"desktop".equals(peer.optString("platform")))return requested;}
            throw new IOException("TARGET_NOT_FOUND");
        }
        JSONObject unique=DeviceTargets.uniqueOnlineDesktop(devices);
        if(unique!=null)return unique.getString("deviceId");
        java.util.Set<String> active=new java.util.HashSet<>();
        for(int n=0;n<devices.length();n++){JSONObject peer=devices.optJSONObject(n);if(peer==null||!"desktop".equals(peer.optString("platform"))||!peer.optBoolean("online"))continue;JSONObject caps=peer.optJSONObject("capabilities");if(caps!=null&&caps.optBoolean("disabled"))continue;String id=peer.optString("deviceId");if(!id.isEmpty())active.add(id);}
        throw new IOException(active.isEmpty()?"TARGET_NOT_FOUND":"TARGET_REQUIRED");
    }
    // Task center reads existing jobs; it never schedules a new invocation.
    static java.util.List<JSONObject> taskSnapshots(Context c) throws Exception {
        java.util.List<JSONObject> tasks=new java.util.ArrayList<>();
        File[] files=root(c).listFiles((d,n)->n.endsWith(".json"));
        if(files!=null) for(File file:files) {
            JSONObject job=read(file); String state=job.optString("state");
            String status=state.equals("completed")?"completed":state.equals("failed")?"failed":state.equals("queued")?"queued":"executing";
            long created=java.time.Instant.parse(job.getString("createdAt")).toEpochMilli();
            tasks.add(new JSONObject().put("id",job.getString("jobId")).put("companionJobId",job.getString("jobId"))
                .put("extensionId",job.getString("extensionId")).put("kind","文件处理").put("title",job.getString("extensionId")+" · "+job.optString("capability"))
                .put("status",status).put("target",job.getString("targetDeviceId")).put("createdAt",created)
                .put("messageId",job.optString("messageId")).put("error",job.optString("error"))
                .put("result",job.has("result")?MobileTaskJournal.clip(job.getJSONObject("result").toString()):""));
        }
        return tasks;
    }
    static void queryExistingTask(Context c,String extensionId,String id) throws Exception {
        File file=job(c,extensionId,id);JSONObject value=read(file);
        // Query/resume receiving a result is safe; submitting a queued job is owned by its original application.
        if(value.has("messageId")||value.has("result")) run(c,file);
    }
    static void resumePending(Context c){try{File[] files=root(c).listFiles();if(files!=null)for(File f:files)if(f.getName().endsWith(".json"))schedule(c,f);}catch(Exception ignored){}}
    private static void schedule(Context c,File record){if(RUNNING.size()>32||!RUNNING.add(record.getPath()))return;WORK.execute(()->{try{run(c,record);}catch(Exception error){try{JSONObject j=read(record);j.put("error",error.getMessage()==null?error.getClass().getSimpleName():error.getMessage());save(record,j);}catch(Exception ignored){}}finally{RUNNING.remove(record.getPath());}});}
    private static void run(Context c,File record)throws Exception {
        JSONObject j=read(record);if(j.optString("state").equals("completed")||j.optString("state").equals("failed"))return;
        if(!record.getParentFile().equals(root(c)))return;
        android.content.SharedPreferences prefs=c.getSharedPreferences("yanzi-mobile",0);
        String base=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd"),token=prefs.getString("token",""),source=prefs.getString("deviceId","");
        JSONObject input=new JSONObject().put("jobId",j.getString("jobId")).put("capability",j.getString("capability")).put("parameters",j.getJSONObject("parameters"));
        JSONObject result=null;
        if(!j.has("messageId") && !j.has("result")) {
            String lan=LanDiscoveryManager.getLanBaseUrl(c);
            if(!j.optString("requestedTransport").equals("cloud") && lan!=null && j.getString("targetDeviceId").equals(LanDiscoveryManager.getLanDeviceId(c))) {
                try {
                    File file=new File(j.getString("inputPath"));
                    if(file.length()>=2L*1048576)MobileLanChunks.send(lan,j.getString("jobId"),source,"input.bin","companion",file,MobileAttachmentClient.hash(file));
                    else {HttpURLConnection upload=MobileNetworkRouting.openLanConnection(new URL(lan+"/v1/companion/files/"+j.getString("jobId")));
                    try{upload.setConnectTimeout(5000);upload.setReadTimeout(60000);upload.setRequestMethod("PUT");upload.setDoOutput(true);upload.setFixedLengthStreamingMode(file.length());upload.setRequestProperty("X-Content-Sha256",MobileAttachmentClient.hash(file));try(InputStream in=new FileInputStream(file);OutputStream out=upload.getOutputStream()){byte[]b=new byte[65536];int n;while((n=in.read(b))>0)out.write(b,0,n);}if(upload.getResponseCode()!=200)throw new IOException("LAN_UPLOAD_FAILED");}finally{upload.disconnect();}}
                    input.put("transferId",j.getString("jobId"));JSONObject message=envelope(j,input,source);
                    JSONObject reply=lanJson(lan+"/v1/me/mobile/messages",message);
                    if(!reply.optBoolean("success")) {j.put("state","failed").put("error",reply.optString("output"));save(record,j);return;}
                    JSONObject invocation=new JSONObject(reply.getString("output"));if(!invocation.optBoolean("success"))throw new IOException(invocation.optString("error"));
                    result=invocation.getJSONObject("data");j.put("transport","lan").put("lanBase",lan).put("result",result).put("state","receiving");save(record,j);
                }catch(Exception network){input.remove("transferId");}
            }
            if(result==null) {
                if(!j.has("attachment")){j.put("attachment",MobileAttachmentClient.upload(base,token,"input.jpg","image/jpeg",new File(j.getString("inputPath"))));save(record,j);}
                input.put("attachmentId",j.getJSONObject("attachment").getString("attachmentId"));
                JSONObject reply=MobileMessageClient.request(base,"/v1/me/mobile/messages",token,"POST",envelope(j,input,source));
                j.put("messageId",reply.getString("messageId")).put("state","processing").put("transport","cloud");save(record,j);
            }
        }
        if(j.has("messageId")) {
            JSONObject message=MobileMessageClient.requestWithoutQueue(base,"/v1/me/mobile/messages/"+j.getString("messageId"),token,"GET",null);
            String state=message.getString("status");
            if(Arrays.asList("failed","expired","cancelled","unknown").contains(state)){j.put("state","failed").put("error",message.optJSONObject("payload").toString());save(record,j);return;}
            if(!state.equals("completed"))return;
            JSONObject invocation=new JSONObject(message.getJSONObject("payload").getJSONObject("executionResult").getString("output"));
            if(!invocation.optBoolean("success")){j.put("state","failed").put("error",invocation.optString("error"));save(record,j);return;}
            result=invocation.getJSONObject("data");j.put("result",result).put("state","receiving");save(record,j);
        }
        if(result==null)result=j.optJSONObject("result");if(result==null)return;
        File received;
        if(result.has("attachment"))received=MobileAttachmentClient.download(c,base,token,result.getJSONObject("attachment").getString("attachmentId"));
        else {
            received=new File(record.getParentFile(),j.getString("extensionId")+"-"+j.getString("jobId")+".result");
            File partial=new File(received+".part");long offset=partial.exists()?partial.length():0;if(offset>=result.getLong("size")){partial.delete();offset=0;}
            HttpURLConnection download=MobileNetworkRouting.openLanConnection(new URL(j.getString("lanBase")+"/v1/companion/files/"+result.getString("transferId")+"?offset="+offset));
            try{download.setConnectTimeout(5000);download.setReadTimeout(60000);if(download.getResponseCode()!=200)throw new IOException("LAN_RESULT_UNAVAILABLE");long size=offset;try(InputStream in=download.getInputStream();OutputStream out=new FileOutputStream(partial,true)){byte[]b=new byte[65536];int n;while((n=in.read(b))>0){size+=n;if(size>MobileAttachmentClient.LIMIT)throw new IOException("RESULT_TOO_LARGE");out.write(b,0,n);}}if(size!=result.getLong("size")||!MobileAttachmentClient.hash(partial).equals(result.getString("sha256"))){partial.delete();throw new IOException("RESULT_CHECKSUM_MISMATCH");}if(!partial.renameTo(received))throw new IOException("RESULT_COMMIT_FAILED");}finally{download.disconnect();}
        }
        if(!record.getParentFile().equals(root(c)))return;
        j.put("resultPath",received.getAbsolutePath()).put("state","completed").put("completedAt",java.time.Instant.now().toString()).remove("error");
        j.put("resultUri","content://"+c.getPackageName()+".companion-transfer/result/"+j.getString("extensionId")+"/"+j.getString("jobId"));save(record,j);
    }
    private static JSONObject envelope(JSONObject j,JSONObject input,String source)throws Exception {return new JSONObject().put("sourceDeviceId",source).put("targetDeviceId",j.getString("targetDeviceId")).put("kind","capability.invoke").put("clientMessageId",j.getString("jobId")).put("expiresAt",java.time.Instant.parse(j.getString("createdAt")).plusSeconds(1800).toString()).put("payload",new JSONObject().put("name","files.workflow.run").put("payload",input));}
    private static JSONObject lanJson(String url,JSONObject body)throws Exception {HttpURLConnection connection=MobileNetworkRouting.openLanConnection(new URL(url));try{connection.setConnectTimeout(5000);connection.setReadTimeout(60000);connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setRequestProperty("Content-Type","application/json");try(OutputStream out=connection.getOutputStream()){out.write(body.toString().getBytes("UTF-8"));}if(connection.getResponseCode()!=200)throw new IOException("LAN_WORKFLOW_UNAVAILABLE");try(InputStream in=connection.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[]b=new byte[4096];int n;while((n=in.read(b))>0){out.write(b,0,n);if(out.size()>65536)throw new IOException("RESULT_TOO_LARGE");}return new JSONObject(out.toString("UTF-8"));}}finally{connection.disconnect();}}
    public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException {try{java.util.List<String>p=uri.getPathSegments();if(p.size()!=3||!p.get(0).equals("result")||!mode.equals("r"))throw new SecurityException("Read-only result");authorize(p.get(1));JSONObject j=read(job(getContext(),p.get(1),p.get(2)));if(!j.getString("state").equals("completed"))throw new IOException("RESULT_NOT_READY");return ParcelFileDescriptor.open(new File(j.getString("resultPath")),ParcelFileDescriptor.MODE_READ_ONLY);}catch(Exception e){throw new FileNotFoundException(e.getMessage());}}
    public Cursor query(Uri u,String[]p,String s,String[]a,String o){throw new UnsupportedOperationException();}
    public String getType(Uri u){return "image/jpeg";}public Uri insert(Uri u,ContentValues v){throw new UnsupportedOperationException();}public int delete(Uri u,String s,String[]a){throw new UnsupportedOperationException();}public int update(Uri u,ContentValues v,String s,String[]a){throw new UnsupportedOperationException();}
}

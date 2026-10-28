package cc.luoluoluo.yanzi.mobile;

import android.Manifest;
import android.app.job.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.location.*;
import android.net.*;
import android.os.*;
import org.json.*;
import java.util.concurrent.*;

/** Opt-in, account/extension-scoped native environment collection. No JS timers or location history. */
final class MobileEnvironment {
    static final int JOB_ID=51901;
    static final ExecutorService WORK=Executors.newSingleThreadExecutor();
    private static final java.util.concurrent.atomic.AtomicBoolean collecting=new java.util.concurrent.atomic.AtomicBoolean();
    private static final java.util.concurrent.atomic.AtomicBoolean retrying=new java.util.concurrent.atomic.AtomicBoolean();
    static SharedPreferences state(Context c) { return c.getSharedPreferences("mobile-environment",0); }
    static String scope(Context c,String id) throws Exception {
        if(!id.matches("[a-zA-Z0-9_.-]{1,128}")) throw new IllegalArgumentException("无效小程序 ID");
        SharedPreferences login=c.getSharedPreferences("yanzi-mobile",0);
        if(login.getString("token","").isEmpty()) throw new IllegalStateException("请先登录燕子账号");
        return android.util.Base64.encodeToString(java.security.MessageDigest.getInstance("SHA-256").digest(
            (login.getString("baseUrl","")+"\n"+SecureLanConnection.currentAccount(c)+"\n"+login.getString("deviceId","")+"\n"+id).getBytes(java.nio.charset.StandardCharsets.UTF_8)),android.util.Base64.NO_WRAP);
    }
    static JSONObject config(Context c,String id) throws Exception { return new JSONObject(state(c).getString(scope(c,id)+".config","{}")); }
    static void saveConfig(Context c,String id,JSONObject value) throws Exception {
        String key=scope(c,id);
        JSONObject previous=config(c,id);
        if(!String.valueOf(previous.opt("homeLatitude")).equals(String.valueOf(value.opt("homeLatitude")))
                || !String.valueOf(previous.opt("homeLongitude")).equals(String.valueOf(value.opt("homeLongitude")))
                || previous.optInt("radius",200)!=value.optInt("radius",200)) {
            state(c).edit().remove(key+".candidate").remove(key+".candidateTime").remove(key+".count").commit();
        }
        if(!state(c).edit().putString(key+".config",value.toString()).commit()) throw new java.io.IOException("设置保存失败");
        SharedPreferences.Editor edit=state(c).edit();
        java.util.Set<String> ids=new java.util.HashSet<>(state(c).getStringSet("extensionIds",new java.util.HashSet<>()));
        ids.add(id); edit.putStringSet("extensionIds",ids).commit(); schedule(c);
    }
    static boolean permission(Context c,boolean background) {
        boolean location=c.checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION)==PackageManager.PERMISSION_GRANTED
            || c.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)==PackageManager.PERMISSION_GRANTED;
        return location && (!background || Build.VERSION.SDK_INT<29 || c.checkSelfPermission(Manifest.permission.ACCESS_BACKGROUND_LOCATION)==PackageManager.PERMISSION_GRANTED);
    }
    static void schedule(Context c) {
        JobScheduler scheduler=(JobScheduler)c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        boolean needed=false;
        for(String id:state(c).getStringSet("extensionIds",new java.util.HashSet<>())) try {
            if(config(c,id).optBoolean("enabled") || state(c).contains(scope(c,id)+".pending")) needed=true;
        }catch(Exception ignored) { }
        if(scheduler!=null && !needed){scheduler.cancel(JOB_ID);return;}
        if(scheduler!=null && scheduler.getPendingJob(JOB_ID)!=null)return;
        if(scheduler!=null) scheduler.schedule(new JobInfo.Builder(JOB_ID,new ComponentName(c,EnvironmentJobService.class))
            .setPeriodic(15*60*1000L,5*60*1000L).setPersisted(true).build());
    }
    static boolean permitted(Context c,String id) {
        try {
            JSONArray definitions=new JSONArray(c.getSharedPreferences("yanzi-mobile",0).getString("mobileExtensions","[]"));
            for(int i=0;i<definitions.length();i++) if(permittedDefinition(definitions.optJSONObject(i),id)) return true;
            String[] files=c.getAssets().list("mobile-extensions");
            if(files!=null) for(String file:files) try(java.io.InputStream stream=c.getAssets().open("mobile-extensions/"+file)) {
                java.io.ByteArrayOutputStream bytes=new java.io.ByteArrayOutputStream();byte[] buffer=new byte[4096];int count;
                while((count=stream.read(buffer))!=-1)bytes.write(buffer,0,count);
                if(permittedDefinition(new JSONObject(bytes.toString("UTF-8")),id))return true;
            }
        }catch(Exception ignored) { }
        return false;
    }
    private static boolean permittedDefinition(JSONObject definition,String id) {
        if(definition==null || !id.equals(definition.optString("id")))return false;
        JSONArray permissions=definition.optJSONArray("permissions");
        if(permissions!=null)for(int i=0;i<permissions.length();i++)if("device.environment".equals(permissions.optString(i)))return true;
        return false;
    }
    static JSONObject network(Context c) throws Exception {
        ConnectivityManager m=(ConnectivityManager)c.getSystemService(Context.CONNECTIVITY_SERVICE);
        JSONObject value=new JSONObject().put("type","none").put("wifiConnected",false).put("internetValidated",false);
        if(m==null) return value;
        Network active=m.getActiveNetwork(); NetworkCapabilities current=active==null?null:m.getNetworkCapabilities(active);
        if(current!=null) value.put("internetValidated",current.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED))
            .put("type",current.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)?"wifi":current.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)?"cellular":"other");
        for(Network n:m.getAllNetworks()) { NetworkCapabilities caps=m.getNetworkCapabilities(n);
            if(caps!=null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) value.put("wifiConnected",true); }
        return value;
    }
    static Location locate(Context c,boolean background) throws Exception {
        if(!permission(c,background)) throw new IllegalStateException(background?"后台定位未授权":"定位权限未授权");
        LocationManager manager=(LocationManager)c.getSystemService(Context.LOCATION_SERVICE);
        if(manager==null || !manager.isLocationEnabled()) throw new IllegalStateException("系统定位已关闭");
        Location best=null;
        for(String provider:manager.getProviders(true)) {
            try { Location l=manager.getLastKnownLocation(provider);
                if(l!=null && l.hasAccuracy() && EnvironmentPolicy.fresh(l.getTime(),System.currentTimeMillis()) && (best==null || l.getAccuracy()<best.getAccuracy())) best=l;
            } catch(SecurityException ignored) { }
        }
        if(best!=null) return best;
        CountDownLatch done=new CountDownLatch(1); final Location[] result=new Location[1];
        LocationListener listener=new LocationListener() {
            public void onLocationChanged(Location l) { if(l.hasAccuracy() && EnvironmentPolicy.fresh(l.getTime(),System.currentTimeMillis())) { result[0]=l; done.countDown(); } }
            public void onStatusChanged(String p,int s,Bundle b) { }
            public void onProviderEnabled(String p) { }
            public void onProviderDisabled(String p) { }
        };
        try {
            boolean requested=false;
            for(String provider:new String[]{LocationManager.NETWORK_PROVIDER,LocationManager.GPS_PROVIDER})
                try { if(manager.isProviderEnabled(provider)) { manager.requestSingleUpdate(provider,listener,Looper.getMainLooper()); requested=true; } } catch(SecurityException ignored) { }
            if(!requested || !done.await(20,TimeUnit.SECONDS)) throw new IllegalStateException("定位超时，未使用过期位置");
            return result[0];
        } finally { manager.removeUpdates(listener); }
    }
    static synchronized JSONObject collect(Context c,String id,boolean background,boolean setHome) throws Exception {
        String key=scope(c,id); JSONObject cfg=config(c,id);
        String token=c.getSharedPreferences("yanzi-mobile",0).getString("token","");
        if(background && !cfg.optBoolean("enabled")) return status(c,id);
        Location location=null; String reason="ok";
        try { location=locate(c,background); } catch(Exception e) { if(e instanceof InterruptedException) throw e; reason=e.getMessage(); }
        if(!key.equals(scope(c,id)) || !token.equals(c.getSharedPreferences("yanzi-mobile",0).getString("token",""))) throw new IllegalStateException("账号已变化，已丢弃采样");
        cfg=config(c,id);
        if(background && !cfg.optBoolean("enabled")) return status(c,id);
        if(setHome) {
            if(location==null || location.getAccuracy()>200) throw new IllegalStateException("请在定位精度优于 200 米时设置家的位置");
            cfg.put("homeLatitude",location.getLatitude()).put("homeLongitude",location.getLongitude()); saveConfig(c,id,cfg);
        }
        String place="unknown";
        if(location!=null && cfg.has("homeLatitude")) {
            float[] distance=new float[1]; Location.distanceBetween(location.getLatitude(),location.getLongitude(),cfg.getDouble("homeLatitude"),cfg.getDouble("homeLongitude"),distance);
            String candidate=EnvironmentPolicy.classify(distance[0],location.getAccuracy(),cfg.optDouble("radius",200));
            String previous=state(c).getString(key+".candidate","");
            int count=candidate.equals(previous)?state(c).getInt(key+".count",0)+(location.getTime()>state(c).getLong(key+".candidateTime",0)?1:0):1;
            state(c).edit().putString(key+".candidate",candidate).putInt(key+".count",count).putLong(key+".candidateTime",location.getTime()).commit();
            if(count>=2 && !candidate.equals("unknown")) place=candidate;
        }
        JSONObject value=new JSONObject().put("schemaVersion",1).put("enabled",cfg.optBoolean("enabled"))
            .put("observedAt",java.time.Instant.ofEpochMilli(location==null?System.currentTimeMillis():location.getTime()).toString())
            .put("place",place).put("confidence",place.equals("unknown")?"unknown":"high")
            .put("availability",location==null?"location_unavailable":"available").put("network",network(c))
            .put("shareCoordinates",cfg.optBoolean("shareCoordinates"));
        if(location!=null && cfg.optBoolean("shareCoordinates")) value.put("location",new JSONObject()
            .put("latitude",location.getLatitude()).put("longitude",location.getLongitude()).put("accuracy",location.getAccuracy()));
        if(!state(c).edit().putString(key+".last",value.toString()).putString(key+".error",reason.equals("ok")?"":reason).commit()) throw new java.io.IOException("采样保存失败");
        if(cfg.optBoolean("enabled")) { queue(c,id,value); flush(c,id,key,token); }
        return status(c,id);
    }
    static synchronized void queue(Context c,String id,JSONObject value) throws Exception {
        String key=scope(c,id); long sequence=Math.max(state(c).getLong(key+".sequence",0)+1,System.currentTimeMillis());
        value.put("sequence",sequence);
        if(!state(c).edit().putLong(key+".sequence",sequence).putString(key+".pending",value.toString()).commit()) throw new java.io.IOException("待发状态保存失败");
        schedule(c);
    }
    static synchronized void flush(Context c,String id,String key,String token) throws Exception {
        if(!key.equals(scope(c,id))) return;
        SharedPreferences login=c.getSharedPreferences("yanzi-mobile",0);
        if(!token.equals(login.getString("token",""))) return;
        String pending=state(c).getString(key+".pending",""); if(pending.isEmpty()) return;
        JSONObject value=new JSONObject(pending);
        if(value.optBoolean("enabled") && System.currentTimeMillis()-java.time.Instant.parse(value.getString("observedAt")).toEpochMilli()>3600000) {
            state(c).edit().remove(key+".pending").commit(); return;
        }
        try {
            MobileMessageClient.requestWithoutQueue(login.getString("baseUrl","https://sync.luoluoluo.cc.cd"),
                "/v1/me/devices/"+login.getString("deviceId","")+"/environment/"+id,token,"PUT",value);
            if(key.equals(scope(c,id)) && pending.equals(state(c).getString(key+".pending",""))) state(c).edit()
                .remove(key+".pending").putString(key+".reportedAt",java.time.Instant.now().toString()).commit();
        } catch(Exception e) { state(c).edit().putString(key+".error","上报待重试："+e.getClass().getSimpleName()).commit(); }
    }
    static synchronized void disable(Context c,String id) throws Exception {
        JSONObject cfg=config(c,id); cfg.put("enabled",false); saveConfig(c,id,cfg);
        String key=scope(c,id); state(c).edit().remove(key+".last").remove(key+".candidate").remove(key+".candidateTime").remove(key+".count").commit();
        queue(c,id,new JSONObject().put("enabled",false));
        flush(c,id,key,c.getSharedPreferences("yanzi-mobile",0).getString("token",""));
    }
    static JSONObject status(Context c,String id) throws Exception {
        String key=scope(c,id);
        return new JSONObject().put("config",config(c,id)).put("sample",new JSONObject(state(c).getString(key+".last","{}")))
            .put("reportedAt",state(c).getString(key+".reportedAt","")).put("pending",state(c).contains(key+".pending"))
            .put("error",state(c).getString(key+".error","")).put("foregroundPermission",permission(c,false)).put("backgroundPermission",permission(c,true));
    }
    // Connection recovery and definition removal flush metadata only; never trigger GPS here.
    static void retryPending(Context source) {
        Context c=source.getApplicationContext();
        if(!retrying.compareAndSet(false,true))return;
        WORK.execute(()->{
            try {
                for(String id:new java.util.HashSet<>(state(c).getStringSet("extensionIds",new java.util.HashSet<>())))try {
                    if(config(c,id).optBoolean("enabled") && !permitted(c,id))disable(c,id);
                    else flush(c,id,scope(c,id),c.getSharedPreferences("yanzi-mobile",0).getString("token",""));
                }catch(Exception ignored) { }
            }finally {retrying.set(false);schedule(c);}
        });
    }
    static void runScheduled(Context c) {
        if(!collecting.compareAndSet(false,true)) return;
        try {
            for(String id:new java.util.HashSet<>(state(c).getStringSet("extensionIds",new java.util.HashSet<>()))) {
                if(Thread.currentThread().isInterrupted()) return;
                try { String key=scope(c,id);
                    if(config(c,id).optBoolean("enabled") && !permitted(c,id)) disable(c,id);
                    else if(config(c,id).optBoolean("enabled")) collect(c,id,true,false);
                    else flush(c,id,key,c.getSharedPreferences("yanzi-mobile",0).getString("token",""));
                } catch(Exception ignored) { }
            }
        } finally { collecting.set(false); schedule(c); }
    }
}

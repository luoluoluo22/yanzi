package cc.luoluoluo.yanzi.mobile;

import android.app.*;
import android.content.*;
import android.os.*;
import org.json.*;
import java.lang.ref.WeakReference;
import java.util.*;
import java.util.concurrent.*;

/** Consent polling is independent of the messaging network queue. */
public final class ExternalAccessManager {
    private static final ExecutorService worker=Executors.newSingleThreadExecutor();
    private static WeakReference<Activity> foreground=new WeakReference<>(null);
    private static final Set<String> shown=new HashSet<>();
    private static long nextPoll; private static boolean busy;
    private static String session="";
    public static synchronized void foreground(Activity activity){foreground=new WeakReference<>(activity);nextPoll=0;pollAsync(activity);}
    public static synchronized void invalidate(Context context){nextPoll=0;pollAsync(context);}
    public static synchronized void background(Activity activity){if(foreground.get()==activity)foreground.clear();}
    public static synchronized void pollAsync(Context source){
        if(busy||SystemClock.elapsedRealtime()<nextPoll)return;
        Context context=source.getApplicationContext();
        SharedPreferences prefs=context.getSharedPreferences("yanzi-mobile",Context.MODE_PRIVATE);
        String token=prefs.getString("token","");if(token.isEmpty())return;
        busy=true;nextPoll=SystemClock.elapsedRealtime()+(foreground.get()!=null?15000:60000);
        worker.execute(()->{try{
            String base=prefs.getString("baseUrl","https://sync.luoluoluo.cc.cd");
            JSONObject result=MobileMessageClient.request(base,"/v1/applications/access-requests",token,"GET",null);
            if(!token.equals(prefs.getString("token","")))return;
            synchronized(ExternalAccessManager.class){if(!session.equals(token)){shown.clear();session=token;}}
            JSONArray requests=result.optJSONArray("requests");if(requests==null)return;
            Set<String> pending=new HashSet<>();
            for(int i=0;i<requests.length();i++){
                JSONObject row=requests.getJSONObject(i);String id=row.getString("requestId");pending.add(id);
                synchronized(ExternalAccessManager.class){if(!shown.add(id))continue;}
                new Handler(Looper.getMainLooper()).post(()->{
                    if(!token.equals(prefs.getString("token","")))return;
                    Intent open=new Intent(context,ExternalAccessActivity.class).putExtra("requestId",id);
                    Activity active; synchronized(ExternalAccessManager.class){active=foreground.get();}
                    if(active!=null&&!active.isFinishing()){active.startActivity(open);return;}
                    NotificationManager manager=(NotificationManager)context.getSystemService(Context.NOTIFICATION_SERVICE);
                    manager.createNotificationChannel(new NotificationChannel("yanzi_external_access","外部应用授权",NotificationManager.IMPORTANCE_HIGH));
                    PendingIntent action=PendingIntent.getActivity(context,id.hashCode(),open,PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
                    manager.notify(id.hashCode(),new Notification.Builder(context,"yanzi_external_access").setSmallIcon(android.R.drawable.ic_dialog_info)
                        .setContentTitle("外部应用请求数据授权").setContentText(row.optString("clientName")+" · 核对码 "+row.optString("userCode"))
                        .setContentIntent(action).setAutoCancel(true).build());
                });
            }
            synchronized(ExternalAccessManager.class){for(String id:new HashSet<>(shown))if(!pending.contains(id)){shown.remove(id);((NotificationManager)context.getSystemService(Context.NOTIFICATION_SERVICE)).cancel(id.hashCode());}}
        }catch(Exception ignored){/* Keep connection diagnostics free of invitation secrets. */}
        finally{synchronized(ExternalAccessManager.class){busy=false;}}});
    }
}

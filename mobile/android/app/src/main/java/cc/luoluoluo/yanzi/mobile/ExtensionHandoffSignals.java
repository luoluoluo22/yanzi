package cc.luoluoluo.yanzi.mobile;
import android.content.*;
import android.content.pm.*;
import org.json.*;
import java.io.IOException;

/** Routes an authenticated extension handoff from the account message channel to a signed companion app. */
final class ExtensionHandoffSignals {
    static final String KIND="extension.handoff";

    static JSONObject deliver(Context context,String account,JSONObject payload) throws Exception {
        if(payload==null)throw new IOException("MISSING_HANDOFF_PAYLOAD");
        String extension=payload.optString("extensionId","");
        String input=payload.optString("input","");
        String expectedAccount=payload.optString("accountId","");
        if(!extension.matches("[a-z0-9-]{1,80}")||input.isEmpty()||input.length()>4096)throw new IOException("INVALID_HANDOFF");
        if(account.isEmpty()||!account.equals(expectedAccount))throw new IOException("ACCOUNT_CHANGED");

        PackageManager pm=context.getPackageManager();
        for(java.util.Map.Entry<String,?> subscriber:context.getSharedPreferences("extension-subscribers",0).getAll().entrySet()) {
            if(!extension.equals(String.valueOf(subscriber.getValue())))continue;
            String[] key=subscriber.getKey().split("/",2);
            if(key.length<1||key[0].isEmpty())continue;
            String pkg=key[0];
            try {
                if(pm.checkSignatures(pkg,context.getPackageName())!=PackageManager.SIGNATURE_MATCH)continue;
                if(pm.checkPermission(context.getPackageName()+".permission.EXTENSION_STORAGE",pkg)!=PackageManager.PERMISSION_GRANTED)continue;
                ApplicationInfo app=pm.getApplicationInfo(pkg,PackageManager.GET_META_DATA);
                if(app.metaData==null||!app.metaData.getBoolean("yanzi.handoff",false))continue;
                if(!java.util.Arrays.asList(app.metaData.getString("yanzi.extensionScopes","").split(",")).contains(extension))continue;

                Intent launch=pm.getLaunchIntentForPackage(pkg);
                if(launch==null)throw new IOException("COMPANION_LAUNCH_UNAVAILABLE");
                launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP)
                    .putExtra("yanzi.handoff.input",input);
                if(MainActivity.sForeground&&MainActivity.sInstance!=null&&!MainActivity.sInstance.isFinishing()) {
                    MainActivity.sInstance.runOnUiThread(()->MainActivity.sInstance.startActivity(launch));
                    return new JSONObject().put("opened",true).put("extensionId",extension);
                }

                Intent event=new Intent(context.getPackageName()+".EXTENSION_HANDOFF").setPackage(pkg)
                    .putExtra("extensionId",extension).putExtra("accountId",account).putExtra("input",input);
                context.sendBroadcast(event,context.getPackageName()+".permission.EXTENSION_STORAGE");
                MobileNotificationManager.ensureChannels(context);
                if(MobileEventNotifier.canNotify(context)) {
                    android.app.PendingIntent open=android.app.PendingIntent.getActivity(context,
                        input.hashCode(),launch,android.app.PendingIntent.FLAG_IMMUTABLE|android.app.PendingIntent.FLAG_UPDATE_CURRENT);
                    android.app.Notification notice=new android.app.Notification.Builder(context,MobileNotificationManager.CHANNEL_SYNC)
                        .setSmallIcon(android.R.drawable.ic_dialog_info).setContentTitle("在手机上打开笔记")
                        .setContentText("点击继续查看电脑上的当前笔记").setContentIntent(open).setAutoCancel(true).build();
                    ((android.app.NotificationManager)context.getSystemService(Context.NOTIFICATION_SERVICE))
                        .notify("extension-handoff",41003,notice);
                }
                return new JSONObject().put("opened",false).put("queued",true).put("extensionId",extension);
            } catch(PackageManager.NameNotFoundException ignored) {}
        }
        throw new IOException("COMPANION_NOT_FOUND");
    }
}

package cc.luoluoluo.yanzi.mobile;
import android.content.*;
import android.content.pm.*;
import org.json.*;

/** Metadata-only invalidations, delivered through the existing authenticated message channel. */
final class ExtensionStorageSignals {
    static final String KIND="extension-storage.changed";
    static void subscribe(Context c,String caller,String extension) {
        c.getSharedPreferences("extension-subscribers",0).edit().putString(caller+"/"+extension,extension).apply();
    }
    static void publish(Context c,String base,String token,String device,String ext,String key,long revision) throws Exception {
        MobileApplicationContext.initialize(c);
        JSONObject message=new JSONObject().put("kind",KIND).put("sourceDeviceId",device)
            .put("targetPlatform","desktop").put("clientMessageId",java.util.UUID.randomUUID().toString())
            .put("payload",new JSONObject().put("extensionId",ext).put("key",key).put("revision",revision));
        java.io.File queued=MobileMessageOutbox.save(base,token,message);
        MobileMessageClient.requestWithoutQueue(base,"/v1/me/mobile/messages",token,"POST",message);
        MobileMessageOutbox.complete(queued);
    }
    static void deliver(Context c,String account,String ext,String key,long revision) {
        if(!ext.matches("[a-z0-9-]+")||key.isEmpty()||revision<0)return;
        for(java.util.Map.Entry<String,?> subscriber:c.getSharedPreferences("extension-subscribers",0).getAll().entrySet()) {
            if(!ext.equals(subscriber.getValue()))continue;
            try {
                String pkg=subscriber.getKey().split("/",2)[0];
                if(c.getPackageManager().checkSignatures(pkg,c.getPackageName())!=PackageManager.SIGNATURE_MATCH)continue;
                ApplicationInfo app=c.getPackageManager().getApplicationInfo(pkg,PackageManager.GET_META_DATA);
                if(app.metaData==null||!java.util.Arrays.asList(app.metaData.getString("yanzi.extensionScopes","").split(",")).contains(ext))continue;
                Intent event=new Intent(c.getPackageName()+".EXTENSION_STORAGE_CHANGED").setPackage(pkg)
                    .putExtra("accountId",account).putExtra("extensionId",ext).putExtra("key",key).putExtra("revision",revision);
                c.sendBroadcast(event,c.getPackageName()+".permission.EXTENSION_STORAGE");
            } catch(PackageManager.NameNotFoundException ignored) {}
        }
    }
    static void reconnected(Context c,String token) {
        try {
            String account=ExtensionStorageProvider.accountId(token);
            java.util.HashSet<String> scopes=new java.util.HashSet<>();
            for(Object ext:c.getSharedPreferences("extension-subscribers",0).getAll().values())scopes.add(String.valueOf(ext));
            // Empty key is a reconnect hint, not a data change.
            for(String ext:scopes)deliver(c,account,ext,"*",0);
        } catch(Exception ignored) {}
    }
}

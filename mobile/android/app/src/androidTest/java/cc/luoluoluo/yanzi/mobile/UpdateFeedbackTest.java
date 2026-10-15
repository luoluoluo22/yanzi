package cc.luoluoluo.yanzi.mobile;

import android.app.Instrumentation;
import android.os.Bundle;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

/** No preference writes: safe on the explicitly selected Dev installation. */
public final class UpdateFeedbackTest {
    static void run(Instrumentation instrumentation, boolean probeNetwork) {
        android.content.Context context=instrumentation.getTargetContext();
        Bundle result=new Bundle();
        try {
            if(!context.getPackageName().endsWith(".dev"))throw new AssertionError("Dev only");
            instrumentation.runOnMainSync(()->{
                TextView label=new TextView(context);label.setText("获取应用");
                BusyButton text=new BusyButton(label);
                require(text.begin("正在获取…"),"First click accepted");
                require(!text.begin("重复点击"),"Duplicate rejected");
                require(!label.isEnabled() && label.getAlpha()<1f,"Disabled and dimmed");
                require("正在获取…".contentEquals(label.getText()),"Immediate loading label");
                text.finish();require(label.isEnabled()&&label.getAlpha()==1f&&"获取应用".contentEquals(label.getText()),"Restored after completion/failure");
                require(text.begin("再次获取…"),"Retry allowed");text.finish();
                LinearLayout row=new LinearLayout(context);BusyButton button=new BusyButton(row);
                button.begin("正在检查更新…");require(!row.isEnabled()&&row.getChildAt(0).getVisibility()==View.VISIBLE,"Row spinner visible");
                button.finish();require(row.isEnabled()&&row.getChildAt(0).getVisibility()==View.GONE,"Row spinner removed");
            });
            android.app.Activity activity=instrumentation.startActivitySync(new android.content.Intent(context,MainActivity.class)
                .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
            final TextView[] checkingLabel=new TextView[1];
            instrumentation.runOnMainSync(()->{
                checkingLabel[0]=new TextView(activity);checkingLabel[0].setText("检查更新");
                BusyButton busy=new BusyButton(checkingLabel[0]);
                UpdateManager.checkUpdate(activity,true,busy);
                require(!checkingLabel[0].isEnabled(),"Real updater immediately disables action");
                UpdateManager.checkUpdate(activity,true,busy);
                require(!checkingLabel[0].isEnabled(),"Repeated call retains busy state");
            });
            long timeout=System.nanoTime()+java.util.concurrent.TimeUnit.SECONDS.toNanos(50);
            java.util.concurrent.atomic.AtomicBoolean restored=new java.util.concurrent.atomic.AtomicBoolean();
            while(System.nanoTime()<timeout){
                instrumentation.runOnMainSync(()->restored.set(checkingLabel[0].isEnabled()&&"检查更新".contentEquals(checkingLabel[0].getText())));
                if(restored.get())break;Thread.sleep(100);
            }
            require(restored.get(),"Real updater restores button on no Dev release/error");
            instrumentation.runOnMainSync(activity::finish);
            StringBuilder measurements=new StringBuilder();
            if(probeNetwork){
                MobileNetworkRouting.initialize(context);
                java.util.List<UpdateDownloadNodes.Node> nodes=UpdateDownloadNodes.select("https://sync.luoluoluo.cc.cd/downloads/android/yanzi-mobile-0.2.45.apk",
                    (url,system)->system?(java.net.HttpURLConnection)url.openConnection():MobileNetworkRouting.openCloudConnection(url),()->false,message->measurements.append(message).append('\n'));
                require(!nodes.isEmpty()&&nodes.get(0).elapsedNanos>0,"Network candidates really passed APK probes");measurements.append("Selected: ").append(nodes.get(0).name);
            }
            result.putString("stream","UPDATE_FEEDBACK=PASSED (11 assertions)\n"+measurements);
            instrumentation.finish(android.app.Activity.RESULT_OK,result);
        }catch(Throwable error){result.putString("stream",android.util.Log.getStackTraceString(error));instrumentation.finish(android.app.Activity.RESULT_CANCELED,result);}
    }
    private static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
}

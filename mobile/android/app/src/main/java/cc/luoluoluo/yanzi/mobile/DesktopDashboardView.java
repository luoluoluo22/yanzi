package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.graphics.Color;
import android.view.Gravity;
import android.widget.*;

final class DesktopDashboardView {
    static final class Result {
        final TextView status, channel, sync;
        Result(TextView status, TextView channel, TextView sync){this.status=status;this.channel=channel;this.sync=sync;}
        void update(boolean connected,String type){
            status.setText(connected?"在线运行":"当前离线");
            status.setTextColor(connected?YanziUiKit.GREEN:YanziUiKit.RED);
            channel.setText(connected?("lan".equals(type)?"局域网":"云端"):"—");
            sync.setText(connected?"已同步":"待连接");
        }
    }
    private DesktopDashboardView(){}

    static Result populate(Activity a, LinearLayout parent, Runnable chat, Runnable apps, Runnable files, Runnable shell) {
        parent.addView(YanziUiKit.header(a,"电脑","远程连接与控制你的桌面端燕子","monitor",YanziUiKit.BLUE),YanziUiKit.cardLp(a));
        LinearLayout hero=YanziUiKit.tintedCard(a,Color.rgb(13,31,49),Color.rgb(35,66,100));
        LinearLayout title=new LinearLayout(a);title.setOrientation(LinearLayout.HORIZONTAL);title.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout copy=new LinearLayout(a);copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(YanziUiKit.text(a,"我的电脑",19,YanziUiKit.TEXT,true));
        TextView status=YanziUiKit.text(a,"检测中…",12,YanziUiKit.SECONDARY,true);copy.addView(status);
        title.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView badge=YanziUiKit.text(a,"●",18,YanziUiKit.GREEN,true);title.addView(badge);
        hero.addView(title);
        LinearLayout metrics=new LinearLayout(a);metrics.setOrientation(LinearLayout.HORIZONTAL);metrics.setPadding(0,YanziUiKit.dp(a,12),0,0);
        TextView ch=YanziUiKit.text(a,"检测中",15,YanziUiKit.BLUE,true);ch.setGravity(Gravity.CENTER);
        TextView sy=YanziUiKit.text(a,"检测中",15,YanziUiKit.GREEN,true);sy.setGravity(Gravity.CENTER);
        LinearLayout m1=YanziUiKit.metric(a,"实时","连接状态",YanziUiKit.BLUE);
        LinearLayout m2=YanziUiKit.metric(a,"—","通道",YanziUiKit.PURPLE);
        LinearLayout m3=YanziUiKit.metric(a,"—","同步",YanziUiKit.GREEN);
        ((TextView)m2.getChildAt(0)).setTag("channel");
        ((TextView)m3.getChildAt(0)).setTag("sync");
        metrics.addView(m1,new LinearLayout.LayoutParams(0,-2,1f));
        LinearLayout.LayoutParams g1=new LinearLayout.LayoutParams(0,-2,1f);g1.leftMargin=YanziUiKit.dp(a,7);metrics.addView(m2,g1);
        LinearLayout.LayoutParams g2=new LinearLayout.LayoutParams(0,-2,1f);g2.leftMargin=YanziUiKit.dp(a,7);metrics.addView(m3,g2);
        ch=(TextView)m2.getChildAt(0);sy=(TextView)m3.getChildAt(0);
        hero.addView(metrics);
        parent.addView(hero,YanziUiKit.cardLp(a));

        parent.addView(YanziUiKit.sectionLabel(a,"远程操作"));
        LinearLayout tools=new LinearLayout(a);tools.setOrientation(LinearLayout.HORIZONTAL);
        tools.addView(YanziUiKit.actionTile(a,"chat",YanziUiKit.BLUE,"聊天","文字与附件",chat),new LinearLayout.LayoutParams(0,-2,1f));
        LinearLayout.LayoutParams p1=new LinearLayout.LayoutParams(0,-2,1f);p1.leftMargin=YanziUiKit.dp(a,7);tools.addView(YanziUiKit.actionTile(a,"apps",YanziUiKit.PURPLE,"小程序","远程运行",apps),p1);
        LinearLayout.LayoutParams p2=new LinearLayout.LayoutParams(0,-2,1f);p2.leftMargin=YanziUiKit.dp(a,7);tools.addView(YanziUiKit.actionTile(a,"folder-outline",YanziUiKit.ORANGE,"文件","浏览与传输",files),p2);
        LinearLayout.LayoutParams p3=new LinearLayout.LayoutParams(0,-2,1f);p3.leftMargin=YanziUiKit.dp(a,7);tools.addView(YanziUiKit.actionTile(a,"console",YanziUiKit.GREEN,"终端","PowerShell",shell),p3);
        parent.addView(tools,YanziUiKit.cardLp(a));
        parent.addView(YanziUiKit.sectionLabel(a,"电脑工作区"));
        return new Result(status,ch,sy);
    }
}

package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.graphics.Color;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.*;

final class DeveloperDashboardView {
    static final class Result {
        final TextView count;
        final TextView sync;
        Result(TextView count, TextView sync){this.count=count;this.sync=sync;}
        void update(int value, String syncText){count.setText(String.valueOf(value));sync.setText(syncText);}
    }
    private DeveloperDashboardView(){}

    static Result populate(Activity a, LinearLayout parent, int extensionCount,
                           Runnable create, Runnable sync, Runnable appCenter,
                           Runnable docs, Runnable terminal) {
        LinearLayout head=YanziUiKit.header(a,"开发","构建、调试与管理手机小程序","code",YanziUiKit.BLUE);
        parent.addView(head,YanziUiKit.cardLp(a));

        LinearLayout hero=YanziUiKit.tintedCard(a,Color.rgb(13,35,67),Color.rgb(38,76,124));
        LinearLayout top=new LinearLayout(a);top.setOrientation(LinearLayout.HORIZONTAL);top.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout copy=new LinearLayout(a);copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(YanziUiKit.text(a,"快速创建小程序",20,YanziUiKit.TEXT,true));
        copy.addView(YanziUiKit.text(a,"从模板开始，保存后立即同步并运行",12,YanziUiKit.SECONDARY,false));
        top.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView createBtn=YanziUiKit.primaryButton(a,"新建小程序",create);
        top.addView(createBtn,new LinearLayout.LayoutParams(YanziUiKit.dp(a,112),YanziUiKit.dp(a,40)));
        hero.addView(top);

        LinearLayout metrics=new LinearLayout(a);metrics.setOrientation(LinearLayout.HORIZONTAL);metrics.setPadding(0,YanziUiKit.dp(a,12),0,0);
        LinearLayout m1=YanziUiKit.metric(a,String.valueOf(extensionCount),"本机小程序",YanziUiKit.BLUE);
        LinearLayout m2=YanziUiKit.metric(a,"热重载","开发模式",YanziUiKit.PURPLE);
        LinearLayout m3=YanziUiKit.metric(a,"云端","同步通道",YanziUiKit.GREEN);
        TextView count=(TextView)m1.getChildAt(0);
        TextView syncTv=(TextView)m3.getChildAt(0);
        metrics.addView(m1,new LinearLayout.LayoutParams(0,-2,1f));
        LinearLayout.LayoutParams gap=new LinearLayout.LayoutParams(0,-2,1f);gap.leftMargin=YanziUiKit.dp(a,7);metrics.addView(m2,gap);
        LinearLayout.LayoutParams gap2=new LinearLayout.LayoutParams(0,-2,1f);gap2.leftMargin=YanziUiKit.dp(a,7);metrics.addView(m3,gap2);
        hero.addView(metrics);
        parent.addView(hero,YanziUiKit.cardLp(a));

        parent.addView(YanziUiKit.sectionLabel(a,"开发工具"));
        LinearLayout tools=new LinearLayout(a);tools.setOrientation(LinearLayout.HORIZONTAL);
        tools.addView(YanziUiKit.actionTile(a,"sync",YanziUiKit.GREEN,"同步","账号小程序",sync),new LinearLayout.LayoutParams(0,-2,1f));
        LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1f);lp.leftMargin=YanziUiKit.dp(a,7);
        tools.addView(YanziUiKit.actionTile(a,"apps",YanziUiKit.BLUE,"应用中心","获取与发布",appCenter),lp);
        LinearLayout.LayoutParams lp2=new LinearLayout.LayoutParams(0,-2,1f);lp2.leftMargin=YanziUiKit.dp(a,7);
        tools.addView(YanziUiKit.actionTile(a,"file-document-outline",YanziUiKit.ORANGE,"文档","API 参考",docs),lp2);
        LinearLayout.LayoutParams lp3=new LinearLayout.LayoutParams(0,-2,1f);lp3.leftMargin=YanziUiKit.dp(a,7);
        tools.addView(YanziUiKit.actionTile(a,"console",YanziUiKit.PURPLE,"终端","运行 JS",terminal),lp3);
        LinearLayout.LayoutParams toolWrap=YanziUiKit.cardLp(a);
        parent.addView(tools,toolWrap);
        parent.addView(YanziUiKit.sectionLabel(a,"工作区"));
        return new Result(count,syncTv);
    }
}

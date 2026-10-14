package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.view.Gravity;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

final class DesktopDashboardView {
    static final String COMPUTER_ICON = "mdi:desktop-classic";

    private static final class ActionRef {
        final LinearLayout root;
        final ImageView icon;
        final TextView label;
        final int accent;

        ActionRef(LinearLayout root, ImageView icon, TextView label, int accent) {
            this.root = root;
            this.icon = icon;
            this.label = label;
            this.accent = accent;
        }

        void setActive(boolean active) {
            int color = active ? accent : YanziUiKit.MUTED;
            icon.setColorFilter(color);
            label.setTextColor(color);
        }
    }

    static final class Result {
        final ActionRef[] actions;
        Result(ActionRef[] actions) { this.actions = actions; }
        void select(int index) {
            for (int i = 0; i < actions.length; i++) {if(actions[i]!=null)actions[i].setActive(i == index);};
        }
        void conversation(String name, Runnable choose) {
            ActionRef chat=actions[0];
            chat.label.setText(name);
            chat.label.setSingleLine(true);
            chat.label.setMaxWidth(YanziUiKit.dp(chat.root.getContext(),90));
            chat.label.setEllipsize(android.text.TextUtils.TruncateAt.END);
            chat.root.setContentDescription("聊天对象："+name);
            if (name.contains("反重力")) {
                chat.icon.setImageDrawable(YanziUiKit.getAntigravityLogo(chat.root.getContext()));
                chat.icon.setColorFilter(null);
            } else {
                android.graphics.Path path = MobileIconLibrary.resolveOrDefault("chat");
                chat.icon.setImageDrawable(new PathDrawable(path, chat.accent));
            }
            if(chat.icon.getParent()==chat.root) {
                chat.root.removeView(chat.icon);
                LinearLayout icons=new LinearLayout(chat.root.getContext());
                icons.setGravity(Gravity.CENTER);
                icons.addView(chat.icon,new LinearLayout.LayoutParams(YanziUiKit.dp(chat.root.getContext(),32),YanziUiKit.dp(chat.root.getContext(),32)));
                TextView arrow=YanziUiKit.text(chat.root.getContext(),"⌄",18,YanziUiKit.BLUE,true);
                arrow.setGravity(Gravity.CENTER); arrow.setContentDescription("切换聊天对象");
                arrow.setOnClickListener(v->choose.run());
                icons.addView(arrow,new LinearLayout.LayoutParams(YanziUiKit.dp(chat.root.getContext(),40),YanziUiKit.dp(chat.root.getContext(),44)));
                chat.root.addView(icons,0);
            }
            chat.root.setOnLongClickListener(v->{choose.run();return true;});
        }
    }

    private DesktopDashboardView() {}

    static Result populate(
            Activity a,
            LinearLayout parent,
            Runnable chat,
            Runnable apps,
            Runnable files,
            Runnable shell) {

        LinearLayout tools = new LinearLayout(a);
        tools.setOrientation(LinearLayout.HORIZONTAL);
        tools.setGravity(Gravity.CENTER_VERTICAL);

        ActionRef chatAction = flatAction(a, "chat", YanziUiKit.BLUE, "聊天", chat);
        ActionRef appAction = flatAction(a, "apps", YanziUiKit.PURPLE, "小程序", apps);
        ActionRef fileAction = flatAction(a, "folder-outline", YanziUiKit.ORANGE, "文件", files);
        ActionRef shellAction = flatAction(a, "console", YanziUiKit.GREEN, "终端", shell);

        tools.addView(chatAction.root, weighted());
        tools.addView(appAction.root, weighted());
        tools.addView(fileAction.root, weighted());
        tools.addView(shellAction.root, weighted());

        LinearLayout.LayoutParams toolsLp = new LinearLayout.LayoutParams(-1, -2);
        toolsLp.bottomMargin = YanziUiKit.dp(a, 10);
        parent.addView(tools, toolsLp);

        Result result = new Result(new ActionRef[]{chatAction, appAction, fileAction, shellAction});
        result.select(0);
        return result;
    }

    private static ActionRef flatAction(
            Activity a,
            String iconName,
            int accent,
            String title,
            Runnable action) {

        LinearLayout item = new LinearLayout(a);
        item.setOrientation(LinearLayout.VERTICAL);
        item.setGravity(Gravity.CENTER);
        item.setPadding(
                YanziUiKit.dp(a, 4),
                YanziUiKit.dp(a, 10),
                YanziUiKit.dp(a, 4),
                YanziUiKit.dp(a, 10));
        item.setClickable(true);
        item.setFocusable(true);
        if (action != null) item.setOnClickListener(v -> action.run());

        ImageView icon = YanziUiKit.icon(a, iconName, accent, 27);
        item.addView(icon, new LinearLayout.LayoutParams(
                YanziUiKit.dp(a, 32),
                YanziUiKit.dp(a, 32)));

        TextView label = YanziUiKit.text(a, title, 12, YanziUiKit.TEXT, true);
        label.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams labelLp = new LinearLayout.LayoutParams(-2, -2);
        labelLp.topMargin = YanziUiKit.dp(a, 7);
        item.addView(label, labelLp);

        return new ActionRef(item, icon, label, accent);
    }

    private static LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, -2, 1f);
    }
}

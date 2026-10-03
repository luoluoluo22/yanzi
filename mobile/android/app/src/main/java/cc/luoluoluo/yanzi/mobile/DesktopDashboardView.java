package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.view.Gravity;
import android.view.View;
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
        final TextView state;
        final ActionRef[] actions;

        Result(TextView state, ActionRef[] actions) {
            this.state = state;
            this.actions = actions;
        }

        void select(int index) {
            for (int i = 0; i < actions.length; i++) {
                actions[i].setActive(i == index);
            }
        }

        void update(boolean connected, String type) {
            if (!connected) {
                state.setText("离线");
                state.setTextColor(YanziUiKit.RED);
                return;
            }
            String channel = "lan".equals(type) ? "局域网" : "云端";
            state.setText(channel);
            state.setTextColor(YanziUiKit.GREEN);
        }
    }

    private DesktopDashboardView() {}

    static Result populate(
            Activity a,
            LinearLayout parent,
            Runnable chat,
            Runnable apps,
            Runnable files,
            Runnable shell,
            Runnable details) {

        LinearLayout info = new LinearLayout(a);
        info.setOrientation(LinearLayout.HORIZONTAL);
        info.setGravity(Gravity.CENTER_VERTICAL);
        info.setPadding(
                YanziUiKit.dp(a, 4),
                YanziUiKit.dp(a, 6),
                YanziUiKit.dp(a, 4),
                YanziUiKit.dp(a, 14));

        ImageView computer = YanziUiKit.icon(a, COMPUTER_ICON, YanziUiKit.TEXT, 28);
        computer.setContentDescription("我的电脑");
        computer.setClickable(true);
        computer.setFocusable(true);
        computer.setOnClickListener(v -> details.run());
        TextView state = YanziUiKit.text(a, "检测中…", 12, YanziUiKit.SECONDARY, false);
        LinearLayout.LayoutParams stateLp = new LinearLayout.LayoutParams(-2, -2);
        stateLp.setMarginStart(YanziUiKit.dp(a, 12));

        info.addView(computer, new LinearLayout.LayoutParams(
                YanziUiKit.dp(a, 48), YanziUiKit.dp(a, 48)));
        computer.setPadding(YanziUiKit.dp(a, 8), YanziUiKit.dp(a, 8),
                YanziUiKit.dp(a, 8), YanziUiKit.dp(a, 8));
        info.addView(state, stateLp);
        parent.addView(info);

        parent.addView(YanziUiKit.sectionLabel(a, "远程操作"));

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

        Result result = new Result(state, new ActionRef[]{chatAction, appAction, fileAction, shellAction});
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

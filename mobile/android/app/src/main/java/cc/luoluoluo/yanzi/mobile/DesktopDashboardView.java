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
        final ImageView stateIcon;
        final ActionRef[] actions;

        Result(ImageView stateIcon, ActionRef[] actions) {
            this.stateIcon = stateIcon;
            this.actions = actions;
        }

        void select(int index) {
            for (int i = 0; i < actions.length; i++) {
                actions[i].setActive(i == index);
            }
        }

        void update(boolean connected, String type) {
            String iconName;
            int color;
            String description;
            if (!connected) {
                iconName = "cloud-off-outline";
                color = YanziUiKit.RED;
                description = "离线";
            } else if ("reconnecting".equals(type)) {
                iconName = "lan-connect";
                color = YanziUiKit.ORANGE;
                description = "局域网重连中";
            } else if ("lan".equals(type)) {
                iconName = "lan-connect";
                color = YanziUiKit.GREEN;
                description = "局域网";
            } else {
                iconName = "cloud-outline";
                color = YanziUiKit.BLUE;
                description = "云端";
            }
            stateIcon.setImageDrawable(new PathDrawable(
                    MobileIconLibrary.resolveOrDefault(iconName), color));
            stateIcon.setContentDescription(description);
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

        ImageView stateIcon = YanziUiKit.icon(a, "cloud-off-outline", YanziUiKit.MUTED, 28);
        stateIcon.setContentDescription("检测连接状态");
        stateIcon.setClickable(true);
        stateIcon.setFocusable(true);
        stateIcon.setOnClickListener(v -> details.run());

        LinearLayout.LayoutParams iconLp = new LinearLayout.LayoutParams(
                YanziUiKit.dp(a, 48), YanziUiKit.dp(a, 48));
        info.addView(stateIcon, iconLp);
        stateIcon.setPadding(
                YanziUiKit.dp(a, 8),
                YanziUiKit.dp(a, 8),
                YanziUiKit.dp(a, 8),
                YanziUiKit.dp(a, 8));
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

        Result result = new Result(stateIcon,
                new ActionRef[]{chatAction, appAction, fileAction, shellAction});
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

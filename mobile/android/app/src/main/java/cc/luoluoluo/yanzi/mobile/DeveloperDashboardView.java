package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.view.Gravity;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

final class DeveloperDashboardView {
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
        final ActionRef miniPrograms;
        final ActionRef catalog;
        final ActionRef docs;
        final ActionRef terminal;

        Result(ActionRef miniPrograms, ActionRef catalog, ActionRef docs, ActionRef terminal) {
            this.miniPrograms = miniPrograms;
            this.catalog = catalog;
            this.docs = docs;
            this.terminal = terminal;
        }

        void selectWorkspace(int mobileSubTabIndex) {
            miniPrograms.setActive(mobileSubTabIndex == 0);
            catalog.setActive(false);
            docs.setActive(mobileSubTabIndex == 1);
            terminal.setActive(mobileSubTabIndex == 2);
        }
    }

    private DeveloperDashboardView() {}

    static Result populate(
            Activity a,
            LinearLayout parent,
            Runnable miniPrograms,
            Runnable catalog,
            Runnable docs,
            Runnable terminal) {

        parent.addView(YanziUiKit.sectionLabel(a, "手机应用与工具"));

        LinearLayout tools = new LinearLayout(a);
        tools.setOrientation(LinearLayout.HORIZONTAL);
        tools.setGravity(Gravity.CENTER_VERTICAL);

        ActionRef mini = flatAction(a, "apps", YanziUiKit.BLUE, "小程序", miniPrograms);
        ActionRef store = flatAction(a, "view-grid-outline", YanziUiKit.GREEN, "应用中心", catalog);
        ActionRef doc = flatAction(a, "file-document-outline", YanziUiKit.ORANGE, "文档", docs);
        ActionRef shell = flatAction(a, "console", YanziUiKit.PURPLE, "终端", terminal);

        tools.addView(mini.root, weighted());
        tools.addView(store.root, weighted());
        tools.addView(doc.root, weighted());
        tools.addView(shell.root, weighted());

        LinearLayout.LayoutParams toolsLp = new LinearLayout.LayoutParams(-1, -2);
        toolsLp.bottomMargin = YanziUiKit.dp(a, 8);
        parent.addView(tools, toolsLp);

        Result result = new Result(mini, store, doc, shell);
        result.selectWorkspace(0);
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

        ImageView icon = YanziUiKit.icon(a, iconName, YanziUiKit.MUTED, 27);
        item.addView(icon, new LinearLayout.LayoutParams(
                YanziUiKit.dp(a, 32),
                YanziUiKit.dp(a, 32)));

        TextView label = YanziUiKit.text(a, title, 12, YanziUiKit.MUTED, true);
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

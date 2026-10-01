package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.content.SharedPreferences;
import android.content.res.Resources;
import android.graphics.Color;
import android.graphics.Path;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * Start/home dashboard inspired by the Yanzi dark visual system.
 * Keeps the first tab focused on navigation and status while the real Yanm list
 * remains available in an expandable section.
 */
final class HomeDashboardView {
    private HomeDashboardView() {}

    static final class Result {
        final GridLayout yanmList;
        final LinearLayout yanmDetails;
        final TextView yanmComponentCount;
        final TextView yanmExpandedCount;
        final TextView desktopStatus;
        final TextView desktopChannel;
        final TextView desktopSync;

        Result(
                GridLayout yanmList,
                LinearLayout yanmDetails,
                TextView yanmComponentCount,
                TextView yanmExpandedCount,
                TextView desktopStatus,
                TextView desktopChannel,
                TextView desktopSync) {
            this.yanmList = yanmList;
            this.yanmDetails = yanmDetails;
            this.yanmComponentCount = yanmComponentCount;
            this.yanmExpandedCount = yanmExpandedCount;
            this.desktopStatus = desktopStatus;
            this.desktopChannel = desktopChannel;
            this.desktopSync = desktopSync;
        }

        void updateYanmState(int componentCount, int expandedCount) {
            yanmComponentCount.setText(componentCount + "\n组件");
            yanmExpandedCount.setText(expandedCount + "\n已展开");
        }

        void updateDesktopState(boolean connected, String type) {
            desktopStatus.setText(connected ? "在线运行" : "当前离线");
            desktopStatus.setTextColor(connected ? rgb("#35E3A4") : rgb("#FF7A83"));
            if (!connected) {
                desktopChannel.setText("通道\n等待连接");
                desktopSync.setText("同步\n待同步");
                return;
            }
            desktopChannel.setText("通道\n" + ("lan".equals(type) ? "局域网" : "云端"));
            desktopSync.setText("同步\n已同步");
        }
    }

    static Result populate(
            Activity activity,
            LinearLayout page,
            SharedPreferences prefs,
            Runnable quickStart,
            Runnable openApplications,
            Runnable openAiData,
            Runnable openDesktop,
            Runnable openSearch,
            Runnable openNotifications) {

        page.removeAllViews();
        page.setPadding(0, 0, 0, 0);

        final int blue = rgb("#4F8CFF");
        final int purple = rgb("#8B5CF6");
        final int green = rgb("#32D6B0");
        final int textPrimary = rgb("#F7FAFF");
        final int textSecondary = rgb("#A8B7CF");
        final int textMuted = rgb("#70809A");

        // Header
        LinearLayout header = new LinearLayout(activity);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(activity, 4), dp(activity, 2), dp(activity, 4), dp(activity, 8));

        ImageView logo = new ImageView(activity);
        logo.setImageResource(R.drawable.yanzi_logo);
        logo.setColorFilter(rgb("#78A9FF"));
        logo.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        LinearLayout.LayoutParams logoLp = new LinearLayout.LayoutParams(dp(activity, 36), dp(activity, 36));
        logoLp.rightMargin = dp(activity, 10);
        header.addView(logo, logoLp);

        LinearLayout brand = new LinearLayout(activity);
        brand.setOrientation(LinearLayout.VERTICAL);
        TextView title = text(activity, "燕子", 22, textPrimary, true);
        TextView subtitle = text(activity, "让数据流动，让 AI 为你工作", 12, textSecondary, false);
        brand.addView(title);
        brand.addView(subtitle);
        header.addView(brand, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        header.addView(roundIconButton(activity, "search", openSearch));
        LinearLayout.LayoutParams bellLp = new LinearLayout.LayoutParams(dp(activity, 38), dp(activity, 38));
        bellLp.leftMargin = dp(activity, 8);
        header.addView(roundIconButton(activity, "bell", openNotifications), bellLp);
        page.addView(header);

        // Hero
        FrameLayout hero = new FrameLayout(activity);
        hero.setBackground(gradient(
                new int[]{rgb("#0E2444"), rgb("#102A58"), rgb("#0B1730")},
                GradientDrawable.Orientation.TL_BR,
                22,
                rgb("#264B77")));
        hero.setPadding(dp(activity, 16), dp(activity, 14), dp(activity, 14), dp(activity, 14));

        View moon = new View(activity);
        GradientDrawable moonBg = new GradientDrawable(
                GradientDrawable.Orientation.TL_BR,
                new int[]{Color.argb(135, 101, 155, 255), Color.argb(25, 90, 115, 190)});
        moonBg.setShape(GradientDrawable.OVAL);
        moon.setBackground(moonBg);
        FrameLayout.LayoutParams moonLp = new FrameLayout.LayoutParams(dp(activity, 104), dp(activity, 104), Gravity.TOP | Gravity.END);
        moonLp.topMargin = dp(activity, -8);
        moonLp.rightMargin = dp(activity, -22);
        hero.addView(moon, moonLp);

        ImageView heroBird = new ImageView(activity);
        heroBird.setImageResource(R.drawable.yanzi_logo);
        heroBird.setColorFilter(rgb("#9FC4FF"));
        heroBird.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        heroBird.setAlpha(0.90f);
        FrameLayout.LayoutParams birdLp = new FrameLayout.LayoutParams(dp(activity, 96), dp(activity, 76), Gravity.END | Gravity.CENTER_VERTICAL);
        birdLp.rightMargin = dp(activity, 6);
        hero.addView(heroBird, birdLp);

        LinearLayout heroCopy = new LinearLayout(activity);
        heroCopy.setOrientation(LinearLayout.VERTICAL);
        FrameLayout.LayoutParams copyLp = new FrameLayout.LayoutParams(dp(activity, 230), ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.START | Gravity.CENTER_VERTICAL);
        hero.addView(heroCopy, copyLp);

        TextView heroTitle = text(activity, "连接一切数据\n让 AI 发挥更大价值", 20, Color.WHITE, true);
        heroTitle.setLineSpacing(0f, 1.05f);
        heroCopy.addView(heroTitle);

        TextView heroSub = text(activity, "接入应用 · 获取数据 · AI 智能处理", 11, rgb("#BDD0EB"), false);
        LinearLayout.LayoutParams heroSubLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        heroSubLp.topMargin = dp(activity, 6);
        heroCopy.addView(heroSub, heroSubLp);

        TextView heroButton = pillButton(activity, "快速开始  ›", rgb("#CBE2FF"), rgb("#0D294A"));
        heroButton.setOnClickListener(v -> quickStart.run());
        LinearLayout.LayoutParams heroButtonLp = new LinearLayout.LayoutParams(dp(activity, 126), dp(activity, 38));
        heroButtonLp.topMargin = dp(activity, 11);
        heroCopy.addView(heroButton, heroButtonLp);

        LinearLayout.LayoutParams heroLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(activity, 150));
        heroLp.bottomMargin = dp(activity, 10);
        page.addView(hero, heroLp);

        // Application center
        LinearLayout appsCard = card(activity, rgb("#101D2D"), rgb("#203A59"));
        appsCard.addView(sectionHeader(
                activity, "view-grid-outline", blue, "应用中心",
                "连接你常用的数据与小程序", "查看全部  ›", openApplications));
        LinearLayout appTiles = new LinearLayout(activity);
        appTiles.setOrientation(LinearLayout.HORIZONTAL);
        appTiles.setGravity(Gravity.CENTER);
        appTiles.setPadding(0, dp(activity, 10), 0, 0);
        appTiles.addView(appTile(activity, "file-document-outline", rgb("#F5B942"), "便签", "已接入"), weighted());
        appTiles.addView(appTile(activity, "calendar-month-outline", rgb("#F16C7A"), "日历", "已接入"), weighted());
        appTiles.addView(appTile(activity, "apps", blue, "小程序", "管理"), weighted());
        appTiles.addView(appTile(activity, "cloud-outline", purple, "WebDAV", "连接"), weighted());
        appTiles.addView(appTile(activity, "plus", rgb("#8291A8"), "更多", "添加"), weighted());
        appsCard.addView(appTiles);
        page.addView(appsCard, cardLp(activity));

        // AI data
        LinearLayout aiCard = card(activity, rgb("#151936"), rgb("#343065"));
        aiCard.addView(sectionHeader(
                activity, "database-outline", purple, "AI 数据接入",
                "让 AI 获取、理解并使用你的数据", "查看详情  ›", openAiData));

        LinearLayout steps = new LinearLayout(activity);
        steps.setOrientation(LinearLayout.HORIZONTAL);
        steps.setGravity(Gravity.CENTER_VERTICAL);
        steps.setPadding(0, dp(activity, 10), 0, 0);
        steps.addView(step(activity, "link-variant", "1. 选择应用", "授权数据"), weighted());
        steps.addView(chevron(activity));
        steps.addView(step(activity, "file-document-outline", "2. 选择数据", "部分或全部"), weighted());
        steps.addView(chevron(activity));
        steps.addView(step(activity, "creation", "3. AI 处理", "生成并执行"), weighted());
        aiCard.addView(steps);
        aiCard.setOnClickListener(v -> openAiData.run());
        page.addView(aiCard, cardLp(activity));

        // Yanm summary
        LinearLayout yanmCard = card(activity, rgb("#0F2228"), rgb("#214B55"));
        TextView enterYanm = linkText(activity, "进入燕幕  ›");
        LinearLayout yanmHead = sectionHeader(
                activity, "monitor-dashboard", green, "燕幕",
                "实时状态、运行记录和系统提示", null, null);
        yanmHead.addView(enterYanm);
        yanmCard.addView(yanmHead);

        LinearLayout stats = new LinearLayout(activity);
        stats.setOrientation(LinearLayout.HORIZONTAL);
        stats.setGravity(Gravity.CENTER);
        stats.setPadding(0, dp(activity, 10), 0, 0);
        TextView yanmCount = valueStat(activity, "0", "组件", blue);
        TextView expandedCount = valueStat(activity, "0", "已展开", purple);
        TextView syncState = valueStat(activity, "✓", "云同步", green);
        TextView healthState = valueStat(activity, "正常", "状态", rgb("#F3A845"));
        stats.addView(wrapStat(activity, yanmCount), weighted());
        stats.addView(wrapStat(activity, expandedCount), weighted());
        stats.addView(wrapStat(activity, syncState), weighted());
        stats.addView(wrapStat(activity, healthState), weighted());
        yanmCard.addView(stats);

        LinearLayout yanmDetails = new LinearLayout(activity);
        yanmDetails.setOrientation(LinearLayout.VERTICAL);
        yanmDetails.setVisibility(View.GONE);
        yanmDetails.setPadding(0, dp(activity, 12), 0, 0);

        GridLayout yanmList = new GridLayout(activity);
        yanmList.setColumnCount(1);
        yanmList.setAlignmentMode(GridLayout.ALIGN_BOUNDS);
        yanmList.setUseDefaultMargins(false);
        yanmDetails.addView(yanmList, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        yanmCard.addView(yanmDetails);

        enterYanm.setOnClickListener(v -> {
            boolean opening = yanmDetails.getVisibility() != View.VISIBLE;
            yanmDetails.setVisibility(opening ? View.VISIBLE : View.GONE);
            enterYanm.setText(opening ? "收起燕幕  ⌃" : "进入燕幕  ›");
        });
        page.addView(yanmCard, cardLp(activity));

        // Device status
        LinearLayout deviceCard = card(activity, rgb("#101C2B"), rgb("#253A59"));
        deviceCard.addView(sectionHeader(
                activity, "server-outline", rgb("#7EA4E8"), "设备状态",
                "查看电脑连接状态与同步通道", "查看详情  ›", openDesktop));

        LinearLayout deviceRow = new LinearLayout(activity);
        deviceRow.setOrientation(LinearLayout.HORIZONTAL);
        deviceRow.setGravity(Gravity.CENTER_VERTICAL);
        deviceRow.setPadding(0, dp(activity, 9), 0, 0);

        LinearLayout deviceName = new LinearLayout(activity);
        deviceName.setOrientation(LinearLayout.VERTICAL);
        TextView computer = text(activity, "我的电脑", 15, textPrimary, true);
        TextView live = text(activity, "检测中…", 12, textMuted, false);
        deviceName.addView(computer);
        deviceName.addView(live);
        deviceRow.addView(deviceName, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1.25f));

        TextView channel = metric(activity, "通道", "检测中");
        TextView sync = metric(activity, "同步", "检测中");
        deviceRow.addView(channel, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 0.85f));
        deviceRow.addView(sync, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 0.85f));
        deviceCard.addView(deviceRow);
        deviceCard.setOnClickListener(v -> openDesktop.run());
        page.addView(deviceCard, cardLp(activity));

        TextView footer = text(activity, "下拉可刷新燕幕与连接状态", 11, textMuted, false);
        footer.setGravity(Gravity.CENTER);
        footer.setPadding(0, dp(activity, 2), 0, dp(activity, 10));
        page.addView(footer);

        return new Result(yanmList, yanmDetails, yanmCount, expandedCount, live, channel, sync);
    }

    private static LinearLayout sectionHeader(
            Activity activity,
            String iconName,
            int accent,
            String titleText,
            String subtitleText,
            String link,
            Runnable action) {
        LinearLayout header = new LinearLayout(activity);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);

        FrameLayout iconChip = new FrameLayout(activity);
        iconChip.setBackground(roundRect(accentWithAlpha(accent, 52), 14, accentWithAlpha(accent, 100), 1));
        ImageView icon = icon(activity, iconName, Color.WHITE, 26);
        FrameLayout.LayoutParams iconLp = new FrameLayout.LayoutParams(dp(activity, 19), dp(activity, 19), Gravity.CENTER);
        iconChip.addView(icon, iconLp);
        LinearLayout.LayoutParams chipLp = new LinearLayout.LayoutParams(dp(activity, 36), dp(activity, 36));
        chipLp.rightMargin = dp(activity, 10);
        header.addView(iconChip, chipLp);

        LinearLayout copy = new LinearLayout(activity);
        copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(text(activity, titleText, 17, rgb("#F7FAFF"), true));
        copy.addView(text(activity, subtitleText, 11, rgb("#9EADC4"), false));
        header.addView(copy, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        if (link != null && !link.isEmpty()) {
            TextView more = linkText(activity, link);
            if (action != null) more.setOnClickListener(v -> action.run());
            header.addView(more);
        }
        return header;
    }

    private static LinearLayout card(Activity activity, int background, int stroke) {
        LinearLayout card = new LinearLayout(activity);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(activity, 10), dp(activity, 10), dp(activity, 10), dp(activity, 10));
        card.setBackground(roundRect(background, 20, stroke, 1));
        return card;
    }

    private static LinearLayout appTile(Activity activity, String iconName, int accent, String title, String status) {
        LinearLayout tile = new LinearLayout(activity);
        tile.setOrientation(LinearLayout.VERTICAL);
        tile.setGravity(Gravity.CENTER);
        tile.setPadding(dp(activity, 4), 0, dp(activity, 4), 0);

        FrameLayout iconChip = new FrameLayout(activity);
        iconChip.setBackground(roundRect(rgb("#182638"), 14, rgb("#243A56"), 1));
        iconChip.addView(icon(activity, iconName, accent, 23),
                new FrameLayout.LayoutParams(dp(activity, 20), dp(activity, 20), Gravity.CENTER));
        tile.addView(iconChip, new LinearLayout.LayoutParams(dp(activity, 36), dp(activity, 36)));

        TextView label = text(activity, title, 11, rgb("#F1F5FB"), false);
        label.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams labelLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        labelLp.topMargin = dp(activity, 4);
        tile.addView(label, labelLp);

        TextView state = text(activity, status, 10, "已接入".equals(status) ? rgb("#35E3A4") : rgb("#8190A8"), false);
        state.setGravity(Gravity.CENTER);
        tile.addView(state);
        return tile;
    }

    private static LinearLayout step(Activity activity, String iconName, String title, String subtitle) {
        LinearLayout step = new LinearLayout(activity);
        step.setOrientation(LinearLayout.VERTICAL);
        step.setGravity(Gravity.CENTER);

        FrameLayout iconChip = new FrameLayout(activity);
        iconChip.setBackground(roundRect(rgb("#22264B"), 24, rgb("#343A68"), 1));
        iconChip.addView(icon(activity, iconName, rgb("#C9C8FF"), 19),
                new FrameLayout.LayoutParams(dp(activity, 19), dp(activity, 19), Gravity.CENTER));
        step.addView(iconChip, new LinearLayout.LayoutParams(dp(activity, 36), dp(activity, 36)));

        TextView t = text(activity, title, 11, rgb("#EEF2FF"), true);
        t.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams titleLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        titleLp.topMargin = dp(activity, 4);
        step.addView(t, titleLp);

        TextView s = text(activity, subtitle, 10, rgb("#9FAAC2"), false);
        s.setGravity(Gravity.CENTER);
        step.addView(s);
        return step;
    }

    private static TextView chevron(Activity activity) {
        TextView arrow = text(activity, "›", 24, rgb("#7786A2"), false);
        arrow.setGravity(Gravity.CENTER);
        return arrow;
    }

    private static LinearLayout wrapStat(Activity activity, TextView value) {
        LinearLayout wrap = new LinearLayout(activity);
        wrap.setOrientation(LinearLayout.VERTICAL);
        wrap.setGravity(Gravity.CENTER);
        wrap.setBackground(roundRect(rgb("#142234"), 14, rgb("#203750"), 1));
        wrap.setPadding(dp(activity, 4), dp(activity, 6), dp(activity, 4), dp(activity, 6));
        wrap.addView(value);
        return wrap;
    }

    private static TextView valueStat(Activity activity, String value, String label, int color) {
        TextView tv = text(activity, value + "\n" + label, 12, color, true);
        tv.setGravity(Gravity.CENTER);
        tv.setLineSpacing(0f, 1.05f);
        return tv;
    }

    private static TextView metric(Activity activity, String label, String value) {
        TextView tv = text(activity, label + "\n" + value, 12, rgb("#C7D3E6"), false);
        tv.setGravity(Gravity.CENTER);
        tv.setLineSpacing(0f, 1.08f);
        return tv;
    }

    private static TextView pillButton(Activity activity, String text, int background, int foreground) {
        TextView button = text(activity, text, 14, foreground, true);
        button.setGravity(Gravity.CENTER);
        button.setBackground(roundRect(background, 22, Color.TRANSPARENT, 0));
        button.setClickable(true);
        button.setFocusable(true);
        return button;
    }

    private static View roundIconButton(Activity activity, String iconName, Runnable action) {
        FrameLayout button = new FrameLayout(activity);
        button.setBackground(roundRect(rgb("#152235"), 23, rgb("#273B56"), 1));
        button.setClickable(true);
        button.setFocusable(true);
        if (action != null) button.setOnClickListener(v -> action.run());
        button.addView(icon(activity, iconName, rgb("#EDF4FF"), 20),
                new FrameLayout.LayoutParams(dp(activity, 20), dp(activity, 20), Gravity.CENTER));
        button.setLayoutParams(new LinearLayout.LayoutParams(dp(activity, 38), dp(activity, 38)));
        return button;
    }

    private static TextView linkText(Activity activity, String value) {
        TextView link = text(activity, value, 12, rgb("#B8C8E1"), false);
        link.setGravity(Gravity.CENTER_VERTICAL | Gravity.END);
        link.setPadding(dp(activity, 8), dp(activity, 8), 0, dp(activity, 8));
        link.setClickable(true);
        return link;
    }

    private static ImageView icon(Activity activity, String iconName, int color, int sizeDp) {
        ImageView icon = new ImageView(activity);
        Path path = MobileIconLibrary.resolveOrDefault(iconName);
        icon.setImageDrawable(new PathDrawable(path, color));
        icon.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        icon.setLayoutParams(new ViewGroup.LayoutParams(dp(activity, sizeDp), dp(activity, sizeDp)));
        return icon;
    }

    private static TextView text(Activity activity, String value, float sp, int color, boolean bold) {
        TextView tv = new TextView(activity);
        tv.setText(value);
        tv.setTextSize(sp);
        tv.setTextColor(color);
        tv.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        tv.setIncludeFontPadding(false);
        return tv;
    }

    private static GradientDrawable gradient(int[] colors, GradientDrawable.Orientation orientation, int radiusDp, int strokeColor) {
        float density = Resources.getSystem().getDisplayMetrics().density;
        GradientDrawable background = new GradientDrawable(orientation, colors);
        background.setCornerRadius(radiusDp * density);
        background.setStroke(Math.max(1, Math.round(density)), strokeColor);
        return background;
    }

    private static GradientDrawable roundRect(int color, int radiusDp, int strokeColor, int strokeWidthDp) {
        float density = Resources.getSystem().getDisplayMetrics().density;
        GradientDrawable background = new GradientDrawable();
        background.setColor(color);
        background.setCornerRadius(radiusDp * density);
        if (strokeWidthDp > 0 && strokeColor != Color.TRANSPARENT) {
            background.setStroke(Math.max(1, Math.round(strokeWidthDp * density)), strokeColor);
        }
        return background;
    }

    private static LinearLayout.LayoutParams cardLp(Activity activity) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = dp(activity, 8);
        return lp;
    }

    private static LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
    }

    private static int accentWithAlpha(int color, int alpha) {
        return Color.argb(alpha, Color.red(color), Color.green(color), Color.blue(color));
    }

    private static int rgb(String hex) {
        return Color.parseColor(hex);
    }

    private static int dp(Activity activity, int value) {
        float density = activity.getResources().getDisplayMetrics().density;
        return Math.max(1, Math.round(value * density));
    }
}

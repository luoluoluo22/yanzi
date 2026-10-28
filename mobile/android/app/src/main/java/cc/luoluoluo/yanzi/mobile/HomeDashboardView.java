package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
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

        Result(
                GridLayout yanmList,
                LinearLayout yanmDetails,
                TextView yanmComponentCount,
                TextView yanmExpandedCount) {
            this.yanmList = yanmList;
            this.yanmDetails = yanmDetails;
            this.yanmComponentCount = yanmComponentCount;
            this.yanmExpandedCount = yanmExpandedCount;
        }

        void updateYanmState(int componentCount, int expandedCount) {
            yanmComponentCount.setText(componentCount + "\n组件");
            yanmExpandedCount.setText(expandedCount + "\n已展开");
        }
    }

    static Result populate(
            Activity activity,
            LinearLayout page) {

        page.removeAllViews();
        page.setPadding(0, 0, 0, 0);

        final int blue = rgb("#4F8CFF");
        final int purple = rgb("#8B5CF6");
        final int green = rgb("#32D6B0");
        final int textPrimary = rgb("#F7FAFF");
        final int textSecondary = rgb("#A8B7CF");
        final int textMuted = rgb("#70809A");

        // Yanm summary
        LinearLayout yanmCard = card(activity, rgb("#0F2228"), rgb("#214B55"));
        TextView enterYanm = linkText(activity, "展开  ›");
        LinearLayout yanmHead = sectionHeader(
                activity, "monitor-dashboard", green, "组件",
                "电脑与手机共享的燕幕内容", null, null);
        yanmHead.addView(enterYanm);
        yanmCard.addView(yanmHead);

        LinearLayout stats = new LinearLayout(activity);
        stats.setOrientation(LinearLayout.HORIZONTAL);
        stats.setGravity(Gravity.CENTER);
        stats.setPadding(0, dp(activity, 10), 0, 0);
        TextView yanmCount = valueStat(activity, "0", "组件", blue);
        TextView expandedCount = valueStat(activity, "0", "已展开", purple);
        TextView syncState = valueStat(activity, "✓", "已同步", green);
        stats.addView(wrapStat(activity, yanmCount), weighted());
        stats.addView(wrapStat(activity, expandedCount), weighted());
        stats.addView(wrapStat(activity, syncState), weighted());
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
            enterYanm.setText(opening ? "收起  ⌃" : "展开  ›");
        });
        page.addView(yanmCard, cardLp(activity));

        TextView footer = text(activity, "下拉刷新燕幕", 11, textMuted, false);
        footer.setGravity(Gravity.CENTER);
        footer.setPadding(0, dp(activity, 2), 0, dp(activity, 10));
        page.addView(footer);

        return new Result(yanmList, yanmDetails, yanmCount, expandedCount);
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

package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.content.res.Resources;
import android.graphics.Color;
import android.graphics.Path;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.*;

final class YanziUiKit {
    static final int BG = Color.rgb(5, 13, 24);
    static final int CARD = Color.rgb(15, 29, 46);
    static final int CARD_ALT = Color.rgb(18, 34, 53);
    static final int STROKE = Color.rgb(35, 58, 86);
    static final int TEXT = Color.rgb(247, 250, 255);
    static final int SECONDARY = Color.rgb(166, 182, 207);
    static final int MUTED = Color.rgb(112, 128, 154);
    static final int BLUE = Color.rgb(79, 140, 255);
    static final int PURPLE = Color.rgb(139, 92, 246);
    static final int GREEN = Color.rgb(50, 214, 176);
    static final int ORANGE = Color.rgb(243, 168, 69);
    static final int RED = Color.rgb(241, 108, 122);

    private YanziUiKit() {}

    static int dp(Context c, int value) {
        return Math.max(1, Math.round(value * c.getResources().getDisplayMetrics().density));
    }

    static TextView text(Context c, String value, float sp, int color, boolean bold) {
        TextView v = new TextView(c);
        v.setText(value);
        v.setTextSize(sp);
        v.setTextColor(color);
        v.setTypeface(bold ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        v.setIncludeFontPadding(false);
        return v;
    }

    static GradientDrawable bg(int color, int radiusDp, int strokeColor, int strokeDp) {
        float density = Resources.getSystem().getDisplayMetrics().density;
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(radiusDp * density);
        if (strokeDp > 0) d.setStroke(Math.max(1, Math.round(strokeDp * density)), strokeColor);
        return d;
    }

    static LinearLayout card(Context c) {
        LinearLayout v = new LinearLayout(c);
        v.setOrientation(LinearLayout.VERTICAL);
        v.setPadding(dp(c, 14), dp(c, 14), dp(c, 14), dp(c, 14));
        v.setBackground(bg(CARD, 20, STROKE, 1));
        return v;
    }

    static LinearLayout tintedCard(Context c, int color, int stroke) {
        LinearLayout v = card(c);
        v.setBackground(bg(color, 20, stroke, 1));
        return v;
    }

    static LinearLayout.LayoutParams cardLp(Context c) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.bottomMargin = dp(c, 10);
        return lp;
    }

    static LinearLayout header(Context c, String title, String subtitle, String iconName, int accent) {
        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        FrameLayout chip = new FrameLayout(c);
        chip.setBackground(bg(Color.argb(42, Color.red(accent), Color.green(accent), Color.blue(accent)), 15, Color.argb(100, Color.red(accent), Color.green(accent), Color.blue(accent)), 1));
        ImageView icon = icon(c, iconName, accent, 23);
        chip.addView(icon, new FrameLayout.LayoutParams(dp(c,23), dp(c,23), Gravity.CENTER));
        LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(dp(c,46), dp(c,46));
        cp.rightMargin = dp(c,12);
        row.addView(chip, cp);
        LinearLayout copy = new LinearLayout(c);
        copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(text(c, title, 21, TEXT, true));
        TextView sub = text(c, subtitle, 12, SECONDARY, false);
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(-2,-2);
        sp.topMargin=dp(c,3);
        copy.addView(sub, sp);
        row.addView(copy, new LinearLayout.LayoutParams(0,-2,1f));
        return row;
    }

    static TextView link(Context c, String value, Runnable action) {
        TextView v = text(c, value + "  ›", 13, SECONDARY, false);
        v.setGravity(Gravity.CENTER_VERTICAL);
        v.setPadding(dp(c,8), dp(c,8), 0, dp(c,8));
        v.setClickable(true);
        v.setFocusable(true);
        if (action != null) v.setOnClickListener(x -> action.run());
        return v;
    }

    static TextView primaryButton(Context c, String value, Runnable action) {
        TextView v = text(c, value, 14, Color.rgb(10,37,67), true);
        v.setGravity(Gravity.CENTER);
        v.setBackground(bg(Color.rgb(200,225,255), 20, Color.TRANSPARENT, 0));
        v.setClickable(true);
        v.setFocusable(true);
        if (action != null) v.setOnClickListener(x -> action.run());
        return v;
    }

    static TextView secondaryButton(Context c, String value, Runnable action) {
        TextView v = text(c, value, 13, TEXT, true);
        v.setGravity(Gravity.CENTER);
        v.setBackground(bg(Color.rgb(23,40,62), 16, STROKE, 1));
        v.setClickable(true);
        v.setFocusable(true);
        if (action != null) v.setOnClickListener(x -> action.run());
        return v;
    }

    static LinearLayout actionTile(Context c, String iconName, int accent, String title, String subtitle, Runnable action) {
        LinearLayout tile = new LinearLayout(c);
        tile.setOrientation(LinearLayout.VERTICAL);
        tile.setGravity(Gravity.CENTER);
        tile.setPadding(dp(c,6), dp(c,10), dp(c,6), dp(c,9));
        tile.setBackground(bg(Color.rgb(20,35,54), 16, Color.rgb(31,54,80), 1));
        tile.setClickable(true);
        tile.setFocusable(true);
        if (action != null) tile.setOnClickListener(v -> action.run());
        FrameLayout chip = new FrameLayout(c);
        chip.setBackground(bg(Color.argb(40, Color.red(accent), Color.green(accent), Color.blue(accent)), 16, Color.argb(100, Color.red(accent), Color.green(accent), Color.blue(accent)), 1));
        chip.addView(icon(c, iconName, accent, 21), new FrameLayout.LayoutParams(dp(c,21),dp(c,21),Gravity.CENTER));
        tile.addView(chip, new LinearLayout.LayoutParams(dp(c,40),dp(c,40)));
        TextView t = text(c,title,12,TEXT,true); t.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams tp=new LinearLayout.LayoutParams(-1,-2);tp.topMargin=dp(c,6);tile.addView(t,tp);
        TextView s = text(c,subtitle,10,MUTED,false); s.setGravity(Gravity.CENTER); tile.addView(s);
        return tile;
    }

    static LinearLayout metric(Context c, String value, String label, int accent) {
        LinearLayout box = new LinearLayout(c);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER);
        box.setPadding(dp(c,6),dp(c,8),dp(c,6),dp(c,8));
        box.setBackground(bg(Color.rgb(20,35,54),14,Color.rgb(31,54,80),1));
        TextView valueTv=text(c,value,17,accent,true); valueTv.setGravity(Gravity.CENTER);
        TextView labelTv=text(c,label,10,MUTED,false);labelTv.setGravity(Gravity.CENTER);
        box.addView(valueTv);box.addView(labelTv);
        return box;
    }

    static LinearLayout row(Context c, String iconName, int accent, String title, String subtitle, Runnable action) {
        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(c,12),dp(c,12),dp(c,10),dp(c,12));
        row.setBackground(bg(CARD_ALT,15,STROKE,1));
        ImageView icon=icon(c,iconName,accent,22);
        LinearLayout.LayoutParams ip=new LinearLayout.LayoutParams(dp(c,30),dp(c,30));ip.rightMargin=dp(c,10);row.addView(icon,ip);
        LinearLayout copy=new LinearLayout(c);copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(text(c,title,14,TEXT,true));
        if(subtitle!=null&&!subtitle.isEmpty())copy.addView(text(c,subtitle,11,MUTED,false));
        row.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
        TextView arrow=text(c,"›",22,SECONDARY,false);row.addView(arrow);
        row.setClickable(true);row.setFocusable(true);if(action!=null)row.setOnClickListener(v->action.run());
        return row;
    }

    static LinearLayout switchRow(Context c, String iconName, int accent, String title, String subtitle, boolean checked, android.widget.CompoundButton.OnCheckedChangeListener listener) {
        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(c,12),dp(c,10),dp(c,10),dp(c,10));
        row.setBackground(bg(CARD_ALT,15,STROKE,1));
        ImageView icon=icon(c,iconName,accent,22);
        LinearLayout.LayoutParams ip=new LinearLayout.LayoutParams(dp(c,30),dp(c,30));ip.rightMargin=dp(c,10);row.addView(icon,ip);
        LinearLayout copy=new LinearLayout(c);copy.setOrientation(LinearLayout.VERTICAL);
        copy.addView(text(c,title,14,TEXT,true));
        if(subtitle!=null&&!subtitle.isEmpty())copy.addView(text(c,subtitle,11,MUTED,false));
        row.addView(copy,new LinearLayout.LayoutParams(0,-2,1f));
        Switch sw=new Switch(c);sw.setChecked(checked);sw.setOnCheckedChangeListener(listener);row.addView(sw);
        return row;
    }

    static TextView sectionLabel(Context c, String title) {
        TextView v=text(c,title,13,SECONDARY,true);
        v.setPadding(dp(c,3),dp(c,7),0,dp(c,8));
        return v;
    }

    static ImageView icon(Context c, String iconName, int color, int size) {
        ImageView image=new ImageView(c);
        Path path=MobileIconLibrary.resolveOrDefault(iconName);
        image.setImageDrawable(new PathDrawable(path,color));
        image.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        image.setLayoutParams(new ViewGroup.LayoutParams(dp(c,size),dp(c,size)));
        return image;
    }
}

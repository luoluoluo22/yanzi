package cc.luoluoluo.yanzi.mobile;

import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.SystemClock;
import android.text.Layout;
import android.text.Spannable;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.BackgroundColorSpan;
import android.text.style.ForegroundColorSpan;
import android.text.style.StrikethroughSpan;
import android.text.style.StyleSpan;
import android.text.style.TypefaceSpan;
import android.text.style.URLSpan;
import android.text.style.UnderlineSpan;
import android.util.Patterns;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.TableLayout;
import android.widget.TableRow;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Shared text-message renderer. Keeps text selectable while opening HTTP(S) links
 * on a short tap, and treats incoming Markdown as text (not executable HTML).
 */
final class ChatRichText {
    private static final int BODY = Color.rgb(241, 245, 249);
    private static final int MUTED = Color.rgb(203, 213, 225);
    private static final int LINK = Color.rgb(125, 211, 252);
    private static final Pattern INLINE = Pattern.compile(
            "\\[([^\\]\\n]+)\\]\\((https?://[^\\s)]+)\\)|`([^`\\n]+)`|\\*\\*([^*\\n]+)\\*\\*|__([^_\\n]+)__|~~([^~\\n]+)~~|(?<!\\*)\\*([^*\\n]+)\\*(?!\\*)|(?<!_)_([^_\\n]+)_(?!_)",
            Pattern.CASE_INSENSITIVE);
    private static final Pattern HEADING = Pattern.compile("^\\s{0,3}(#{1,6})\\s+(.+)$");
    private static final Pattern LIST = Pattern.compile("^(\\s*)([-*+]\\s+|\\d+[.)]\\s+)(.*)$");
    private static final Pattern RULE = Pattern.compile("^\\s*(?:-{3,}|\\*{3,}|_{3,})\\s*$");
    private static final Pattern TABLE_DIVIDER = Pattern.compile("^\\s*\\|?\\s*:?-{3,}:?\\s*(?:\\|\\s*:?-{3,}:?\\s*)+\\|?\\s*$");

    private ChatRichText() {}

    static void render(LinearLayout parent, String raw, int maxWidth) {
        if (raw == null || raw.isEmpty()) return;
        String[] lines = raw.replace("\r\n", "\n").replace('\r', '\n').split("\n", -1);
        StringBuilder paragraph = new StringBuilder();
        for (int i = 0; i < lines.length; ) {
            String line = lines[i];
            String stripped = line.trim();
            if (stripped.isEmpty()) {
                flush(parent, paragraph, maxWidth);
                i++;
                continue;
            }
            if (stripped.startsWith("```") || stripped.startsWith("~~~")) {
                flush(parent, paragraph, maxWidth);
                String fence = stripped.substring(0, 3);
                String language = stripped.substring(3).trim();
                StringBuilder block = new StringBuilder();
                i++;
                while (i < lines.length && !lines[i].trim().startsWith(fence)) {
                    if (block.length() > 0) block.append('\n');
                    block.append(lines[i++]);
                }
                if (i < lines.length) i++;
                codeBlock(parent, block.toString(), language, maxWidth);
                continue;
            }
            if (i + 1 < lines.length && line.contains("|") && TABLE_DIVIDER.matcher(lines[i + 1]).matches()) {
                flush(parent, paragraph, maxWidth);
                ArrayList<String> rows = new ArrayList<>();
                rows.add(line);
                i += 2;
                while (i < lines.length && lines[i].contains("|") && !lines[i].trim().isEmpty()) {
                    rows.add(lines[i++]);
                }
                table(parent, rows, maxWidth);
                continue;
            }
            Matcher heading = HEADING.matcher(line);
            Matcher list = LIST.matcher(line);
            if (heading.matches()) {
                flush(parent, paragraph, maxWidth);
                text(parent, heading.group(2), maxWidth, Math.max(16, 22 - heading.group(1).length()),
                        true, 8, 3, BODY);
            } else if (stripped.startsWith(">")) {
                flush(parent, paragraph, maxWidth);
                StringBuilder quote = new StringBuilder();
                while (i < lines.length && lines[i].trim().startsWith(">")) {
                    if (quote.length() > 0) quote.append('\n');
                    quote.append(lines[i].trim().replaceFirst("^>\\s?", ""));
                    i++;
                }
                quote(parent, quote.toString(), maxWidth);
                continue;
            } else if (RULE.matcher(line).matches()) {
                flush(parent, paragraph, maxWidth);
                View divider = new View(parent.getContext());
                divider.setBackgroundColor(Color.rgb(71, 85, 105));
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, dp(parent.getContext(), 1));
                lp.setMargins(0, dp(parent.getContext(), 9), 0, dp(parent.getContext(), 9));
                parent.addView(divider, lp);
            } else if (list.matches()) {
                flush(parent, paragraph, maxWidth);
                String marker = list.group(2).trim();
                String item = list.group(3);
                if (item.startsWith("[ ] ")) {
                    marker = "☐";
                    item = item.substring(4);
                } else if (item.matches("(?i)^\\[x\\] .*")) {
                    marker = "☑";
                    item = item.substring(4);
                } else if (!Character.isDigit(marker.charAt(0))) {
                    marker = "•";
                }
                int indent = Math.min(3, list.group(1).length() / 2);
                TextView bullet = text(parent, marker + "  " + item, maxWidth, 15, false, 2, 2, BODY);
                bullet.setPadding(dp(parent.getContext(), 4 + indent * 12), 0, 0, 0);
            } else {
                if (paragraph.length() > 0) paragraph.append('\n');
                paragraph.append(line);
            }
            i++;
        }
        flush(parent, paragraph, maxWidth);
    }

    private static void flush(LinearLayout parent, StringBuilder pending, int width) {
        if (pending.length() > 0) {
            text(parent, pending.toString(), width, 15, false, 5, 3, BODY);
            pending.setLength(0);
        }
    }

    private static TextView text(LinearLayout parent, String raw, int width, int sp,
                                 boolean bold, int top, int bottom, int color) {
        RichTextView view = new RichTextView(parent.getContext());
        view.setText(inline(raw), TextView.BufferType.SPANNABLE);
        view.setTextColor(color);
        view.setTextSize(sp);
        view.setLineSpacing(dp(parent.getContext(), 2), 1.10f);
        view.setIncludeFontPadding(true);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        view.setMaxWidth(width);
        view.setTextIsSelectable(true);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.topMargin = dp(parent.getContext(), top);
        lp.bottomMargin = dp(parent.getContext(), bottom);
        parent.addView(view, lp);
        return view;
    }

    private static void codeBlock(LinearLayout parent, String body, String language, int width) {
        Context context = parent.getContext();
        LinearLayout shell = new LinearLayout(context);
        shell.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(11, 18, 32));
        bg.setStroke(dp(context, 1), Color.rgb(51, 65, 85));
        bg.setCornerRadius(dp(context, 8));
        shell.setBackground(bg);
        if (!language.isEmpty()) {
            TextView title = new TextView(context);
            title.setText(language);
            title.setTextSize(11);
            title.setTextColor(MUTED);
            title.setPadding(dp(context, 12), dp(context, 8), dp(context, 12), 0);
            shell.addView(title);
        }
        HorizontalScrollView scroll = new HorizontalScrollView(context);
        RichTextView code = new RichTextView(context);
        code.setText(body);
        code.setTextIsSelectable(true);
        code.setTypeface(Typeface.MONOSPACE);
        code.setTextSize(13);
        code.setTextColor(BODY);
        code.setHorizontallyScrolling(true);
        code.setPadding(dp(context, 12), dp(context, 10), dp(context, 12), dp(context, 12));
        scroll.addView(code, new android.widget.FrameLayout.LayoutParams(-2, -2));
        shell.addView(scroll, new LinearLayout.LayoutParams(-2, -2));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.width = Math.min(width, context.getResources().getDisplayMetrics().widthPixels - dp(context, 72));
        lp.setMargins(0, dp(context, 6), 0, dp(context, 8));
        parent.addView(shell, lp);
    }

    private static void quote(LinearLayout parent, String body, int width) {
        Context context = parent.getContext();
        LinearLayout wrap = new LinearLayout(context);
        wrap.setOrientation(LinearLayout.HORIZONTAL);
        View line = new View(context);
        line.setBackgroundColor(Color.rgb(96, 165, 250));
        wrap.addView(line, new LinearLayout.LayoutParams(dp(context, 3), -1));
        LinearLayout content = new LinearLayout(context);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(context, 10), 0, 0, 0);
        text(content, body, Math.max(dp(context, 100), width - dp(context, 18)), 14, false, 3, 3, MUTED);
        wrap.addView(content, new LinearLayout.LayoutParams(-2, -2));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.setMargins(0, dp(context, 6), 0, dp(context, 6));
        parent.addView(wrap, lp);
    }

    private static void table(LinearLayout parent, ArrayList<String> rows, int width) {
        Context context = parent.getContext();
        HorizontalScrollView scroll = new HorizontalScrollView(context);
        scroll.setHorizontalScrollBarEnabled(true);
        TableLayout table = new TableLayout(context);
        table.setShrinkAllColumns(false);
        for (int rowIndex = 0; rowIndex < rows.size(); rowIndex++) {
            String raw = rows.get(rowIndex).trim();
            if (raw.startsWith("|")) raw = raw.substring(1);
            if (raw.endsWith("|")) raw = raw.substring(0, raw.length() - 1);
            String[] cells = raw.split("(?<!\\\\)\\|", -1);
            TableRow row = new TableRow(context);
            row.setBackgroundColor(rowIndex == 0 ? Color.rgb(51, 65, 85)
                    : (rowIndex % 2 == 0 ? Color.rgb(30, 41, 59) : Color.rgb(22, 32, 46)));
            for (String cell : cells) {
                RichTextView tv = new RichTextView(context);
                tv.setText(inline(cell.trim().replace("\\|", "|")), TextView.BufferType.SPANNABLE);
                tv.setTextIsSelectable(true);
                tv.setTextColor(BODY);
                tv.setTextSize(13);
                if (rowIndex == 0) tv.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
                tv.setMinWidth(dp(context, 64));
                tv.setMaxWidth(dp(context, 200));
                tv.setPadding(dp(context, 10), dp(context, 8), dp(context, 10), dp(context, 8));
                row.addView(tv);
            }
            table.addView(row);
        }
        scroll.addView(table);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(Math.min(width,
                context.getResources().getDisplayMetrics().widthPixels - dp(context, 72)), -2);
        lp.setMargins(0, dp(context, 6), 0, dp(context, 8));
        parent.addView(scroll, lp);
    }

    private static SpannableStringBuilder inline(String raw) {
        SpannableStringBuilder out = new SpannableStringBuilder();
        Matcher matcher = INLINE.matcher(raw);
        int last = 0;
        while (matcher.find()) {
            out.append(raw, last, matcher.start());
            int begin = out.length();
            String piece = null;
            for (int i = 1; i <= 8; i++) if (matcher.group(i) != null && i != 2) {
                piece = matcher.group(i); break;
            }
            if (piece == null) piece = matcher.group();
            out.append(piece);
            int end = out.length();
            if (matcher.group(1) != null) {
                String url = matcher.group(2);
                out.setSpan(new URLSpan(url), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                out.setSpan(new ForegroundColorSpan(LINK), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                out.setSpan(new UnderlineSpan(), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            } else if (matcher.group(3) != null) {
                out.setSpan(new TypefaceSpan("monospace"), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                out.setSpan(new BackgroundColorSpan(Color.rgb(51, 65, 85)), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            } else if (matcher.group(4) != null || matcher.group(5) != null) {
                out.setSpan(new StyleSpan(Typeface.BOLD), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            } else if (matcher.group(6) != null) {
                out.setSpan(new StrikethroughSpan(), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            } else {
                out.setSpan(new StyleSpan(Typeface.ITALIC), begin, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            }
            last = matcher.end();
        }
        out.append(raw, last, raw.length());
        Matcher urls = Patterns.WEB_URL.matcher(out);
        while (urls.find()) {
            int start = urls.start(), end = urls.end();
            while (end > start && ".,;:!?)]}".indexOf(out.charAt(end - 1)) >= 0) end--;
            if (end <= start || out.getSpans(start, end, URLSpan.class).length > 0
                    || out.getSpans(start, end, BackgroundColorSpan.class).length > 0) continue;
            String value = out.subSequence(start, end).toString();
            if (!value.startsWith("https://") && !value.startsWith("http://")) {
                if (value.contains("@") || !value.contains(".")) continue;
                value = "https://" + value;
            }
            out.setSpan(new URLSpan(value), start, end, Spannable.SPAN_EXCLUSIVE_EXCLUSIVE);
            out.setSpan(new ForegroundColorSpan(LINK), start, end, Spannable.SPAN_EXCLUSIVE_EXCLUSIVE);
            out.setSpan(new UnderlineSpan(), start, end, Spannable.SPAN_EXCLUSIVE_EXCLUSIVE);
        }
        return out;
    }

    private static int dp(Context context, int amount) {
        return Math.round(amount * context.getResources().getDisplayMetrics().density);
    }

    /**
     * setTextIsSelectable installs ArrowKeyMovementMethod; LinkMovementMethod would
     * replace it and break partial selection. Detect quick URL taps without
     * consuming long-press/drag events, which remain the platform selection UI.
     */
    private static final class RichTextView extends androidx.appcompat.widget.AppCompatTextView {
        private float downX, downY;
        private long downAt;
        private boolean dragged;

        RichTextView(Context context) { super(context); }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                downX = event.getX();
                downY = event.getY();
                downAt = SystemClock.uptimeMillis();
                dragged = false;
            } else if (event.getActionMasked() == MotionEvent.ACTION_MOVE) {
                int slop = ViewConfiguration.get(getContext()).getScaledTouchSlop();
                if (Math.abs(event.getX() - downX) > slop || Math.abs(event.getY() - downY) > slop) dragged = true;
            } else if (event.getActionMasked() == MotionEvent.ACTION_UP && !dragged
                    && SystemClock.uptimeMillis() - downAt < ViewConfiguration.getLongPressTimeout()) {
                URLSpan link = getLinkAt(event.getX(), event.getY());
                if (link != null) {
                    Uri uri = Uri.parse(link.getURL());
                    String scheme = uri.getScheme();
                    if ("https".equalsIgnoreCase(scheme) || "http".equalsIgnoreCase(scheme)) {
                        try {
                            Intent intent = new Intent(Intent.ACTION_VIEW, uri);
                            intent.addCategory(Intent.CATEGORY_BROWSABLE);
                            getContext().startActivity(intent);
                        } catch (ActivityNotFoundException error) {
                            Toast.makeText(getContext(), "没有可打开链接的应用", Toast.LENGTH_SHORT).show();
                        }
                        return true;
                    }
                }
            }
            return super.onTouchEvent(event);
        }

        private URLSpan getLinkAt(float rawX, float rawY) {
            if (!(getText() instanceof Spanned)) return null;
            Layout layout = getLayout();
            if (layout == null) return null;
            int x = (int)rawX - getTotalPaddingLeft() + getScrollX();
            int y = (int)rawY - getTotalPaddingTop() + getScrollY();
            if (y < 0 || y > layout.getHeight()) return null;
            int line = layout.getLineForVertical(y);
            if (x < layout.getLineLeft(line) || x > layout.getLineRight(line)) return null;
            int offset = layout.getOffsetForHorizontal(line, x);
            Spanned spanned = (Spanned)getText();
            URLSpan[] spans = spanned.getSpans(offset, offset, URLSpan.class);
            return spans.length == 0 ? null : spans[0];
        }
    }
}

package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.content.ContextWrapper;
import android.content.Intent;
import android.os.Bundle;
import android.os.SystemClock;
import android.text.Spanned;
import android.text.style.BackgroundColorSpan;
import android.text.style.StrikethroughSpan;
import android.text.style.StyleSpan;
import android.text.style.URLSpan;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.concurrent.atomic.AtomicReference;

/** Non-destructive on-device typography and link/selection regression fixture. */
public final class ChatRichTextVerification {
    private static void require(boolean ok, String label) {
        if (!ok) throw new AssertionError(label);
    }
    private static final class CaptureContext extends ContextWrapper {
        volatile Intent opened;
        CaptureContext(Context base) { super(base); }
        @Override public void startActivity(Intent intent) { opened = intent; }
    }
    private static void collect(View item, ArrayList<TextView> texts, ArrayList<HorizontalScrollView> scrolls) {
        if (item instanceof TextView) texts.add((TextView)item);
        if (item instanceof HorizontalScrollView) scrolls.add((HorizontalScrollView)item);
        if (item instanceof ViewGroup) {
            ViewGroup group = (ViewGroup)item;
            for (int i = 0; i < group.getChildCount(); i++) collect(group.getChildAt(i), texts, scrolls);
        }
    }
    static void run(Instrumentation test) {
        Bundle result = new Bundle();
        try {
            require(test.getTargetContext().getPackageName().endsWith(".dev"),
                    "Only run against isolated Dev app");
            AtomicReference<Throwable> failure = new AtomicReference<>();
            test.runOnMainSync(() -> {
                try {
                    CaptureContext ctx = new CaptureContext(test.getTargetContext());
                    LinearLayout root = new LinearLayout(ctx);
                    root.setOrientation(LinearLayout.VERTICAL);
                    String fixture = "# 标题\n\n**加粗**、*斜体*、~~删除~~、`代码`、[点击链接](https://example.com/test?q=1)\n\n" +
                            "- 列表项\n- [x] 完成项\n> 引用内容\n---\n```java\nint x = 3;\n```\n\n| 列A | 列B |\n| --- | --- |\n| 一 | 二 |";
                    ChatRichText.render(root, fixture, 650);
                    ArrayList<TextView> texts = new ArrayList<>();
                    ArrayList<HorizontalScrollView> scrolls = new ArrayList<>();
                    collect(root, texts, scrolls);
                    require(texts.size() >= 10, "Text blocks rendered: " + texts.size());
                    require(scrolls.size() >= 2, "Code and tables horizontally scroll");
                    int selectable = 0, styled = 0, linked = 0, coded = 0, struck = 0;
                    for (TextView tv : texts) {
                        if (tv.isTextSelectable()) selectable++;
                        if (!(tv.getText() instanceof Spanned)) continue;
                        Spanned spans = (Spanned)tv.getText();
                        if (spans.getSpans(0, spans.length(), StyleSpan.class).length > 0) styled++;
                        if (spans.getSpans(0, spans.length(), BackgroundColorSpan.class).length > 0) coded++;
                        if (spans.getSpans(0, spans.length(), StrikethroughSpan.class).length > 0) struck++;
                        linked += spans.getSpans(0, spans.length(), URLSpan.class).length;
                    }
                    require(selectable >= 7, "Selectable text nodes: " + selectable);
                    require(styled > 0 && coded > 0 && struck > 0, "Inline style spans");
                    require(linked == 1, "Markdown link parsed: " + linked);
                    // Capture and inspect navigation without launching a browser/network request.
                    LinearLayout probe = new LinearLayout(ctx);
                    ChatRichText.render(probe, "[打开](https://example.com/path)", 650);
                    TextView link = (TextView)probe.getChildAt(0);
                    require(link.isTextSelectable(), "Link label remains selectable");
                    int w = 650;
                    link.measure(View.MeasureSpec.makeMeasureSpec(w, View.MeasureSpec.AT_MOST),
                            View.MeasureSpec.makeMeasureSpec(150, View.MeasureSpec.AT_MOST));
                    link.layout(0, 0, link.getMeasuredWidth(), link.getMeasuredHeight());
                    long now = SystemClock.uptimeMillis();
                    MotionEvent down = MotionEvent.obtain(now, now, MotionEvent.ACTION_DOWN, 18, 22, 0);
                    MotionEvent up = MotionEvent.obtain(now, now+100, MotionEvent.ACTION_UP, 18, 22, 0);
                    link.dispatchTouchEvent(down); link.dispatchTouchEvent(up);
                    down.recycle(); up.recycle();
                    require(ctx.opened != null, "A short tap opens the link");
                    require(Intent.ACTION_VIEW.equals(ctx.opened.getAction()), "Link uses ACTION_VIEW");
                    require("https://example.com/path".equals(String.valueOf(ctx.opened.getData())), "Preserves URL");

                    // Verify embedded markdown image extraction and placeholder rendering
                    String imgFixture = "前言文本\n![测试图片](file:///C:/Users/Administrator/Desktop/sample.png)\n后记文本";
                    java.util.Set<String> extracted = ChatRichText.extractImageNames(imgFixture);
                    require(extracted.contains("sample.png"), "Extracted image filename contains sample.png");
                    LinearLayout imgContainer = new LinearLayout(ctx);
                    ChatRichText.render(imgContainer, imgFixture, 650);
                    require(imgContainer.getChildCount() == 3, "Image fixture rendered as 3 distinct views in container");

                    // Verify file markdown link parsing and bare file path URLSpan
                    LinearLayout fileBox = new LinearLayout(ctx);
                    ChatRichText.render(fileBox, "请查阅 [脚本代码](file:///f:/Desktop/main.py) 与裸路径 file:///C:/test.log", 650);
                    TextView fileTv = (TextView)fileBox.getChildAt(0);
                    Spanned fileSpanned = (Spanned)fileTv.getText();
                    URLSpan[] fileUrls = fileSpanned.getSpans(0, fileSpanned.length(), URLSpan.class);
                    require(fileUrls.length == 2, "Both file markdown link and bare file path parsed as URLSpan, found: " + fileUrls.length);
                    require("file:///f:/Desktop/main.py".equals(fileUrls[0].getURL()), "First link URL matches file:///f:/Desktop/main.py");
                    require("file:///C:/test.log".equals(fileUrls[1].getURL()), "Second link URL matches file:///C:/test.log");

                    result.putString("stream", "CHAT_RICH_TEXT=PASSED (markdown heading, bold, italic, strike, inline code, URLSpan, file links, lists, quotes, code/table scroll, selectable text, captured HTTPS tap, embedded markdown image)");
                } catch (Throwable error) { failure.set(error); }
            });
            if (failure.get() != null) throw failure.get();
            test.finish(Activity.RESULT_OK, result);
        } catch (Throwable e) {
            result.putString("stream", android.util.Log.getStackTraceString(e));
            test.finish(Activity.RESULT_CANCELED, result);
        }
    }
}

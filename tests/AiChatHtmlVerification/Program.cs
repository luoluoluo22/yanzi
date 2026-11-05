using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        try
        {
            var target = typeof(OpenQuickHost.MainWindow).Assembly
                .GetType("OpenQuickHost.AiChatHtmlContent", throwOnError: true)!;
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var detect = target.GetMethod("IsHtmlMessage", flags)!;
            var render = target.GetMethod("TryRender", flags)!;
            var payload = target.GetMethod("CreateClipboardPayload", flags)!;

            bool Detect(string input) => (bool)detect.Invoke(null, [input])!;
            bool Render(TextBlock text, string html) => (bool)render.Invoke(null, [text, html])!;
            (string Html, string Text) Payload(string html) =>
                ((string Html, string Text))payload.Invoke(null, [html])!;

            Assert("ordinary Markdown remains Markdown", !Detect("# Title\n**Bold**"));
            Assert("HTML inside fenced code remains literal",
                !Detect("\u0060\u0060\u0060html\n<p>HTML should be code</p>\n\u0060\u0060\u0060"));
            Assert("HTML fragments recognized", Detect("<div><h2>标题</h2><p>正文</p></div>"));
            Assert("complete HTML documents recognized",
                Detect("<!doctype html><html><body><h1>你好</h1></body></html>"));
            Assert("inline HTML recognized", Detect("文字 <strong>加粗</strong> 后续"));

            var mixedBlock = new TextBlock();
            Assert("mixed HTML and Markdown renders together",
                Render(mixedBlock, "介绍 <strong>HTML</strong> 与 **Markdown**") &&
                Flatten(mixedBlock.Inlines).Contains("HTML") &&
                Flatten(mixedBlock.Inlines).Contains("Markdown"));

            var textBlock = new TextBlock();
            var html = "<div><h2>标题</h2><p>欢迎 <strong>世界</strong></p>" +
                       "<script>alert('xss')</script><style>body{display:none}</style>" +
                       "<a href='javascript:alert(1)'>坏链接</a>" +
                       "<a href='https://example.com/'>安全链接</a></div>";
            Assert("safe HTML rendering activates", Render(textBlock, html));
            var runs = Flatten(textBlock.Inlines);
            Assert("text is rendered", runs.Contains("标题") && runs.Contains("世界"));
            Assert("script/style bodies are not displayed",
                !runs.Contains("alert") && !runs.Contains("display:none"));
            Assert("unsafe hyperlinks not interactive",
                CountLinks(textBlock.Inlines) == 1);

            var tableBlock = new TextBlock();
            Assert("tables rendered", Render(tableBlock,
                "<table><thead><tr><th>名称</th><th>值</th></tr></thead>" +
                "<tbody><tr><td>项目</td><td>42</td></tr></tbody></table>"));
            var table = tableBlock.Inlines.OfType<InlineUIContainer>()
                .Select(i => i.Child).OfType<Grid>().FirstOrDefault();
            Assert("table uses native WPF grid with cells",
                table is not null && table.RowDefinitions.Count == 2 &&
                table.ColumnDefinitions.Count == 2 && table.Children.Count == 4);
            var tableParent = new Border { Width = 480, Padding = new Thickness(12), Child = tableBlock };
            tableParent.Measure(new System.Windows.Size(480, 800));
            tableParent.Arrange(new Rect(0, 0, 480, 800));
            tableParent.UpdateLayout();
            Assert("table is visible within a normal chat bubble",
                table is not null && table.ActualWidth > 30 && table.ActualWidth <= 480);

            var (clipboardHtml, clipboardText) = Payload(
                "<!doctype html><html><head><script>evil()</script></head><body>" +
                "<h1 onclick='evil()'>中文标题</h1>" +
                "<p>内容 <a href='javascript:evil()'>坏</a>" +
                "<a href='https://example.com/?q=1&amp;a=2'>好</a></p>" +
                "<img src='https://tracker.invalid/pixel' alt='示意图'>" +
                "<iframe src='https://bad.invalid'></iframe></body></html>");
            Assert("HTML payload strips scripting and event handlers",
                !clipboardHtml.Contains("evil", StringComparison.OrdinalIgnoreCase) &&
                !clipboardHtml.Contains("onclick", StringComparison.OrdinalIgnoreCase) &&
                !clipboardHtml.Contains("iframe", StringComparison.OrdinalIgnoreCase));
            Assert("rich copy keeps heading and safe hyperlinks",
                clipboardHtml.Contains("<h1>中文标题</h1>") &&
                clipboardHtml.Contains("href=\"https://example.com/?q=1&amp;a=2\""));
            Assert("plain copy retains visible Chinese text",
                clipboardText.Contains("中文标题") && clipboardText.Contains("内容") &&
                clipboardText.Contains("示意图") && !clipboardText.Contains("evil"));

            static int Offset(string html, string name)
            {
                var m = Regex.Match(html, @"(?m)^" + Regex.Escape(name) + @":(\d{10})");
                if (!m.Success) throw new Exception("CF_HTML header missing " + name);
                return int.Parse(m.Groups[1].Value);
            }
            var bytes = Encoding.UTF8.GetBytes(clipboardHtml);
            var startHtml = Offset(clipboardHtml, "StartHTML");
            var endHtml = Offset(clipboardHtml, "EndHTML");
            var startFragment = Offset(clipboardHtml, "StartFragment");
            var endFragment = Offset(clipboardHtml, "EndFragment");
            Assert("CF_HTML UTF-8 byte offsets valid",
                startHtml > 0 && startHtml < startFragment &&
                startFragment < endFragment && endFragment < endHtml &&
                endHtml == bytes.Length &&
                Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment)
                    .Contains("<h1>中文标题</h1>"));

            Console.WriteLine($"AI_CHAT_HTML_QA_OK checks={_passed}");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("AI_CHAT_HTML_QA_FAILED " + e);
            return 1;
        }
    }

    private static string Flatten(InlineCollection items)
    {
        var sb = new StringBuilder();
        foreach (var inline in items)
        {
            switch (inline)
            {
                case Run run: sb.Append(run.Text); break;
                case Span span: sb.Append(Flatten(span.Inlines)); break;
            }
        }
        return sb.ToString();
    }

    private static int CountLinks(InlineCollection items)
    {
        var count = 0;
        foreach (var inline in items)
        {
            if (inline is Hyperlink) count++;
            if (inline is Span span) count += CountLinks(span.Inlines);
        }
        return count;
    }

    private static void Assert(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException(label);
        _passed++;
        Console.WriteLine("PASS " + label);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Xml.Linq;
using OpenQuickHost;

internal static class Program
{
    private static int _count;
    private const string Html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>Pelican on a Bicycle</title>
<style>
body { margin:0; background:#c2e7fc; font-family: sans-serif; }
.wheel { animation: spin 1.25s linear infinite; transform-origin: center; }
.pelican { animation: bob .8s ease-in-out infinite alternate; }
@keyframes spin { to { transform: rotate(360deg); } }
@keyframes bob { to { transform: translateY(-8px); } }
</style>
</head>
<body>
<main style="text-align:center">
<h1>鹈鹕骑自行车</h1>
<svg viewBox="0 0 600 340" xmlns="http://www.w3.org/2000/svg" style="width:min(90vw,600px)">
<circle class="wheel" cx="130" cy="260" r="64" fill="none" stroke="#182c44" stroke-width="13" stroke-dasharray="15 10"/>
<circle class="wheel" cx="460" cy="260" r="64" fill="none" stroke="#182c44" stroke-width="13" stroke-dasharray="15 10"/>
<path d="M130 260 L240 170 L340 260 L130 260 M240 170 L460 260" fill="none" stroke="#e05d45" stroke-width="13"/>
<g class="pelican"><ellipse cx="285" cy="130" rx="65" ry="45" fill="#fafaf4"/>
<path d="M320 120 L455 148 L320 155Z" fill="#f6b54a"/>
<circle cx="315" cy="116" r="6" fill="#14263d"/></g>
</svg>
<p>CSS animation and inline SVG, without JavaScript.</p>
</main>
</body>
</html>
""";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--preview"))
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new AiChatHtmlPreviewWindow(Html));
            return 0;
        }

        try
        {
            var wrapped = "下面是一个网页：\n\n<CodeBlock language=\"html\" editable>\n" +
                          Html + "\n</CodeBlock>\n";
            Check("nonstandard CodeBlock HTML extracted",
                AiChatHtmlPreviewContent.TryExtractHtml(wrapped, out var extracted));
            Check("DOCTYPE and CSS animation retained",
                extracted.StartsWith("<!DOCTYPE html>") && extracted.Contains("@keyframes spin") &&
                extracted.Contains("class=\"pelican\""));
            var normalized = AiChatHtmlPreviewContent.NormalizeCodeBlocks(wrapped);
            Check("CodeBlock wrapper replaced by Markdown fence",
                normalized.Contains("```html") && !normalized.Contains("<CodeBlock"));
            Check("HTML document remains literal inside code fence",
                normalized.Contains("<svg") && normalized.Contains("</html>"));
            var document = new MdXaml.Markdown().Transform(normalized);
            var normalText = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Check("nonstandard tag not printed in markdown document",
                !normalText.Contains("<CodeBlock"));
            Check("fenced HTML renders as code UI",
                document.Blocks.Cast<Block>().OfType<BlockUIContainer>().Any());

            var model = new AiChatMessage(false, wrapped);
            Check("assistant HTML preview affordance visible",
                model.HasHtmlPreview && model.HtmlPreviewVisibility == Visibility.Visible);
            Check("CodeBlock is not mistaken for standalone HTML",
                !model.IsStandaloneHtml && model.MarkdownVisibility == Visibility.Visible);
            Check("message renders normalized content", model.FormattedText.Contains("```html"));
            model.IsStreaming = true;
            Check("streaming does not initialize HTML preview",
                !model.HasHtmlPreview && model.FormattedText == string.Empty);
            model.IsStreaming = false;
            Check("preview enabled after streaming completes", model.HasHtmlPreview);
            Check("user message has no preview",
                !new AiChatMessage(true, wrapped).HasHtmlPreview);

            var fence = "网页\n```html\n" + Html + "\n```\n";
            Check("standard html fence extracted",
                AiChatHtmlPreviewContent.TryExtractHtml(fence, out var fromFence) &&
                fromFence.Contains("Pelican on a Bicycle"));
            Check("complete standalone HTML extracted",
                AiChatHtmlPreviewContent.TryExtractHtml(Html, out var raw) && raw == Html.Trim());
            Check("ordinary Markdown is not HTML preview",
                !AiChatHtmlPreviewContent.TryExtractHtml("# 标题\n*你好*\n", out _));
            Check("oversized payload rejected",
                !AiChatHtmlPreviewContent.TryExtractHtml(new string('a', 1_000_010), out _));

            var sandboxed = AiChatHtmlPreviewContent.BuildProtectedDocument(Html);
            Check("CSP inserted before user styles",
                sandboxed.IndexOf("Content-Security-Policy", StringComparison.Ordinal) <
                sandboxed.IndexOf("<style>", StringComparison.Ordinal));
            foreach(var term in new [] {
                "script-src 'none'", "connect-src 'none'", "frame-src 'none'",
                "object-src 'none'", "form-action 'none'", "default-src 'none'"
            }) Check("CSP "+term, sandboxed.Contains(term));
            Check("inline CSS animation permitted", sandboxed.Contains("style-src 'unsafe-inline'"));
            Check("SVG preserved for rendering", sandboxed.Contains("<svg") && sandboxed.Contains("<circle"));

            var root = FindRoot();
            var windowXaml = XDocument.Load(Path.Combine(root,"src","OpenQuickHost","MainWindow.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var previewButton = windowXaml.Descendants()
                .FirstOrDefault(e => e.Attribute(x+"Name")?.Value == "AssistantHtmlPreviewButton");
            Check("preview control bound and wired",
                previewButton?.Attribute("Visibility")?.Value == "{Binding HtmlPreviewVisibility}" &&
                previewButton.Attribute("Click")?.Value == "AiChatPreviewHtmlButton_Click");
            var windowCode = File.ReadAllText(Path.Combine(root,"src","OpenQuickHost","AiChatHtmlPreviewWindow.cs"));
            Check("isolated WebView2 profile",
                windowCode.Contains("AiChatHtmlPreviewWebView2") && windowCode.Contains("EnsureCoreWebView2Async"));
            Check("script disabled even if CSP is ignored",
                windowCode.Contains("IsScriptEnabled = false"));
            Check("network and redirects explicitly blocked",
                windowCode.Contains("WebResourceRequested") && windowCode.Contains("args.Cancel = true"));
            Check("host objects and messages denied",
                windowCode.Contains("AreHostObjectsAllowed = false") &&
                windowCode.Contains("IsWebMessageEnabled = false"));
            Console.WriteLine("AI_CHAT_HTML_PREVIEW_QA_OK checks=" + _count);
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("AI_CHAT_HTML_PREVIEW_QA_FAILED "+ex);
            return 1;
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while(current != null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repo root absent");
    }

    private static void Check(string name, bool pass)
    {
        if (!pass) throw new InvalidOperationException(name);
        Console.WriteLine("PASS "+name);
        _count++;
    }
}

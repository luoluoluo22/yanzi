using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

/// <summary>
/// Renders model-supplied HTML as inert WPF elements. No XAML evaluation,
/// browser scripting, remote image requests or arbitrary URI handlers.
/// </summary>
internal static class AiChatHtmlContent
{
    private static readonly Regex HtmlTag = new(
        @"<\s*(?:!doctype\s+html|html|body|article|section|div|p|br|hr|h[1-6]|table|thead|tbody|tr|th|td|ul|ol|li|blockquote|pre|code|strong|em|b|i|u|del|s|a|span|img)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(150));

    private static readonly Regex FencedCode = new(
        @"(?ms)^\s*(?:\x60{3,}|~{3,})[^\r\n]*\r?\n.*?^\s*(?:\x60{3,}|~{3,})\s*$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(150));

    private static readonly HashSet<string> SkippedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "iframe", "object", "embed", "svg", "math",
        "form", "input", "button", "textarea", "select", "meta", "link",
        "noscript", "template", "video", "audio", "canvas"
    };

    private static readonly HashSet<string> SupportedHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "article", "section", "p", "br", "hr", "h1", "h2", "h3", "h4",
        "h5", "h6", "table", "thead", "tbody", "tfoot", "tr", "th", "td",
        "ul", "ol", "li", "blockquote", "pre", "code", "strong", "b", "em",
        "i", "u", "del", "s", "a", "span"
    };

    public static bool IsHtmlMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 250_000)
            return false;

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("\x60\x60\x60", StringComparison.Ordinal) ||
            trimmed.StartsWith("~~~", StringComparison.Ordinal))
            return false;

        try { return HtmlTag.IsMatch(FencedCode.Replace(text, string.Empty)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static HtmlNode ParseBody(string text)
    {
        var document = new HtmlAgilityPack.HtmlDocument { OptionFixNestedTags = true };
        document.LoadHtml(text);
        return document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
    }

    public static bool TryRender(TextBlock target, string text)
    {
        if (!IsHtmlMessage(text))
            return false;

        try
        {
            var body = ParseBody(text);
            var budget = 10_000;
            RenderNodes(target, target.Inlines, body.ChildNodes, ref budget, 0);
            return true;
        }
        catch (Exception)
        {
            target.Inlines.Clear();
            return false; // Fall back to the existing Markdown renderer.
        }
    }

    private static void RenderNodes(TextBlock host, InlineCollection output,
        IEnumerable<HtmlNode> nodes, ref int budget, int depth)
    {
        if (depth > 48 || budget <= 0) return;
        foreach (var node in nodes)
        {
            if (--budget <= 0) return;
            if (node.NodeType == HtmlNodeType.Comment) continue;
            if (node.NodeType == HtmlNodeType.Text)
            {
                var value = WebUtility.HtmlDecode(node.InnerText);
                value = Regex.Replace(value, @"\s+", " ");
                if (!string.IsNullOrEmpty(value)) MarkdownTextBehavior.ParseAndAddInlineSegment(output, value);
                continue;
            }
            if (node.NodeType != HtmlNodeType.Element || SkippedTags.Contains(node.Name)) continue;

            var tag = node.Name.ToLowerInvariant();
            if (tag == "br") { output.Add(new LineBreak()); continue; }
            if (tag == "hr")
            {
                NewLine(output);
                output.Add(new Run("────────────────────"));
                NewLine(output);
                continue;
            }
            if (tag == "img")
            {
                var alt = node.GetAttributeValue("alt", string.Empty);
                var src = SafeExternalUri(node.GetAttributeValue("src", string.Empty));
                var description = string.IsNullOrWhiteSpace(alt) ? "图片" : alt;
                output.Add(src is null
                    ? new Run("[图片: " + description + "]")
                    : MakeLink("[图片: " + description + "]", src));
                continue;
            }
            if (tag == "table")
            {
                NewLine(output);
                RenderTable(host, output, node);
                NewLine(output);
                continue;
            }
            if (tag is "ul" or "ol")
            {
                NewLine(output);
                var index = 1;
                foreach (var child in node.ChildNodes.Where(n => n.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
                {
                    output.Add(new Run(tag == "ol" ? $"{index++}. " : "• "));
                    RenderNodes(host, output, child.ChildNodes, ref budget, depth + 1);
                    NewLine(output);
                }
                continue;
            }
            var isBlock = tag is "div" or "article" or "section" or "p" or "blockquote" or
                "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
            if (isBlock) NewLine(output);

            Span span = new();
            if (tag is "strong" or "b" || tag.StartsWith("h", StringComparison.Ordinal) && tag.Length == 2)
                span.FontWeight = FontWeights.Bold;
            if (tag is "em" or "i" or "blockquote") span.FontStyle = FontStyles.Italic;
            if (tag is "u") span.TextDecorations = TextDecorations.Underline;
            if (tag is "s" or "del") span.TextDecorations = TextDecorations.Strikethrough;
            if (tag is "code" or "pre") span.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1]))
                span.FontSize = tag[1] switch { '1' => 22, '2' => 19, '3' => 17, _ => 15 };

            if (tag == "pre")
            {
                span.Inlines.Add(new Run(WebUtility.HtmlDecode(node.InnerText)));
            }
            else
            {
                RenderNodes(host, span.Inlines, node.ChildNodes, ref budget, depth + 1);
            }

            if (tag == "a")
            {
                var url = SafeExternalUri(node.GetAttributeValue("href", string.Empty));
                if (url is not null)
                {
                    var link = MakeLink(GetPlainText(node).Trim(), url);
                    output.Add(link);
                }
                else output.Add(span);
            }
            else output.Add(span);
            if (isBlock) NewLine(output);
        }
    }

    private static void NewLine(InlineCollection inlines)
    {
        if (inlines.LastInline is not null && inlines.LastInline is not LineBreak)
            inlines.Add(new LineBreak());
    }

    private static Uri? SafeExternalUri(string? address)
    {
        if (!Uri.TryCreate(WebUtility.HtmlDecode(address), UriKind.Absolute, out var uri))
            return null;
        return uri.Scheme is "https" or "http" ? uri : null;
    }

    private static Hyperlink MakeLink(string title, Uri uri)
    {
        var link = new Hyperlink(new Run(string.IsNullOrWhiteSpace(title) ? uri.AbsoluteUri : title))
        {
            NavigateUri = uri,
            Cursor = System.Windows.Input.Cursors.Hand,
            TextDecorations = TextDecorations.Underline
        };
        link.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 165, 250));
        link.RequestNavigate += (_, args) =>
        {
            try
            {
                if (SafeExternalUri(args.Uri.AbsoluteUri) is not null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.AbsoluteUri)
                    { UseShellExecute = true });
            }
            catch { /* Invalid links must not crash chat. */ }
            args.Handled = true;
        };
        return link;
    }

    private static void RenderTable(TextBlock host, InlineCollection output, HtmlNode tableNode)
    {
        var rows = tableNode.SelectNodes(".//tr")?.Take(80).ToList();
        if (rows is null || rows.Count == 0) return;
        var columnCount = Math.Min(8, rows.Max(r => r.ChildNodes.Count(n =>
            n.Name.Equals("th", StringComparison.OrdinalIgnoreCase) ||
            n.Name.Equals("td", StringComparison.OrdinalIgnoreCase))));
        if (columnCount == 0) return;

        var grid = new Grid { MaxWidth = 720 };
        grid.SetBinding(FrameworkElement.MaxWidthProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth))
        { Source = host, Mode = BindingMode.OneWay });
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = rows[row].ChildNodes.Where(n => n.Name is "td" or "th").Take(columnCount).ToArray();
            for (var col = 0; col < cells.Length; col++)
            {
                var cell = cells[col];
                var label = new TextBlock
                {
                    Text = GetPlainText(cell).Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(6, 5, 6, 5),
                    FontSize = 13,
                    FontWeight = cell.Name == "th" ? FontWeights.SemiBold : FontWeights.Normal
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
                var border = new Border { BorderThickness = new Thickness(0.5), Child = label };
                border.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
                if (cell.Name == "th")
                    border.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                grid.Children.Add(border);
            }
        }
        output.Add(new InlineUIContainer(grid) { BaselineAlignment = BaselineAlignment.Center });
    }

    private static string GetPlainText(HtmlNode node)
    {
        var buffer = new StringBuilder();
        WritePlainText(node, buffer);
        return Regex.Replace(buffer.ToString(), @"[ \t]+\n", "\n").Trim();
    }

    private static void WritePlainText(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Comment || SkippedTags.Contains(node.Name)) return;
        if (node.NodeType == HtmlNodeType.Text)
        {
            output.Append(WebUtility.HtmlDecode(node.InnerText));
            return;
        }
        var tag = node.Name.ToLowerInvariant();
        if (tag == "br") { output.AppendLine(); return; }
        if (tag == "img")
        {
            output.Append("[图片: ").Append(node.GetAttributeValue("alt", "图片")).Append(']');
            return;
        }
        if (tag == "li") output.Append("• ");
        if (tag is "td" or "th") output.Append('\t');
        foreach (var child in node.ChildNodes) WritePlainText(child, output);
        if (tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
            "li" or "ul" or "ol" or "tr" or "table" or "blockquote" or "br" or "section")
            output.AppendLine();
    }

    private static void WriteSafeHtml(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Comment || SkippedTags.Contains(node.Name)) return;
        if (node.NodeType == HtmlNodeType.Text)
        {
            output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(node.InnerText)));
            return;
        }
        if (node.NodeType != HtmlNodeType.Element)
        {
            foreach (var child in node.ChildNodes) WriteSafeHtml(child, output);
            return;
        }
        var tag = node.Name.ToLowerInvariant();
        if (!SupportedHtmlTags.Contains(tag))
        {
            if (tag == "img")
                output.Append("[图片: ").Append(WebUtility.HtmlEncode(node.GetAttributeValue("alt", "图片"))).Append(']');
            else
                foreach (var child in node.ChildNodes) WriteSafeHtml(child, output);
            return;
        }
        output.Append('<').Append(tag);
        if (tag == "a" && SafeExternalUri(node.GetAttributeValue("href", string.Empty)) is { } uri)
            output.Append(" href=\"").Append(WebUtility.HtmlEncode(uri.AbsoluteUri)).Append('"');
        output.Append('>');
        foreach (var child in node.ChildNodes) WriteSafeHtml(child, output);
        if (tag is not ("br" or "hr")) output.Append("</").Append(tag).Append('>');
    }

    /// <summary>Provides both CF_HTML and UnicodeText to Word, browsers and plain-text editors.</summary>
    internal static (string Html, string PlainText) CreateClipboardPayload(string source)
    {
        var body = ParseBody(source);
        var safeHtml = new StringBuilder();
        foreach (var node in body.ChildNodes) WriteSafeHtml(node, safeHtml);
        var plain = GetPlainText(body);
        const string start = "<html><body><!--StartFragment-->";
        const string end = "<!--EndFragment--></body></html>";
        const string headerFormat = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var emptyHeader = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            headerFormat, 0, 0, 0, 0);
        var html = start + safeHtml + end;
        var startHtml = Encoding.UTF8.GetByteCount(emptyHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(start);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(safeHtml.ToString());
        var endHtml = startHtml + Encoding.UTF8.GetByteCount(html);
        var header = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            headerFormat, startHtml, endHtml, startFragment, endFragment);
        return (header + html, plain);
    }

    public static void CopyRich(string source)
    {
        var (html, plain) = CreateClipboardPayload(source);
        var data = new Forms.DataObject();
        data.SetData(Forms.DataFormats.Html, html);
        data.SetData(Forms.DataFormats.UnicodeText, plain);
        data.SetData(Forms.DataFormats.Text, plain);
        ClipboardService.SetDataObject(data, true);
    }
}

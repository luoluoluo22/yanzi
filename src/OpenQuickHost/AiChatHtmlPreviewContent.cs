using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Converts model-specific CodeBlock wrappers into Markdown fences and extracts
/// a self-contained HTML page for an explicitly requested, isolated preview.
/// </summary>
public static class AiChatHtmlPreviewContent
{
    private const int MaxPreviewCharacters = 300_000;
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(300);

    private static readonly Regex CodeBlock = new(
        @"<CodeBlock\b(?<attributes>[^>]{0,1000})>(?<content>[\s\S]*?)</CodeBlock\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex HtmlLanguage = new(
        @"\b(?:language|lang)\s*=\s*(?:""(?:html|svg)""|'(?:html|svg)'|(?:html|svg)(?=\s|$))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex HtmlFence = new(
        @"(?m)^[ \t]*(?<marker>\x60{3,}|~{3,})[ \t]*(?:html|svg)[ \t]*\r?\n(?<content>[\s\S]*?)^[ \t]*\k<marker>[ \t]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex HtmlRoot = new(
        @"(?i)<\s*(?:!doctype\s+html|html\b|svg\b|body\b|div\b|section\b|p\b|h[1-6]\b|table\b|style\b)",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex HeadStart = new(
        @"<head\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    public static string NormalizeCodeBlocks(string? content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > 1_000_000 ||
            !content.Contains("<CodeBlock", StringComparison.OrdinalIgnoreCase))
            return content ?? string.Empty;

        try
        {
            return CodeBlock.Replace(content, match =>
            {
                var language = HtmlLanguage.IsMatch(match.Groups["attributes"].Value) ? "html" : "text";
                var code = match.Groups["content"].Value.Trim('\r', '\n');
                // Fences must be longer than any consecutive backticks in the code.
                var longest = Regex.Matches(code, @"\x60+", RegexOptions.None, RegexBudget)
                    .Cast<Match>().Select(m => m.Length).DefaultIfEmpty(2).Max();
                var fence = new string('`', Math.Max(3, longest + 1));
                return "\n\n" + fence + language + "\n" + code + "\n" + fence + "\n\n";
            });
        }
        catch (RegexMatchTimeoutException) { return content; }
    }

    public static bool IsStandaloneHtml(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<CodeBlock", StringComparison.OrdinalIgnoreCase)) return false;
        return trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<div", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<section", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<p", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<h1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("<table", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryExtractHtml(string? content, out string html)
    {
        html = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1_000_000) return false;

        try
        {
            foreach (Match match in CodeBlock.Matches(content))
            {
                if (HtmlLanguage.IsMatch(match.Groups["attributes"].Value) &&
                    Accept(match.Groups["content"].Value, out html))
                    return true;
            }

            foreach (Match match in HtmlFence.Matches(content))
            {
                if (Accept(match.Groups["content"].Value, out html))
                    return true;
            }

            if (IsStandaloneHtml(content) && Accept(content, out html))
                return true;

            // Models sometimes introduce a complete HTML page without a code fence.
            var start = content.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = content.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var end = content.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
                if (end > start)
                    return Accept(content.Substring(start, end + "</html>".Length - start), out html);
            }
        }
        catch (RegexMatchTimeoutException) { }
        html = string.Empty;
        return false;
    }

    private static bool Accept(string candidate, out string html)
    {
        html = candidate.Trim();
        if (html.Length < 14 || html.Length > MaxPreviewCharacters || !HtmlRoot.IsMatch(html))
        {
            html = string.Empty;
            return false;
        }
        return true;
    }

    // Inline SVG and CSS animations work, but page scripts, network resources,
    // frames, forms, navigation and host-object interaction do not.
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; " +
        "img-src data:; font-src data:; connect-src 'none'; frame-src 'none'; " +
        "object-src 'none'; media-src 'none'; form-action 'none'; base-uri 'none'";

    public static string BuildProtectedDocument(string html)
    {
        if (html.Length > MaxPreviewCharacters)
            throw new ArgumentException("HTML preview is too large", nameof(html));

        var meta = "<meta http-equiv=\"Content-Security-Policy\" content=\"" +
                   ContentSecurityPolicy + "\">";
        var head = HeadStart.Match(html);
        if (head.Success)
            return html.Insert(head.Index + head.Length, meta);

        // CSP parsed before any model-supplied element or resource.
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
               meta + "</head><body>" + html + "</body></html>";
    }
}

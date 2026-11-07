using System.Text;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// ChatGPT web replies include a private widget syntax. It is not Markdown, so
/// the message card otherwise shows the tags themselves. Layout tags are
/// unwrapped, decorative tags are removed, and suggested-command directives
/// become a plain list. Command templates such as {#each} and pressable are
/// removed after their labels are kept. Ordinary fenced code is left unchanged.
/// </summary>
public static class AiChatGptMarkup
{
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(300);

    private static readonly Regex Fence = new(
        @"(?ms)^[ \t]*(`{3,}|~{3,})[^\n]*\r?\n.*?\r?\n[ \t]*\1[ \t]*\r?$",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex BodyDirective = new(
        @"\{@body\s+const\s+\w+\s*=\s*\[(?<items>[\s\S]*?)\]\s*\}",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex Quoted = new(
        """
        "(?<value>(?:\\.|[^"\\])*)"
        """,
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex DecorativeTag = new(
        @"<(?:icon|cite|citation|image|img|badge|spacer|divider)\b[^<>]*\/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex LayoutTag = new(
        @"</?(?:box|row|col|card|stack|caption|title|text|button|chip|pill|section|grid|center|header|footer|pressable)\b[^<>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex BrokenBoxLine = new(
        @"(?m)^[ \t]*box\s+border\b[^\n]*\r?\n?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex TemplateDirective = new(
        @"\{(?:#each\b[^{}]*|#if\b[^{}]*|#else(?:if)?\b[^{}]*|/each|/if)\}",
        RegexOptions.Compiled, RegexBudget);

    // Angle brackets are sometimes already gone, leaving the component source as text.
    private static readonly Regex PressableBlock = new(
        @"(?m)^[ \t]*(?:<)?pressable\b[\s\S]*?^[ \t]*(?:</pressable>|/pressable)[ \t]*\r?\n?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex LoneInterpolation = new(
        @"(?m)^[ \t]*\{[A-Za-z_]\w*\}[ \t]*\r?\n?",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex AttributeLine = new(
        @"^(?:background|padding|radius|gap|border|align|onClick|color|size|variant|justify|direction|wrap|margin|width|height|fontSize|fontWeight|opacity|elevation|theme|style|className|class)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexBudget);

    private static readonly Regex TrailingSpace = new(
        @"(?m)[ \t]+(?=\r?$)",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex ExtraSpaces = new(
        @"[ \t]{2,}",
        RegexOptions.Compiled, RegexBudget);

    private static readonly Regex ExtraBlankLines = new(
        @"(\r?\n){3,}",
        RegexOptions.Compiled, RegexBudget);

    public static string Normalize(string? content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > 1_000_000 || !LooksLikeMarkup(content))
            return content ?? string.Empty;

        try
        {
            var builder = new StringBuilder(content.Length);
            var index = 0;
            foreach (Match fence in Fence.Matches(content))
            {
                builder.Append(NormalizeSegment(content.Substring(index, fence.Index - index)));
                builder.Append(NormalizeFence(fence.Value));
                index = fence.Index + fence.Length;
            }

            builder.Append(NormalizeSegment(content[index..]));
            return ExtraBlankLines.Replace(builder.ToString(), "\n\n").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return content;
        }
    }

    private static bool LooksLikeMarkup(string content) =>
        content.Contains("<icon", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("<cite", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("<box", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("<row", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("<col", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("<card", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("{@body", StringComparison.Ordinal) ||
        content.Contains("{#each", StringComparison.Ordinal) ||
        content.Contains("{/each", StringComparison.Ordinal) ||
        content.Contains("{#if", StringComparison.Ordinal) ||
        content.Contains("<pressable", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("/pressable", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("pressable onClick", StringComparison.OrdinalIgnoreCase) ||
        content.Contains("box border", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSegment(string segment)
    {
        if (!LooksLikeMarkup(segment))
            return segment;

        var text = BodyDirective.Replace(segment, match =>
        {
            var items = Quoted.Matches(match.Groups["items"].Value)
                .Select(item => item.Groups["value"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\").Trim())
                .Where(item => item.Length > 0)
                .ToArray();
            if (items.Length == 0)
                return string.Empty;
            return "\n\n" + string.Join("\n", items.Select(item => "- " + item)) + "\n\n";
        });
        text = PressableBlock.Replace(text, ReplacePressable);
        text = DecorativeTag.Replace(text, string.Empty);
        text = LayoutTag.Replace(text, string.Empty);
        text = BrokenBoxLine.Replace(text, string.Empty);
        text = TemplateDirective.Replace(text, string.Empty);
        text = LoneInterpolation.Replace(text, string.Empty);
        text = ExtraSpaces.Replace(text, " ");
        text = TrailingSpace.Replace(text, string.Empty);
        text = ExtraBlankLines.Replace(text, "\n\n");
        return text;
    }

    private static string NormalizeFence(string fence)
    {
        var firstBreak = fence.IndexOf('\n');
        var lastBreak = fence.LastIndexOf('\n');
        if (firstBreak < 0 || lastBreak <= firstBreak)
            return fence;

        var lines = fence[(firstBreak + 1)..lastBreak]
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0 || !lines.Any(IsStructuralWidgetLine) ||
            !lines.All(line => IsStructuralWidgetLine(line) || IsAttributeLine(line) || IsPlainLabel(line)))
            return fence;

        var kept = lines.Where(line => IsPlainLabel(line) && !IsStructuralWidgetLine(line) && !IsAttributeLine(line)).ToArray();
        return kept.Length == 0 ? "\n" : string.Join("\n", kept) + "\n";
    }

    private static string ReplacePressable(Match match)
    {
        var kept = match.Value.Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0 && !IsStructuralWidgetLine(line) && !IsAttributeLine(line))
            .ToArray();
        return kept.Length == 0 ? string.Empty : string.Join("\n", kept) + "\n";
    }

    private static bool IsStructuralWidgetLine(string line) =>
        line.Equals("/pressable", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("</pressable>", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("pressable", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("<pressable", StringComparison.OrdinalIgnoreCase) ||
        LoneInterpolation.IsMatch(line) ||
        (TemplateDirective.IsMatch(line) && TemplateDirective.Match(line).Length == line.Length);

    private static bool IsAttributeLine(string line) => AttributeLine.IsMatch(line);

    private static bool IsPlainLabel(string line) =>
        line.Length > 0 && line.IndexOfAny(['<', '>', '{', '}', ';', '=']) < 0;
}

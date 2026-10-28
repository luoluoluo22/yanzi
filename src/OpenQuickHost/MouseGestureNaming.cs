using System.Text;
using Point = System.Windows.Point;

namespace OpenQuickHost;

public static class MouseGestureNaming
{
    public static string GetDisplayName(string? sequence, IReadOnlyList<Point>? path = null)
    {
        var normalized = NormalizeSequence(sequence);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builtInName = MouseGestureTemplateRecognizer.RecognizeBuiltInSign(path);
        if (!string.IsNullOrWhiteSpace(builtInName))
        {
            return TranslateSignToFriendlyName(builtInName);
        }

        var templateName = TryGetTemplateName(normalized, path);
        return templateName != null ? TranslateSignToFriendlyName(templateName) : BuildDirectionalName(normalized);
    }

    public static string TranslateSignToFriendlyName(string? sign)
    {
        if (string.IsNullOrWhiteSpace(sign)) return string.Empty;
        return sign.Trim().ToUpperInvariant() switch
        {
            "CHECKMARK" => "✔ 打勾",
            "HEART" => "♥ 心形",
            "CIRCLE" or "LOOP" => "⭕ 画圆",
            "ALPHA" => "α 鱼形/Alpha",
            "TRIANGLE" => "▲ 三角形",
            "RECTANGLE" => "■ 矩形",
            "V" => "V 字形",
            "INVERTED-V" => "倒 V 字形",
            "U" => "U 字形",
            "C" => "C 字形",
            "Z" => "Z 字形",
            "S" => "S 字形",
            "N" => "N 字形",
            "M" => "M 字形",
            "W" => "W 字形",
            "L" => "L 字形",
            "P" => "P 字形",
            "P-REV" => "反向 P",
            _ => sign
        };
    }

    public static string NormalizeSequence(string? sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(sequence.Length);
        foreach (var ch in sequence)
        {
            if (IsGestureArrow(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string? TryGetTemplateName(string sequence, IReadOnlyList<Point>? path)
    {
        return sequence switch
        {
            "↘↗" or "↙↖" => "V",
            "↗↘" or "↖↙" => "INVERTED-V",
            "↓→↑" or "↓←↑" or "↑→↓" or "↑←↓" => "U",
            "→↓←" or "←↓→" or "→↑←" or "←↑→" => "C",
            "→↙→" or "←↘←" => "Z",
            "↓↗↓" or "↑↘↑" => "N",
            "↑→↓←" or "↓→↑←" => "P",
            "↑←↓→" or "↓←↑→" => "P-REV",
            "→↓←↑" or "←↑→↓" => "S",
            _ => TryGetTemplateNameFromPath(sequence, path)
        };
    }

    private static string? TryGetTemplateNameFromPath(string sequence, IReadOnlyList<Point>? path)
    {
        if (path == null || path.Count < 5)
        {
            return null;
        }

        var first = path[0];
        var last = path[^1];
        var minX = path.Min(static p => p.X);
        var maxX = path.Max(static p => p.X);
        var minY = path.Min(static p => p.Y);
        var maxY = path.Max(static p => p.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var closingDistance = (last - first).Length;

        if (closingDistance <= Math.Min(width, height) * 0.35 && sequence.Length >= 4)
        {
            return "CIRCLE";
        }

        var verticalDominant = height > width * 1.15;
        if (verticalDominant &&
            (sequence.Contains("→↓←", StringComparison.Ordinal) ||
             sequence.Contains("→↑←", StringComparison.Ordinal)))
        {
            return "P";
        }

        return null;
    }

    private static string BuildDirectionalName(string sequence)
    {
        return string.Join(" ", sequence.Select(GetDirectionName));
    }

    private static string GetDirectionName(char direction)
    {
        return direction switch
        {
            '↑' => "上",
            '↗' => "右上",
            '→' => "右",
            '↘' => "右下",
            '↓' => "下",
            '↙' => "左下",
            '←' => "左",
            '↖' => "左上",
            _ => direction.ToString()
        };
    }

    private static bool IsGestureArrow(char ch)
    {
        return ch is '↑' or '↗' or '→' or '↘' or '↓' or '↙' or '←' or '↖';
    }
}

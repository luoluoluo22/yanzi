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
            return builtInName;
        }

        var templateName = TryGetTemplateName(normalized, path);
        return templateName ?? BuildDirectionalName(normalized);
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
            return "LOOP";
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
        return string.Join("-", sequence.Select(GetDirectionName));
    }

    private static string GetDirectionName(char direction)
    {
        return direction switch
        {
            '↑' => "UP",
            '↗' => "UP-RIGHT",
            '→' => "RIGHT",
            '↘' => "DOWN-RIGHT",
            '↓' => "DOWN",
            '↙' => "DOWN-LEFT",
            '←' => "LEFT",
            '↖' => "UP-LEFT",
            _ => direction.ToString()
        };
    }

    private static bool IsGestureArrow(char ch)
    {
        return ch is '↑' or '↗' or '→' or '↘' or '↓' or '↙' or '←' or '↖';
    }
}

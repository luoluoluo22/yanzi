using Point = System.Windows.Point;

namespace OpenQuickHost;

public static class MouseGestureTemplateRecognizer
{
    public const int PointCount = 64;
    public const int CoordinateScale = 1000;

    private const double DefaultMaxTemplateDistance = 180;
    private static readonly BuiltInTemplate[] BuiltInTemplates = CreateBuiltInTemplates();

    public static int[]? CreateTemplateData(IReadOnlyList<Point> path)
    {
        var normalized = Normalize(path);
        if (normalized.Count != PointCount)
        {
            return null;
        }

        var data = new int[PointCount * 2];
        for (var index = 0; index < normalized.Count; index++)
        {
            data[index * 2] = (int)Math.Round(normalized[index].X);
            data[(index * 2) + 1] = (int)Math.Round(normalized[index].Y);
        }

        return data;
    }

    public static bool HasTemplateData(int[]? data)
    {
        return data is { Length: PointCount * 2 };
    }

    public static TemplateMatch? FindBestMatch(IReadOnlyList<Point> candidatePath, IEnumerable<RegisteredGesture> templates)
    {
        var candidate = Normalize(candidatePath);
        if (candidate.Count != PointCount)
        {
            return null;
        }

        TemplateMatch? best = null;
        foreach (var template in templates)
        {
            if (!HasTemplateData(template.Data))
            {
                continue;
            }

            var distance = Distance(candidate, DecodeTemplateData(template.Data!));
            var maxDistance = ResolveMaxDistance(template.Tolerance);
            if (distance > maxDistance)
            {
                continue;
            }

            if (best == null || distance < best.Distance)
            {
                best = new TemplateMatch(template, distance);
            }
        }

        return best;
    }

    public static string? RecognizeBuiltInSign(IReadOnlyList<Point>? path)
    {
        if (path == null || path.Count < 2)
        {
            return null;
        }

        var candidate = Normalize(path);
        if (candidate.Count != PointCount)
        {
            return null;
        }

        BuiltInTemplate? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var template in BuiltInTemplates)
        {
            var distance = Distance(candidate, template.Points);
            if (distance < bestDistance)
            {
                best = template;
                bestDistance = distance;
            }
        }

        return best != null && bestDistance <= 230 ? best.Sign : null;
    }

    private static IReadOnlyList<Point> Normalize(IReadOnlyList<Point> rawPath)
    {
        var points = rawPath
            .Where(static point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
            .ToList();
        if (points.Count < 2 || PathLength(points) < 1)
        {
            return [];
        }

        var resampled = Resample(points, PointCount);
        if (resampled.Count != PointCount)
        {
            return [];
        }

        return ScaleToBox(resampled, CoordinateScale);
    }

    private static List<Point> Resample(IReadOnlyList<Point> source, int targetCount)
    {
        var points = source.ToList();
        var interval = PathLength(points) / (targetCount - 1);
        if (interval <= 0)
        {
            return [];
        }

        var resampled = new List<Point>(targetCount) { points[0] };
        var distanceSinceLast = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var segmentLength = Distance(previous, current);
            if (segmentLength <= 0)
            {
                continue;
            }

            while (distanceSinceLast + segmentLength >= interval)
            {
                var ratio = (interval - distanceSinceLast) / segmentLength;
                var inserted = new Point(
                    previous.X + (ratio * (current.X - previous.X)),
                    previous.Y + (ratio * (current.Y - previous.Y)));
                resampled.Add(inserted);
                previous = inserted;
                segmentLength = Distance(previous, current);
                distanceSinceLast = 0;

                if (resampled.Count == targetCount)
                {
                    return resampled;
                }
            }

            distanceSinceLast += segmentLength;
        }

        while (resampled.Count < targetCount)
        {
            resampled.Add(points[^1]);
        }

        return resampled;
    }

    private static IReadOnlyList<Point> ScaleToBox(IReadOnlyList<Point> points, double size)
    {
        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        var scale = Math.Max(width, height);
        if (scale <= 0)
        {
            return [];
        }

        var offsetX = (size - (width * size / scale)) / 2;
        var offsetY = (size - (height * size / scale)) / 2;
        return points
            .Select(point => new Point(
                ((point.X - minX) * size / scale) + offsetX,
                ((point.Y - minY) * size / scale) + offsetY))
            .ToList();
    }

    private static IReadOnlyList<Point> DecodeTemplateData(int[] data)
    {
        var points = new List<Point>(PointCount);
        for (var index = 0; index + 1 < data.Length; index += 2)
        {
            points.Add(new Point(data[index], data[index + 1]));
        }

        return points;
    }

    private static double Distance(IReadOnlyList<Point> a, IReadOnlyList<Point> b)
    {
        if (a.Count != b.Count || a.Count == 0)
        {
            return double.PositiveInfinity;
        }

        var total = 0.0;
        for (var index = 0; index < a.Count; index++)
        {
            total += Distance(a[index], b[index]);
        }

        return total / a.Count;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double PathLength(IReadOnlyList<Point> points)
    {
        var length = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            length += Distance(points[index - 1], points[index]);
        }

        return length;
    }

    private static double ResolveMaxDistance(int? tolerance)
    {
        return Math.Clamp(tolerance ?? DefaultMaxTemplateDistance, 80, 360);
    }

    private static BuiltInTemplate[] CreateBuiltInTemplates()
    {
        return
        [
            BuiltIn("P", (0, 1000), (0, 0), (650, 0), (780, 220), (650, 430), (0, 430)),
            BuiltIn("P", (0, 1000), (0, 0), (700, 0), (700, 420), (0, 420)),
            BuiltIn("S", (850, 0), (250, 0), (0, 250), (750, 500), (1000, 750), (250, 1000)),
            BuiltIn("Z", (0, 0), (900, 0), (0, 1000), (900, 1000)),
            BuiltIn("U", (0, 0), (0, 850), (240, 1000), (760, 1000), (1000, 850), (1000, 0)),
            BuiltIn("C", (1000, 100), (350, 0), (0, 500), (350, 1000), (1000, 900)),
            BuiltIn("V", (0, 0), (500, 1000), (1000, 0)),
            BuiltIn("N", (0, 1000), (0, 0), (1000, 1000), (1000, 0)),
            BuiltIn("M", (0, 1000), (0, 0), (500, 550), (1000, 0), (1000, 1000)),
            BuiltIn("W", (0, 0), (0, 1000), (500, 450), (1000, 1000), (1000, 0)),
            BuiltIn("L", (0, 0), (0, 1000), (850, 1000)),
            BuiltIn("L", (0, 1000), (0, 0), (850, 0))
        ];
    }

    private static BuiltInTemplate BuiltIn(string sign, params (double X, double Y)[] points)
    {
        return new BuiltInTemplate(
            sign,
            Normalize(points.Select(static point => new Point(point.X, point.Y)).ToList()));
    }

    private sealed record BuiltInTemplate(string Sign, IReadOnlyList<Point> Points);
}

public sealed record TemplateMatch(RegisteredGesture Gesture, double Distance);

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Point = System.Windows.Point;

namespace OpenQuickHost;

/// <summary>
/// 工业级手势识别器。
/// 基于 $1 Unistroke / Protractor Recognizer 算法思想优化：
/// - 64 点均匀重采样 + 质心居中平移 + 边界框等比缩放
/// - 黄金分割搜索（Golden Section Search）最佳旋转对齐角（支持 ±45° 自适应容差）
/// - 抗噪 8 方向滞后序列提取（Hysteresis Debounce Filter，消除手抖导致的伪方向）
/// </summary>
public static class MouseGestureTemplateRecognizer
{
    public const int PointCount = 64;
    public const double CoordinateScale = 1000.0;
    public const double MaxRotationAngle = Math.PI / 4.0; // 允许 ±45 度倾斜自适应
    private const double DefaultMaxTemplateDistance = 180.0;
    private const double Diagonal = 1414.21356; // sqrt(1000^2 + 1000^2)
    private const double HalfDiagonal = Diagonal * 0.5;

    private static readonly double GoldenRatio = (Math.Sqrt(5.0) - 1.0) / 2.0; // 0.6180339887
    private static readonly char[] Arrows = { '→', '↘', '↓', '↙', '←', '↖', '↑', '↗' };
    private static readonly double EighthPi = Math.PI / 4.0;
    private static readonly double TwoPi = Math.PI * 2.0;

    private static readonly BuiltInTemplate[] BuiltInTemplates = CreateBuiltInTemplates();

    /// <summary>
    /// 将原始点轨迹编码为 128 整数的标准模板数组（64点 * (X, Y)），用于跨版本/清单持久化。
    /// </summary>
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

    /// <summary>
    /// 校验是否是合法的模板数据。
    /// </summary>
    public static bool HasTemplateData(int[]? data)
    {
        return data is { Length: PointCount * 2 };
    }

    /// <summary>
    /// 在候选模板集合中搜索最佳匹配。
    /// 使用黄金分割搜索最佳旋转角，兼顾旋转容差与几何拓扑相似度。
    /// </summary>
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

            var templatePoints = DecodeTemplateData(template.Data!);
            var (distance, bestAngle) = DistanceAtBestAngle(candidate, templatePoints, -MaxRotationAngle, MaxRotationAngle);
            var maxDistance = ResolveMaxDistance(template.Tolerance);

            if (distance > maxDistance)
            {
                continue;
            }

            var score = Math.Clamp(1.0 - (distance / HalfDiagonal), 0.0, 1.0);
            if (best == null || distance < best.Distance)
            {
                best = new TemplateMatch(template, distance, score, bestAngle);
            }
        }

        return best;
    }

    /// <summary>
    /// 识别系统预置的几何图形或字母手势。
    /// </summary>
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
            var (distance, _) = DistanceAtBestAngle(candidate, template.Points, -MaxRotationAngle, MaxRotationAngle);
            if (distance < bestDistance)
            {
                best = template;
                bestDistance = distance;
            }
        }

        return best != null && bestDistance <= 220.0 ? best.Sign : null;
    }

    /// <summary>
    /// 高抗噪 8 方向序列提取算法。
    /// 采用步长积累 + 滞后死区（Hysteresis）+ 瞬态去抖（Debounce）过滤手抖尖峰。
    /// </summary>
    public static string ExtractSequence(IReadOnlyList<Point> rawPoints, double minStepDistance = 24.0)
    {
        if (rawPoints == null || rawPoints.Count < 2)
        {
            return string.Empty;
        }

        // 1. 过滤无效点与过近距离抖动
        var cleanPoints = new List<Point>(rawPoints.Count);
        foreach (var pt in rawPoints)
        {
            if (double.IsNaN(pt.X) || double.IsNaN(pt.Y)) continue;
            if (cleanPoints.Count > 0 && (pt - cleanPoints[^1]).Length < 2.0) continue;
            cleanPoints.Add(pt);
        }

        if (cleanPoints.Count < 2)
        {
            return string.Empty;
        }

        var rawDirections = new List<(int Direction, double Length)>();
        var anchor = cleanPoints[0];
        var currentDir = -1;
        var accumulatedLength = 0.0;

        for (var i = 1; i < cleanPoints.Count; i++)
        {
            var current = cleanPoints[i];
            var dx = current.X - anchor.X;
            var dy = current.Y - anchor.Y;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));

            if (dist < minStepDistance && i < cleanPoints.Count - 1)
            {
                continue;
            }

            var angle = (Math.Atan2(dy, dx) + TwoPi) % TwoPi;
            var rawDir = (int)Math.Round(angle / EighthPi) % 8;

            if (currentDir == -1)
            {
                currentDir = rawDir;
                accumulatedLength = dist;
            }
            else if (rawDir != currentDir)
            {
                // 滞后判定：计算当前位移角与基准方向角的偏角
                var baseAngle = currentDir * EighthPi;
                var angleDiff = Math.Abs(angle - baseAngle);
                if (angleDiff > Math.PI) angleDiff = TwoPi - angleDiff;

                // 只有当偏角大于 36 度 (~0.63 rad) 时，才确认为新拐向
                if (angleDiff > 0.628 || dist >= minStepDistance * 1.5)
                {
                    rawDirections.Add((currentDir, accumulatedLength));
                    currentDir = rawDir;
                    accumulatedLength = dist;
                }
                else
                {
                    accumulatedLength += dist;
                }
            }
            else
            {
                accumulatedLength += dist;
            }

            anchor = current;
        }

        if (currentDir != -1)
        {
            rawDirections.Add((currentDir, accumulatedLength));
        }

        if (rawDirections.Count == 0)
        {
            return string.Empty;
        }

        // 2. 瞬态抖动过滤（例如 A -> B -> A 且 B 极短时，消除 B）
        var filteredDirs = new List<(int Direction, double Length)>();
        for (var i = 0; i < rawDirections.Count; i++)
        {
            var item = rawDirections[i];
            if (filteredDirs.Count > 0 && item.Direction == filteredDirs[^1].Direction)
            {
                filteredDirs[^1] = (filteredDirs[^1].Direction, filteredDirs[^1].Length + item.Length);
                continue;
            }

            // 检查是否是微小尖峰震荡
            if (i > 0 && i < rawDirections.Count - 1)
            {
                var prev = filteredDirs.Count > 0 ? filteredDirs[^1] : rawDirections[i - 1];
                var next = rawDirections[i + 1];
                if (prev.Direction == next.Direction && item.Length < minStepDistance * 0.85)
                {
                    // 忽略当前的微小突变
                    continue;
                }
            }

            filteredDirs.Add(item);
        }

        // 3. 构建最终箭头序列
        var sb = new StringBuilder(filteredDirs.Count);
        foreach (var dir in filteredDirs)
        {
            sb.Append(Arrows[dir.Direction]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 标准化点集：重采样 -> 居中平移 -> 缩放至标准正方形。
    /// </summary>
    public static IReadOnlyList<Point> Normalize(IReadOnlyList<Point> rawPath)
    {
        var points = rawPath
            .Where(static point => !double.IsNaN(point.X) && !double.IsNaN(point.Y))
            .ToList();
        if (points.Count < 2 || PathLength(points) < 1.0)
        {
            return [];
        }

        var resampled = Resample(points, PointCount);
        if (resampled.Count != PointCount)
        {
            return [];
        }

        var scaled = ScaleToBox(resampled, CoordinateScale);
        return TranslateToCentroid(scaled);
    }

    /// <summary>
    /// 黄金分割搜索最优旋转角下的点云欧氏距离。
    /// </summary>
    private static (double Distance, double BestAngle) DistanceAtBestAngle(
        IReadOnlyList<Point> points,
        IReadOnlyList<Point> template,
        double a,
        double b,
        double threshold = 0.035) // ~2度精度
    {
        var x1 = b - (GoldenRatio * (b - a));
        var x2 = a + (GoldenRatio * (b - a));
        var f1 = DistanceAtAngle(points, template, x1);
        var f2 = DistanceAtAngle(points, template, x2);

        while (Math.Abs(b - a) > threshold)
        {
            if (f1 < f2)
            {
                b = x2;
                x2 = x1;
                f2 = f1;
                x1 = b - (GoldenRatio * (b - a));
                f1 = DistanceAtAngle(points, template, x1);
            }
            else
            {
                a = x1;
                x1 = x2;
                f1 = f2;
                x2 = a + (GoldenRatio * (b - a));
                f2 = DistanceAtAngle(points, template, x2);
            }
        }

        var bestAngle = (a + b) / 2.0;
        var minDistance = Math.Min(f1, f2);
        return (minDistance, bestAngle);
    }

    private static double DistanceAtAngle(IReadOnlyList<Point> points, IReadOnlyList<Point> template, double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var total = 0.0;

        for (var i = 0; i < points.Count && i < template.Count; i++)
        {
            var p = points[i];
            var rotatedX = (p.X * cos) - (p.Y * sin);
            var rotatedY = (p.X * sin) + (p.Y * cos);
            var t = template[i];
            var dx = rotatedX - t.X;
            var dy = rotatedY - t.Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total / points.Count;
    }

    private static List<Point> Resample(IReadOnlyList<Point> source, int targetCount)
    {
        var points = source.ToList();
        var totalLength = PathLength(points);
        if (totalLength <= 0) return [];

        var interval = totalLength / (targetCount - 1);
        if (interval <= 0) return [];

        var resampled = new List<Point>(targetCount) { points[0] };
        var distanceSinceLast = 0.0;

        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var segmentLength = Distance(previous, current);
            if (segmentLength <= 0) continue;

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
        if (scale <= 0) return [];

        var offsetX = (size - (width * size / scale)) / 2.0;
        var offsetY = (size - (height * size / scale)) / 2.0;
        return points
            .Select(point => new Point(
                ((point.X - minX) * size / scale) + offsetX,
                ((point.Y - minY) * size / scale) + offsetY))
            .ToList();
    }

    private static IReadOnlyList<Point> TranslateToCentroid(IReadOnlyList<Point> points)
    {
        if (points.Count == 0) return points;
        var cx = points.Average(static p => p.X);
        var cy = points.Average(static p => p.Y);
        return points.Select(p => new Point(p.X - cx, p.Y - cy)).ToList();
    }

    public static IReadOnlyList<Point> DecodeTemplateData(int[] data)
    {
        var points = new List<Point>(PointCount);
        for (var index = 0; index + 1 < data.Length; index += 2)
        {
            points.Add(new Point(data[index], data[index + 1]));
        }

        return TranslateToCentroid(points);
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
            BuiltIn("P-REV", (1000, 1000), (1000, 0), (350, 0), (220, 220), (350, 430), (1000, 430)),
            BuiltIn("S", (850, 0), (250, 0), (0, 250), (750, 500), (1000, 750), (250, 1000)),
            BuiltIn("Z", (0, 0), (900, 0), (0, 1000), (900, 1000)),
            BuiltIn("U", (0, 0), (0, 850), (240, 1000), (760, 1000), (1000, 850), (1000, 0)),
            BuiltIn("C", (1000, 100), (350, 0), (0, 500), (350, 1000), (1000, 900)),
            BuiltIn("V", (0, 0), (500, 1000), (1000, 0)),
            BuiltIn("INVERTED-V", (0, 1000), (500, 0), (1000, 1000)),
            BuiltIn("N", (0, 1000), (0, 0), (1000, 1000), (1000, 0)),
            BuiltIn("M", (0, 1000), (0, 0), (500, 550), (1000, 0), (1000, 1000)),
            BuiltIn("W", (0, 0), (0, 1000), (500, 450), (1000, 1000), (1000, 0)),
            BuiltIn("L", (0, 0), (0, 1000), (850, 1000)),
            BuiltIn("L", (0, 1000), (0, 0), (850, 0)),
            BuiltIn("CIRCLE", (500, 0), (1000, 500), (500, 1000), (0, 500), (500, 0)),
            BuiltIn("TRIANGLE", (500, 0), (1000, 1000), (0, 1000), (500, 0)),
            BuiltIn("RECTANGLE", (0, 0), (1000, 0), (1000, 1000), (0, 1000), (0, 0))
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

public sealed record TemplateMatch(
    RegisteredGesture Gesture,
    double Distance,
    double Score = 1.0,
    double RotationAngle = 0.0);

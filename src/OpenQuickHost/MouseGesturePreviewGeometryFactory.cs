using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

internal static class MouseGesturePreviewGeometryFactory
{
    public static Geometry Create(string? sequence, int[]? data, double size = 52, double padding = 8)
    {
        var points = GetScaledPoints(sequence, data, size, padding);
        return BuildGeometry(points);
    }

    public static WpfBrush CreateBrush(string? sequence, int[]? data, double size = 52, double padding = 8)
    {
        var points = GetScaledPoints(sequence, data, size, padding);
        return BuildBrush(points, size);
    }

    public static (Geometry Geometry, WpfBrush Brush) CreatePreview(string? sequence, int[]? data, double size = 52, double padding = 8)
    {
        var points = GetScaledPoints(sequence, data, size, padding);
        return (BuildGeometry(points), BuildBrush(points, size));
    }

    private static List<WpfPoint> GetScaledPoints(string? sequence, int[]? data, double size, double padding)
    {
        if (MouseGestureTemplateRecognizer.HasTemplateData(data))
        {
            return ScalePoints(DecodeTemplateData(data!), size, padding);
        }

        var specialPoints = TryGetSpecialShapePoints(sequence);
        if (specialPoints != null && specialPoints.Count > 1)
        {
            return ScalePoints(specialPoints, size, padding);
        }

        var points = BuildSequencePoints(MouseGestureNaming.NormalizeSequence(sequence));
        return ScalePoints(points, size, padding);
    }

    private static List<WpfPoint>? TryGetSpecialShapePoints(string? sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence)) return null;
        var s = sequence.Trim().ToUpperInvariant();

        // 1. 心形手势 (♥ / HEART)
        if (s is "HEART" or "♥" or "↘↓↙↑" or "↙↓↘↑")
        {
            return
            [
                new WpfPoint(500, 260),
                new WpfPoint(420, 140),
                new WpfPoint(300, 60),
                new WpfPoint(140, 60),
                new WpfPoint(40, 180),
                new WpfPoint(30, 350),
                new WpfPoint(120, 530),
                new WpfPoint(280, 720),
                new WpfPoint(500, 940), // 底部心尖
                new WpfPoint(720, 720),
                new WpfPoint(880, 530),
                new WpfPoint(970, 350),
                new WpfPoint(960, 180),
                new WpfPoint(860, 60),
                new WpfPoint(700, 60),
                new WpfPoint(580, 140),
                new WpfPoint(500, 260)
            ];
        }

        // 2. Alpha 鱼形手势 (α / ALPHA)
        if (s is "ALPHA" or "α" or "↘↗↘" or "ALPHA 鱼形")
        {
            return
            [
                new WpfPoint(920, 140),
                new WpfPoint(760, 290),
                new WpfPoint(560, 480),
                new WpfPoint(360, 680),
                new WpfPoint(180, 780),
                new WpfPoint(70, 660),
                new WpfPoint(50, 480),
                new WpfPoint(90, 300),
                new WpfPoint(210, 180),
                new WpfPoint(370, 180),
                new WpfPoint(550, 360),
                new WpfPoint(750, 680),
                new WpfPoint(920, 860)
            ];
        }

        // 3. 打勾手势 (✔ / CHECKMARK)
        if (s is "CHECKMARK" or "✔" or "CHECK" or "↘↗")
        {
            return
            [
                new WpfPoint(100, 520),
                new WpfPoint(240, 660),
                new WpfPoint(380, 880), // 底部转折
                new WpfPoint(540, 660),
                new WpfPoint(720, 420),
                new WpfPoint(880, 200),
                new WpfPoint(960, 80)
            ];
        }

        // 4. 画圆手势 (⭕ / CIRCLE / LOOP)
        if (s is "CIRCLE" or "LOOP" or "⭕" or "O" or "画圆")
        {
            var circlePoints = new List<WpfPoint>(17);
            for (var i = 0; i <= 16; i++)
            {
                var rad = i * (Math.PI * 2.0 / 16.0);
                circlePoints.Add(new WpfPoint(500 + (420 * Math.Cos(rad)), 500 + (420 * Math.Sin(rad))));
            }
            return circlePoints;
        }

        // 5. S 字形手势 (S / S-SHAPE)
        if (s is "S" or "S 型" or "S-SHAPE")
        {
            return
            [
                new WpfPoint(820, 160),
                new WpfPoint(650, 80),
                new WpfPoint(400, 80),
                new WpfPoint(220, 200),
                new WpfPoint(220, 360),
                new WpfPoint(360, 480),
                new WpfPoint(640, 560),
                new WpfPoint(780, 680),
                new WpfPoint(780, 840),
                new WpfPoint(600, 940),
                new WpfPoint(360, 940),
                new WpfPoint(180, 840)
            ];
        }

        // 6. Z 字形手势 (Z / Z-SHAPE)
        if (s is "Z" or "Z 型" or "Z-SHAPE" or "→↙→" or "↓↗↓")
        {
            return
            [
                new WpfPoint(150, 150),
                new WpfPoint(850, 150),
                new WpfPoint(150, 850),
                new WpfPoint(850, 850)
            ];
        }

        // 7. W 字形手势 (W / W-SHAPE)
        if (s is "W" or "W 型" or "W-SHAPE" or "↓↗↓↗" or "↘↗↘↗")
        {
            return
            [
                new WpfPoint(150, 150),
                new WpfPoint(350, 850),
                new WpfPoint(500, 420),
                new WpfPoint(650, 850),
                new WpfPoint(850, 150)
            ];
        }

        // 8. P 字形手势 (P / P-SHAPE)
        if (s is "P" or "P 型" or "P-SHAPE" or "↑→↓←")
        {
            return
            [
                new WpfPoint(250, 900),
                new WpfPoint(250, 100),
                new WpfPoint(650, 100),
                new WpfPoint(780, 280),
                new WpfPoint(650, 480),
                new WpfPoint(250, 480)
            ];
        }

        // 9. C 字形手势 (C / C-SHAPE)
        if (s is "C" or "C 型" or "C-SHAPE" or "←↓→" or "→↓←")
        {
            return
            [
                new WpfPoint(800, 180),
                new WpfPoint(600, 90),
                new WpfPoint(350, 90),
                new WpfPoint(150, 280),
                new WpfPoint(150, 720),
                new WpfPoint(350, 910),
                new WpfPoint(600, 910),
                new WpfPoint(800, 820)
            ];
        }

        // 10. U 字形手势 (U / U-SHAPE)
        if (s is "U" or "U 型" or "U-SHAPE" or "↓→↑")
        {
            return
            [
                new WpfPoint(200, 150),
                new WpfPoint(200, 680),
                new WpfPoint(320, 880),
                new WpfPoint(680, 880),
                new WpfPoint(800, 680),
                new WpfPoint(800, 150)
            ];
        }

        // 11. 上下往返 (↑↓)
        if (s is "↑↓" or "上下往返")
        {
            return
            [
                new WpfPoint(380, 850),
                new WpfPoint(380, 150),
                new WpfPoint(620, 150),
                new WpfPoint(620, 850)
            ];
        }

        // 12. 下上往返 (↓↑)
        if (s is "↓↑" or "下上往返")
        {
            return
            [
                new WpfPoint(380, 150),
                new WpfPoint(380, 850),
                new WpfPoint(620, 850),
                new WpfPoint(620, 150)
            ];
        }

        // 13. 三角形 (▲ / TRIANGLE)
        if (s is "TRIANGLE" or "▲")
        {
            return
            [
                new WpfPoint(500, 80),
                new WpfPoint(940, 920),
                new WpfPoint(60, 920),
                new WpfPoint(500, 80)
            ];
        }

        // 14. 矩形 (■ / RECTANGLE)
        if (s is "RECTANGLE" or "■")
        {
            return
            [
                new WpfPoint(100, 100),
                new WpfPoint(900, 100),
                new WpfPoint(900, 900),
                new WpfPoint(100, 900),
                new WpfPoint(100, 100)
            ];
        }

        return null;
    }

    private static WpfBrush BuildBrush(IReadOnlyList<WpfPoint> points, double size)
    {
        var startPoint = points.Count > 0 ? points[0] : new WpfPoint(size * 0.2, size * 0.8);
        var endPoint = points.Count > 1 ? points[^1] : new WpfPoint(size * 0.8, size * 0.2);

        // 如果起笔和落笔距离较短（如闭环、回旋图形），寻找离起笔点欧氏距离最远的点作为渐变终点
        var dx = endPoint.X - startPoint.X;
        var dy = endPoint.Y - startPoint.Y;
        var distSq = (dx * dx) + (dy * dy);
        if (distSq < 100 && points.Count > 2)
        {
            var furthest = points.OrderByDescending(p => {
                var pdx = p.X - startPoint.X;
                var pdy = p.Y - startPoint.Y;
                return (pdx * pdx) + (pdy * pdy);
            }).FirstOrDefault();

            if (furthest != default)
            {
                endPoint = furthest;
            }
        }

        // 兜底：如果完全重合，向对角微小偏移
        if (Math.Abs(startPoint.X - endPoint.X) < 1e-3 && Math.Abs(startPoint.Y - endPoint.Y) < 1e-3)
        {
            endPoint = new WpfPoint(startPoint.X + 1, startPoint.Y + 1);
        }

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = startPoint,
            EndPoint = endPoint
        };

        brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(255, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(255, 110, 231, 183), 0.35));
        brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(255, 16, 185, 129), 0.85));
        brush.GradientStops.Add(new GradientStop(WpfColor.FromArgb(255, 5, 150, 105), 1.0));

        brush.Freeze();
        return brush;
    }

    private static List<WpfPoint> DecodeTemplateData(int[] data)
    {
        var points = new List<WpfPoint>(data.Length / 2);
        for (var index = 0; index + 1 < data.Length; index += 2)
        {
            points.Add(new WpfPoint(data[index], data[index + 1]));
        }

        return points;
    }

    private static List<WpfPoint> BuildSequencePoints(string sequence)
    {
        var points = new List<WpfPoint> { new(0, 0) };
        var current = new WpfPoint(0, 0);
        foreach (var ch in sequence)
        {
            var delta = ch switch
            {
                '↑' => new WpfVector(0, -1),
                '↗' => new WpfVector(1, -1),
                '→' => new WpfVector(1, 0),
                '↘' => new WpfVector(1, 1),
                '↓' => new WpfVector(0, 1),
                '↙' => new WpfVector(-1, 1),
                '←' => new WpfVector(-1, 0),
                '↖' => new WpfVector(-1, -1),
                _ => new WpfVector(0, 0)
            };
            current += delta;
            points.Add(current);
        }

        return points.Count > 1 ? points : [new WpfPoint(0, 0), new WpfPoint(1, 0)];
    }

    private static List<WpfPoint> ScalePoints(IReadOnlyList<WpfPoint> points, double size, double padding)
    {
        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var scale = Math.Min((size - (padding * 2)) / width, (size - (padding * 2)) / height);
        var actualWidth = width * scale;
        var actualHeight = height * scale;
        var offsetX = padding + ((size - (padding * 2) - actualWidth) / 2);
        var offsetY = padding + ((size - (padding * 2) - actualHeight) / 2);
        return points
            .Select(point => new WpfPoint(
                offsetX + ((point.X - minX) * scale),
                offsetY + ((point.Y - minY) * scale)))
            .ToList();
    }

    private static Geometry BuildGeometry(IReadOnlyList<WpfPoint> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }
}

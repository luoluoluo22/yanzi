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
        var points = MouseGestureTemplateRecognizer.HasTemplateData(data)
            ? DecodeTemplateData(data!)
            : BuildSequencePoints(MouseGestureNaming.NormalizeSequence(sequence));
        return ScalePoints(points, size, padding);
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

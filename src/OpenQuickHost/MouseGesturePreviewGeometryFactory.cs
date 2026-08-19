using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

internal static class MouseGesturePreviewGeometryFactory
{
    public static Geometry Create(string? sequence, int[]? data, double size = 52, double padding = 8)
    {
        var points = MouseGestureTemplateRecognizer.HasTemplateData(data)
            ? DecodeTemplateData(data!)
            : BuildSequencePoints(MouseGestureNaming.NormalizeSequence(sequence));
        return BuildGeometry(ScalePoints(points, size, padding));
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

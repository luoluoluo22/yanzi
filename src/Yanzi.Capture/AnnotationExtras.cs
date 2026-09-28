using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

public sealed class TextAnnotation(
    Point origin,
    string text,
    Brush foreground,
    double fontSize) : Annotation
{
    public Point Origin { get; private set; } = origin;
    public string Text { get; set; } = text;
    public Brush Foreground { get; set; } = foreground;
    public double FontSize { get; set; } = fontSize;

    private FormattedText Measure() => new(
        Text,
        CultureInfo.GetCultureInfo("zh-CN"),
        FlowDirection.LeftToRight,
        new Typeface(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal),
        FontSize,
        Foreground,
        1.0);

    public override Rect Bounds
    {
        get
        {
            var ft = Measure();
            return new Rect(
                Origin.X,
                Origin.Y,
                Math.Max(8, ft.WidthIncludingTrailingWhitespace),
                Math.Max(8, ft.Height));
        }
    }

    public override void Draw(DrawingContext dc) =>
        dc.DrawText(Measure(), Origin);

    public override bool HitTest(Point point) =>
        new Rect(
            Bounds.X - 8,
            Bounds.Y - 8,
            Bounds.Width + 16,
            Bounds.Height + 16).Contains(point);

    public override void Move(Vector delta) => Origin += delta;
}

public sealed class PenAnnotation : Annotation
{
    public List<Point> Points { get; } = [];
    public Brush Stroke { get; set; } = Brushes.OrangeRed;
    public double Thickness { get; set; } = 5;

    public PenAnnotation(IEnumerable<Point> points)
    {
        Points.AddRange(points);
    }

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            var minX = Points.Min(p => p.X);
            var minY = Points.Min(p => p.Y);
            var maxX = Points.Max(p => p.X);
            var maxY = Points.Max(p => p.Y);
            var pad = Math.Max(8, Thickness);
            return new Rect(
                minX - pad,
                minY - pad,
                Math.Max(1, maxX - minX) + pad * 2,
                Math.Max(1, maxY - minY) + pad * 2);
        }
    }

    public override void Draw(DrawingContext dc)
    {
        if (Points.Count == 0) return;
        var pen = new Pen(Stroke, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        if (Points.Count == 1)
        {
            dc.DrawEllipse(Stroke, null, Points[0], Thickness / 2, Thickness / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            for (var i = 1; i < Points.Count; i++)
                ctx.LineTo(Points[i], true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    public override bool HitTest(Point point)
    {
        if (!new Rect(
                Bounds.X - 6,
                Bounds.Y - 6,
                Bounds.Width + 12,
                Bounds.Height + 12).Contains(point))
            return false;

        for (var i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1];
            var b = Points[i];
            var ab = b - a;
            if (ab.LengthSquared < 0.01) continue;
            var ap = point - a;
            var t = Math.Clamp(Vector.Multiply(ap, ab) / ab.LengthSquared, 0, 1);
            var nearest = a + ab * t;
            if ((point - nearest).Length <= Math.Max(10, Thickness + 5))
                return true;
        }
        return false;
    }

    public override void Move(Vector delta)
    {
        for (var i = 0; i < Points.Count; i++)
            Points[i] += delta;
    }
}

public sealed class MosaicAnnotation : Annotation
{
    private readonly BitmapSource _pixelated;
    public Rect Rect { get; private set; }

    private MosaicAnnotation(BitmapSource pixelated, Rect rect)
    {
        _pixelated = pixelated;
        Rect = rect;
    }

    public static MosaicAnnotation Create(
        BitmapSource source,
        Rect rect,
        int blockSize = 14)
    {
        var x = Math.Clamp((int)Math.Floor(rect.X), 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(rect.Y), 0, source.PixelHeight - 1);
        var width = Math.Clamp(
            (int)Math.Ceiling(rect.Width),
            1,
            source.PixelWidth - x);
        var height = Math.Clamp(
            (int)Math.Ceiling(rect.Height),
            1,
            source.PixelHeight - y);

        var crop = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        var formatted = crop.Format == PixelFormats.Bgra32
            ? (BitmapSource)crop
            : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        formatted.CopyPixels(pixels, stride, 0);

        for (var by = 0; by < height; by += blockSize)
        {
            for (var bx = 0; bx < width; bx += blockSize)
            {
                long bb = 0, gg = 0, rr = 0, aa = 0, count = 0;
                var yMax = Math.Min(height, by + blockSize);
                var xMax = Math.Min(width, bx + blockSize);

                for (var py = by; py < yMax; py++)
                {
                    for (var px = bx; px < xMax; px++)
                    {
                        var i = py * stride + px * 4;
                        bb += pixels[i];
                        gg += pixels[i + 1];
                        rr += pixels[i + 2];
                        aa += pixels[i + 3];
                        count++;
                    }
                }

                if (count == 0) continue;
                var b = (byte)(bb / count);
                var g = (byte)(gg / count);
                var r = (byte)(rr / count);
                var a = (byte)(aa / count);

                for (var py = by; py < yMax; py++)
                {
                    for (var px = bx; px < xMax; px++)
                    {
                        var i = py * stride + px * 4;
                        pixels[i] = b;
                        pixels[i + 1] = g;
                        pixels[i + 2] = r;
                        pixels[i + 3] = a;
                    }
                }
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        return new MosaicAnnotation(
            bitmap,
            new Rect(x, y, width, height));
    }

    public override Rect Bounds => Rect;

    public override void Draw(DrawingContext dc)
    {
        dc.DrawImage(_pixelated, Rect);
        dc.DrawRectangle(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1),
            Rect);
    }

    public override bool HitTest(Point point) => Rect.Contains(point);

    public override void Move(Vector delta) =>
        Rect = new Rect(Rect.Location + delta, Rect.Size);
}

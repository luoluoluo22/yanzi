using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

public sealed class CaptureDocument
{
    public BitmapSource BaseImage { get; }
    public ObservableCollection<Annotation> Annotations { get; } = [];
    public CommandHistory History { get; } = new();

    public CaptureDocument(BitmapSource baseImage)
    {
        BaseImage = baseImage;
        if (BaseImage.CanFreeze) BaseImage.Freeze();
    }
}

public abstract class Annotation
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract Rect Bounds { get; }
    public abstract void Draw(DrawingContext dc);
    public abstract bool HitTest(Point point);
    public abstract void Move(Vector delta);
}

public sealed class RectangleAnnotation(Rect rect) : Annotation
{
    public Rect Rect { get; private set; } = Normalize(rect);
    public Brush Stroke { get; set; } = Brushes.DeepSkyBlue;
    public double Thickness { get; set; } = 4;
    public override Rect Bounds => Rect;

    public override void Draw(DrawingContext dc) =>
        dc.DrawRectangle(null, new Pen(Stroke, Thickness), Rect);

    public override bool HitTest(Point point) =>
        Rect.Contains(point) || new Rect(Rect.X - 8, Rect.Y - 8, Rect.Width + 16, Rect.Height + 16).Contains(point);

    public override void Move(Vector delta) => Rect = new Rect(Rect.Location + delta, Rect.Size);

    private static Rect Normalize(Rect r) => new(
        Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom),
        Math.Abs(r.Width), Math.Abs(r.Height));
}

public sealed class ArrowAnnotation(Point start, Point end) : Annotation
{
    public Point Start { get; private set; } = start;
    public Point End { get; private set; } = end;
    public Brush Stroke { get; set; } = Brushes.OrangeRed;
    public double Thickness { get; set; } = 5;

    public override Rect Bounds => new(
        Math.Min(Start.X, End.X) - 10, Math.Min(Start.Y, End.Y) - 10,
        Math.Abs(End.X - Start.X) + 20, Math.Abs(End.Y - Start.Y) + 20);

    public override void Draw(DrawingContext dc)
    {
        var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, Start, End);
        var v = Start - End;
        if (v.Length < 2) return;
        v.Normalize();
        var n = new Vector(-v.Y, v.X);
        var p1 = End + v * 22 + n * 10;
        var p2 = End + v * 22 - n * 10;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(End, true, true);
            ctx.LineTo(p1, true, false);
            ctx.LineTo(p2, true, false);
        }
        dc.DrawGeometry(Stroke, null, geo);
    }

    public override bool HitTest(Point point)
    {
        var ab = End - Start;
        var ap = point - Start;
        if (ab.LengthSquared < 1) return (point - Start).Length < 12;
        var t = Math.Clamp(Vector.Multiply(ap, ab) / ab.LengthSquared, 0, 1);
        var nearest = Start + ab * t;
        return (point - nearest).Length <= 12;
    }

    public override void Move(Vector delta)
    {
        Start += delta;
        End += delta;
    }
}

public sealed class ImageAnnotation(BitmapSource image, Rect rect) : Annotation
{
    public BitmapSource Image { get; } = image;
    public Rect Rect { get; private set; } = rect;
    public override Rect Bounds => Rect;
    public override void Draw(DrawingContext dc) => dc.DrawImage(Image, Rect);
    public override bool HitTest(Point point) => Rect.Contains(point);
    public override void Move(Vector delta) => Rect = new Rect(Rect.Location + delta, Rect.Size);
}

public interface IEditCommand
{
    void Execute();
    void Undo();
}

public sealed class AddAnnotationCommand(CaptureDocument doc, Annotation annotation) : IEditCommand
{
    public void Execute() => doc.Annotations.Add(annotation);
    public void Undo() => doc.Annotations.Remove(annotation);
}

public sealed class RemoveAnnotationCommand(CaptureDocument doc, Annotation annotation) : IEditCommand
{
    private int _index;
    public void Execute()
    {
        _index = doc.Annotations.IndexOf(annotation);
        doc.Annotations.Remove(annotation);
    }
    public void Undo() => doc.Annotations.Insert(Math.Clamp(_index, 0, doc.Annotations.Count), annotation);
}

public sealed class MoveAnnotationCommand(Annotation annotation, Vector delta) : IEditCommand
{
    public void Execute() => annotation.Move(delta);
    public void Undo() => annotation.Move(-delta);
}

public sealed class CommandHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    public event Action? Changed;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void RecordExecuted(IEditCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var command)) return;
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var command)) return;
        command.Execute();
        _undo.Push(command);
        Changed?.Invoke();
    }
}

public static class DocumentRenderer
{
    public static BitmapSource Render(CaptureDocument doc)
    {
        var width = doc.BaseImage.PixelWidth;
        var height = doc.BaseImage.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(doc.BaseImage, new Rect(0, 0, width, height));
            foreach (var annotation in doc.Annotations)
                annotation.Draw(dc);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}

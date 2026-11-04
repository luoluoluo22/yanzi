using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Fixed-size, vertically scrolling tiles. Sizes include container margins.</summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty TileWidthProperty = DependencyProperty.Register(
        nameof(TileWidth), typeof(double), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(92d, FrameworkPropertyMetadataOptions.AffectsMeasure), ValidSize);
    public static readonly DependencyProperty TileHeightProperty = DependencyProperty.Register(
        nameof(TileHeight), typeof(double), typeof(VirtualizingTilePanel),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure), ValidSize);
    private static bool ValidSize(object value) => value is double d && double.IsFinite(d) && d > 0;
    public double TileWidth { get => (double)GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public double TileHeight { get => (double)GetValue(TileHeightProperty); set => SetValue(TileHeightProperty, value); }
    private int _columns = 1;
    public int Columns => _columns;
    private double _offset;
    private Size _extent, _viewport;

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner == null) return default;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : TileWidth;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : TileHeight;
        _columns = Math.Max(1, (int)(width / TileWidth));
        _viewport = new Size(width, height);
        _extent = new Size(width, Math.Ceiling((double)owner.Items.Count / _columns) * TileHeight);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - height));
        ScrollOwner?.InvalidateScrollInfo();
        var first = Math.Max(0, (int)(_offset / TileHeight) - 1) * _columns;
        var last = Math.Min(owner.Items.Count - 1,
            ((int)Math.Ceiling((_offset + height) / TileHeight) + 1) * _columns - 1);
        _ = InternalChildren; // Initialize the items-host generator before first use.
        var generator = ItemContainerGenerator;
        // Recycle before generating so a long jump never holds both pages of containers.
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last) continue;
            if (generator is IRecyclingItemContainerGenerator recycling)
                recycling.Recycle(position, 1);
            else generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
        if (last >= first)
        {
            var start = generator.GeneratorPositionFromIndex(first);
            var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
            using (generator.StartAt(start, GeneratorDirection.Forward, true))
            {
                for (var index = first; index <= last; index++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var created);
                    if (created || !InternalChildren.Contains(child))
                    {
                        if (childIndex == InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(TileWidth, TileHeight));
                }
            }
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * TileWidth,
                index / _columns * TileHeight - _offset, TileWidth, TileHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        // The generator has already removed/moved these containers from its mapping.
        if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        else if (args.Action == NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
        else if (args.Action == NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        var top = index / _columns * TileHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + TileHeight > _offset + ViewportHeight)
            SetVerticalOffset(top + TileHeight - ViewportHeight);
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        DependencyObject? child = visual;
        while (child != null && VisualTreeHelper.GetParent(child) != this)
            child = VisualTreeHelper.GetParent(child);
        if (child is UIElement element)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(
                new GeneratorPosition(InternalChildren.IndexOf(element), 0));
            if (index >= 0) BringIndexIntoView(index);
        }
        return rectangle;
    }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void SetHorizontalOffset(double offset) { }
    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        var next = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (next == _offset) return;
        _offset = next;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }
    public void LineUp() => SetVerticalOffset(_offset - TileHeight);
    public void LineDown() => SetVerticalOffset(_offset + TileHeight);
    public void PageUp() => SetVerticalOffset(_offset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(_offset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(_offset - TileHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset + TileHeight * 3);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}

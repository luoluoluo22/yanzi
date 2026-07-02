using Avalonia;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace Yanzi.Shared;

public class RadialMenuService
{
    private readonly Func<string, IEnumerable<CommandItem>> _commandProvider;
    private readonly RadialMenuSettings _settings;
    private readonly List<RadialMenuPageSettings> _pages = [];
    private string _currentPageId = string.Empty;
    private readonly Stack<string> _pageStack = new();

    public ObservableCollection<RadialMenuItemViewModel> Items { get; } = [];
    public ObservableCollection<RadialMenuItemViewModel> OuterItems { get; } = [];
    public ObservableCollection<RadialMenuItemViewModel> ChildItems { get; } = [];
    public ObservableCollection<RadialMenuItemViewModel> GrandChildItems { get; } = [];
    public ObservableCollection<RadialSeparatorViewModel> MainSeparators { get; } = [];
    public ObservableCollection<RadialSeparatorViewModel> OuterSeparators { get; } = [];
    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators { get; } = [];
    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators { get; } = [];

    public bool HasChildRing { get; private set; }
    public bool HasGrandChildRing { get; private set; }
    public string ChildRingTitle { get; private set; } = string.Empty;
    public string GrandChildRingTitle { get; private set; } = string.Empty;
    
    public double ChildRingCenterX { get; private set; }
    public double ChildRingCenterY { get; private set; }
    public double GrandChildRingCenterX { get; private set; }
    public double GrandChildRingCenterY { get; private set; }

    public double ChildRingEllipseX => ChildRingCenterX - 100;
    public double ChildRingEllipseY => ChildRingCenterY - 100;
    public double ChildRingCenterEllipseX => ChildRingCenterX - 26;
    public double ChildRingCenterEllipseY => ChildRingCenterY - 26;
    public double ChildRingTitleX => ChildRingCenterX - 75;
    public double ChildRingTitleY => ChildRingCenterY - 10;

    public double GrandChildRingEllipseX => GrandChildRingCenterX - 80;
    public double GrandChildRingEllipseY => GrandChildRingCenterY - 80;
    public double GrandChildRingCenterEllipseX => GrandChildRingCenterX - 22;
    public double GrandChildRingCenterEllipseY => GrandChildRingCenterY - 22;
    public double GrandChildRingTitleX => GrandChildRingCenterX - 64;
    public double GrandChildRingTitleY => GrandChildRingCenterY - 10;

    public string PageTitle { get; private set; } = "燕环";
    public string CenterPrimaryText { get; private set; } = "燕环";

    public RadialMenuService(Func<string, IEnumerable<CommandItem>> commandProvider, RadialMenuSettings settings)
    {
        _commandProvider = commandProvider;
        _settings = settings;
        LoadPages();
    }

    private void LoadPages()
    {
        _pages.Clear();
        if (_settings.Pages == null || _settings.Pages.Count == 0)
        {
            _pages.Add(new RadialMenuPageSettings { Id = "default", Name = "燕环" });
        }
        else
        {
            _pages.AddRange(_settings.Pages);
        }
        _currentPageId = string.IsNullOrWhiteSpace(_settings.SelectedPageId) 
            ? _pages.FirstOrDefault()?.Id ?? "default" 
            : _settings.SelectedPageId;
    }

    public void BuildItems(int radius, double windowWidth, double windowHeight)
    {
        BuildItems(radius, windowWidth, windowHeight, windowWidth / 2, windowHeight / 2);
    }

    public void BuildItems(int radius, double windowWidth, double windowHeight, double centerX, double centerY)
    {
        var effectiveRadius = Math.Clamp(radius - 10, 68, 80);
        Items.Clear();
        OuterItems.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();
        ClearChildRing();
        ClearGrandChildRing();
        
        var items = _commandProvider(_currentPageId).ToList();
        var page = _pages.FirstOrDefault(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        PageTitle = page?.Name ?? "燕环";
        CenterPrimaryText = PageTitle;

        var center = new Point(centerX, centerY);
        BuildSeparators(MainSeparators, center.X, center.Y, 26, 110, RadialMenuSettings.InnerSlotCount);
        BuildSeparators(OuterSeparators, center.X, center.Y, 110, 176, RadialMenuSettings.OuterSlotCount);

        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var angle = (-90 + index * 45) * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 78 - 32;
            var y = center.Y + Math.Sin(angle) * 78 - 25;
            var item = items.ElementAtOrDefault(index);
            var slot = GetSlotSettings(_currentPageId, index);
            Items.Add(new RadialMenuItemViewModel(_currentPageId, index, item, slot?.ChildPageId ?? string.Empty, 
                ResolvePageName(slot?.ChildPageId), x, y, angle * 180.0 / Math.PI, RadialMenuRing.Inner,
                CreateSectorGeometry(center.X, center.Y, 26, 110, angle * 180.0 / Math.PI - 22.5, angle * 180.0 / Math.PI + 22.5)));
        }

        for (var offset = 0; offset < RadialMenuSettings.OuterSlotCount; offset++)
        {
            var index = RadialMenuSettings.InnerSlotCount + offset;
            var angleDegrees = -90 + offset * 22.5;
            var angle = angleDegrees * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * 143 - 25;
            var y = center.Y + Math.Sin(angle) * 143 - 20;
            var item = items.ElementAtOrDefault(index);
            var slot = GetSlotSettings(_currentPageId, index);
            OuterItems.Add(new RadialMenuItemViewModel(_currentPageId, index, item, slot?.ChildPageId ?? string.Empty, 
                ResolvePageName(slot?.ChildPageId), x, y, angleDegrees, RadialMenuRing.Outer,
                CreateSectorGeometry(center.X, center.Y, 110, 176, angleDegrees - 11.25, angleDegrees + 11.25)));
        }
    }

    private RadialMenuSlotSettings? GetSlotSettings(string pageId, int index)
    {
        var key = $"{pageId}_{index}";
        if (_settings.Slots != null && _settings.Slots.TryGetValue(key, out var slot))
        {
            return slot;
        }
        return null;
    }

    private string ResolvePageName(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            return string.Empty;
        return _pages.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? pageId;
    }

    private static void BuildSeparators(ObservableCollection<RadialSeparatorViewModel> target, 
                                        double centerX, double centerY, 
                                        double innerRadius, double outerRadius, int count)
    {
        target.Clear();
        var step = 360.0 / count;
        for (var index = 0; index < count; index++)
        {
            var angle = (-90 - step / 2 + index * step) * Math.PI / 180.0;
            target.Add(new RadialSeparatorViewModel(
                centerX + Math.Cos(angle) * innerRadius,
                centerY + Math.Sin(angle) * innerRadius,
                centerX + Math.Cos(angle) * outerRadius,
                centerY + Math.Sin(angle) * outerRadius));
        }
    }

    public void BuildChildRing(RadialMenuItemViewModel parent, double windowWidth, double windowHeight)
    {
        BuildChildRing(parent, windowWidth, windowHeight, windowWidth / 2, windowHeight / 2);
    }

    public void BuildChildRing(RadialMenuItemViewModel parent, double windowWidth, double windowHeight, double centerX, double centerY)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearChildRing();
            return;
        }

        var items = _commandProvider(parent.ChildPageId).ToList();
        var angle = parent.AngleDegrees * Math.PI / 180.0;
        var center = new Point(centerX, centerY);
        ChildRingCenterX = center.X + Math.Cos(angle) * 210;
        ChildRingCenterY = center.Y + Math.Sin(angle) * 210;
        var clamped = ClampRingCenter(ChildRingCenterX, ChildRingCenterY, 112, windowWidth, windowHeight);
        ChildRingCenterX = clamped.X;
        ChildRingCenterY = clamped.Y;
        ChildRingTitle = parent.ChildPageTitle;

        BuildSeparators(ChildSeparators, ChildRingCenterX, ChildRingCenterY, 26, 100, RadialMenuSettings.InnerSlotCount);

        ChildItems.Clear();
        const double radius = 64;
        for (var index = 0; index < 8; index++)
        {
            var childAngle = (-90 + index * 45) * Math.PI / 180.0;
            var x = ChildRingCenterX + Math.Cos(childAngle) * radius - 28;
            var y = ChildRingCenterY + Math.Sin(childAngle) * radius - 22;
            var item = items.ElementAtOrDefault(index);
            var slot = GetSlotSettings(parent.ChildPageId, index);
            ChildItems.Add(new RadialMenuItemViewModel(parent.ChildPageId, index, item, 
                slot?.ChildPageId ?? string.Empty, ResolvePageName(slot?.ChildPageId), 
                x, y, childAngle * 180.0 / Math.PI, RadialMenuRing.Child,
                CreateSectorGeometry(ChildRingCenterX, ChildRingCenterY, 26, 100, childAngle * 180.0 / Math.PI - 22.5, childAngle * 180.0 / Math.PI + 22.5)));
        }

        HasChildRing = true;
    }

    public void BuildGrandChildRing(RadialMenuItemViewModel parent)
    {
        BuildGrandChildRing(parent, 640, 640);
    }

    public void BuildGrandChildRing(RadialMenuItemViewModel parent, double windowWidth, double windowHeight)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearGrandChildRing();
            return;
        }

        var items = _commandProvider(parent.ChildPageId).ToList();
        var angle = parent.AngleDegrees * Math.PI / 180.0;
        GrandChildRingCenterX = ChildRingCenterX + Math.Cos(angle) * 180;
        GrandChildRingCenterY = ChildRingCenterY + Math.Sin(angle) * 180;
        var clamped = ClampRingCenter(GrandChildRingCenterX, GrandChildRingCenterY, 92, windowWidth, windowHeight);
        GrandChildRingCenterX = clamped.X;
        GrandChildRingCenterY = clamped.Y;
        GrandChildRingTitle = parent.ChildPageTitle;

        BuildSeparators(GrandChildSeparators, GrandChildRingCenterX, GrandChildRingCenterY, 22, 80, RadialMenuSettings.InnerSlotCount);

        GrandChildItems.Clear();
        const double radius = 52;
        for (var index = 0; index < 8; index++)
        {
            var childAngleDegrees = -90 + index * 45.0;
            var childAngle = childAngleDegrees * Math.PI / 180.0;
            var x = GrandChildRingCenterX + Math.Cos(childAngle) * radius - 25;
            var y = GrandChildRingCenterY + Math.Sin(childAngle) * radius - 20;
            var item = items.ElementAtOrDefault(index);
            var slot = GetSlotSettings(parent.ChildPageId, index);
            GrandChildItems.Add(new RadialMenuItemViewModel(
                parent.ChildPageId,
                index,
                item,
                slot?.ChildPageId ?? string.Empty,
                ResolvePageName(slot?.ChildPageId),
                x,
                y,
                childAngleDegrees,
                RadialMenuRing.GrandChild,
                CreateSectorGeometry(GrandChildRingCenterX, GrandChildRingCenterY, 22, 80, childAngleDegrees - 22.5, childAngleDegrees + 22.5)));
        }

        HasGrandChildRing = true;
    }

    public void ClearChildRing()
    {
        ChildItems.Clear();
        ChildSeparators.Clear();
        ClearGrandChildRing();
        HasChildRing = false;
        ChildRingTitle = string.Empty;
    }

    public void ClearGrandChildRing()
    {
        GrandChildItems.Clear();
        GrandChildSeparators.Clear();
        HasGrandChildRing = false;
        GrandChildRingTitle = string.Empty;
    }

    private static Geometry CreateSectorGeometry(double centerX, double centerY, double innerRadius, double outerRadius, double startAngleDegrees, double endAngleDegrees)
    {
        static Avalonia.Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180.0;
            return new Avalonia.Point(
                cx + Math.Cos(radians) * radius,
                cy + Math.Sin(radians) * radius);
        }

        var outerStart = PointOnCircle(centerX, centerY, outerRadius, startAngleDegrees);
        var outerEnd = PointOnCircle(centerX, centerY, outerRadius, endAngleDegrees);
        var innerEnd = PointOnCircle(centerX, centerY, innerRadius, endAngleDegrees);
        var innerStart = PointOnCircle(centerX, centerY, innerRadius, startAngleDegrees);
        var isLargeArc = Math.Abs(endAngleDegrees - startAngleDegrees) > 180.0;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = outerEnd,
            Size = new Size(outerRadius, outerRadius),
            RotationAngle = 0,
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });
        figure.Segments.Add(new LineSegment
        {
            Point = innerEnd
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = innerStart,
            Size = new Size(innerRadius, innerRadius),
            RotationAngle = 0,
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.CounterClockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static (double X, double Y) ClampRingCenter(double x, double y, double radius, double width, double height)
    {
        return (
            Math.Clamp(x, radius + 8, width - radius - 8),
            Math.Clamp(y, radius + 8, height - radius - 8)
        );
    }

    public void EnterChildPage(string childPageId)
    {
        if (_pages.All(p => !p.Id.Equals(childPageId, StringComparison.OrdinalIgnoreCase)))
            return;

        _pageStack.Push(_currentPageId);
        _currentPageId = childPageId;
    }

    public void ReturnToParentPage()
    {
        if (_pageStack.Count == 0)
            return;
        _currentPageId = _pageStack.Pop();
    }

    public void CyclePage(int direction)
    {
        if (_pages.Count <= 1)
            return;

        var currentIndex = Math.Max(0, _pages.FindIndex(p => p.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase)));
        var nextIndex = (currentIndex + direction + _pages.Count) % _pages.Count;
        _currentPageId = _pages[nextIndex].Id;
        _pageStack.Clear();
    }

    private record Point(double X, double Y);
}

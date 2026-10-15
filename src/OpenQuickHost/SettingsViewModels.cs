using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 42));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed record SettingsNavigationItem(string Key, string IconReference, string Title, string Accent)
{
    public Geometry? IconGeometry => ExtensionIconLibrary.ResolveVectorIcon(IconReference);
}

public sealed record SyncProviderOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record AutoSyncDelayOption(int Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record MouseGestureTemplateDefinition(string Sequence, string Name, string Description);

public sealed class SettingsMouseGestureItem
{
    public SettingsMouseGestureItem(
        string extensionId,
        string title,
        string category,
        string triggerLabel,
        string sequence,
        string displayName,
        int[]? data,
        int minDistance,
        int? tolerance,
        ImageSource? iconSource,
        Geometry? vectorIcon,
        System.Windows.Media.Brush accentBrush,
        string displayGlyph,
        IReadOnlyList<string>? boundWhitelistAppPaths = null,
        IReadOnlyList<string>? boundBlacklistAppPaths = null)
    {
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        TriggerLabel = triggerLabel;
        Sequence = sequence;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? MouseGestureNaming.GetDisplayName(sequence) : displayName;
        Data = data;
        MinDistance = minDistance;
        Tolerance = tolerance;
        IconSource = iconSource;
        VectorIcon = vectorIcon;
        AccentBrush = accentBrush;
        DisplayGlyph = displayGlyph;
        BoundWhitelistAppPaths = boundWhitelistAppPaths ?? [];
        BoundBlacklistAppPaths = boundBlacklistAppPaths ?? [];
        var preview = MouseGesturePreviewGeometryFactory.CreatePreview(sequence, data, size: 44, padding: 5);
        PreviewGeometry = preview.Geometry;
        PreviewBrush = preview.Brush;
    }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string TriggerLabel { get; }

    public string Sequence { get; }

    public string DisplayName { get; }

    public int[]? Data { get; }

    public IReadOnlyList<string> BoundWhitelistAppPaths { get; }

    public IReadOnlyList<string> BoundBlacklistAppPaths { get; }

    public int MinDistance { get; }

    public int? Tolerance { get; }

    public string ScopeSummaryText
    {
        get
        {
            var parts = new List<string>();
            if (BoundWhitelistAppPaths.Count > 0) parts.Add($"限定 {BoundWhitelistAppPaths.Count} 个应用");
            if (BoundBlacklistAppPaths.Count > 0) parts.Add($"禁用 {BoundBlacklistAppPaths.Count} 个应用");
            return parts.Count > 0 ? string.Join(", ", parts) : "全局生效";
        }
    }

    public string DetailText
    {
        get
        {
            var sequenceText = string.IsNullOrWhiteSpace(Sequence) ? "已录制图形" : $"序列 {Sequence}";
            return $"{sequenceText} · 最小距离 {MinDistance}px" + (Tolerance is > 0 ? $" · 容差 {Tolerance}" : string.Empty);
        }
    }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public Geometry PreviewGeometry { get; }

    public System.Windows.Media.Brush PreviewBrush { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);
}

public sealed class MouseGestureQuickBindItem : INotifyPropertyChanged
{
    private MouseGestureExtensionOption? _selectedExtension;
    private MouseGestureAppOption? _selectedApp;
    private string _extensionSearchText = string.Empty;
    private string _appSearchText = string.Empty;
    private ObservableCollection<MouseGestureExtensionOption> _filteredExtensionOptions = [];
    private ObservableCollection<MouseGestureAppOption> _filteredAppOptions = [];
    private ICollectionView? _filteredAppOptionsView;
    private bool _isExtensionPopupOpen;
    private bool _isAppPopupOpen;

    public MouseGestureQuickBindItem(
        string sequence,
        string displayName,
        string description,
        string? assignedTitle,
        int[]? data = null,
        string? assignedExtensionId = null,
        IReadOnlyList<string>? boundWhitelistAppPaths = null,
        IReadOnlyList<string>? boundBlacklistAppPaths = null)
    {
        Sequence = sequence;
        DisplayName = displayName;
        Description = description;
        AssignedTitle = assignedTitle ?? string.Empty;
        Data = data;
        AssignedExtensionId = assignedExtensionId;
        BoundWhitelistAppPaths = boundWhitelistAppPaths ?? [];
        BoundBlacklistAppPaths = boundBlacklistAppPaths ?? [];
        var preview = MouseGesturePreviewGeometryFactory.CreatePreview(sequence, data, size: 48, padding: 6);
        PreviewGeometry = preview.Geometry;
        PreviewBrush = preview.Brush;
        RebuildFilteredAppOptionsView();
    }

    public string Sequence { get; }

    public int[]? Data { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string AssignedTitle { get; }

    public string? AssignedExtensionId { get; }

    public IReadOnlyList<string> BoundWhitelistAppPaths { get; }

    public IReadOnlyList<string> BoundBlacklistAppPaths { get; }

    public Geometry PreviewGeometry { get; }

    public System.Windows.Media.Brush PreviewBrush { get; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssignedTitle);

    public string StatusText => IsAssigned ? $"已配置: {AssignedTitle}" : "未绑定";

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExtensionSearchText)));
        }
    }

    public string AppSearchText
    {
        get => _appSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _appSearchText)
            {
                return;
            }

            _appSearchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppSearchText)));
        }
    }

    public ObservableCollection<MouseGestureExtensionOption> FilteredExtensionOptions
    {
        get => _filteredExtensionOptions;
        set
        {
            if (ReferenceEquals(value, _filteredExtensionOptions))
            {
                return;
            }

            _filteredExtensionOptions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredExtensionOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredExtensionListVisibility)));
        }
    }

    public ObservableCollection<MouseGestureAppOption> FilteredAppOptions
    {
        get => _filteredAppOptions;
        set
        {
            if (ReferenceEquals(value, _filteredAppOptions))
            {
                return;
            }

            _filteredAppOptions = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppOptions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppListVisibility)));
            RebuildFilteredAppOptionsView();
        }
    }

    public ICollectionView? FilteredAppOptionsView
    {
        get => _filteredAppOptionsView;
        private set
        {
            if (ReferenceEquals(value, _filteredAppOptionsView))
            {
                return;
            }

            _filteredAppOptionsView = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilteredAppOptionsView)));
        }
    }

    public Visibility FilteredExtensionListVisibility => FilteredExtensionOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FilteredAppListVisibility => FilteredAppOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool IsExtensionPopupOpen
    {
        get => _isExtensionPopupOpen;
        set
        {
            if (value == _isExtensionPopupOpen)
            {
                return;
            }

            _isExtensionPopupOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExtensionPopupOpen)));
        }
    }

    public bool IsAppPopupOpen
    {
        get => _isAppPopupOpen;
        set
        {
            if (value == _isAppPopupOpen)
            {
                return;
            }

            _isAppPopupOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAppPopupOpen)));
        }
    }

    public MouseGestureAppOption? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (Equals(value, _selectedApp)) return;
            _selectedApp = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedApp)));
        }
    }

public MouseGestureExtensionOption? SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            if (Equals(value, _selectedExtension))
            {
                return;
            }

            _selectedExtension = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedExtension)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RebuildFilteredAppOptionsView()
    {
        var view = CollectionViewSource.GetDefaultView(_filteredAppOptions);
        if (view is ListCollectionView listView)
        {
            listView.GroupDescriptions.Clear();
            listView.SortDescriptions.Clear();
            listView.SortDescriptions.Add(new SortDescription(nameof(MouseGestureAppOption.IsRunning), ListSortDirection.Descending));
            listView.SortDescriptions.Add(new SortDescription(nameof(MouseGestureAppOption.AppName), ListSortDirection.Ascending));
            listView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MouseGestureAppOption.GroupTitle)));
        }

        FilteredAppOptionsView = view;
    }
}

public sealed class MouseGestureAppOption : INotifyPropertyChanged
{
    private bool _isWhitelistSelected;
    private bool _isBlacklistSelected;

    public MouseGestureAppOption(string appName, string appPath, string category, bool isRunning)
    {
        AppName = appName;
        AppPath = appPath;
        Category = category;
        IsRunning = isRunning;
        DisplayLabel = isRunning ? $"🟢 [运行中] {appName}" : $"💻 {appName}";
        IconSource = ExtensionIconLibrary.TryExtractAssociatedIcon(appPath);
    }

    public string AppName { get; }
    public string AppPath { get; }
    public string Category { get; }
    public bool IsRunning { get; }
    public ImageSource? IconSource { get; }
    public bool HasIcon => IconSource != null;
    public string DisplayLabel { get; }
    public string GroupTitle => IsRunning ? "运行中的应用" : "全部应用";

    public bool IsWhitelistSelected
    {
        get => _isWhitelistSelected;
        set
        {
            if (_isWhitelistSelected != value)
            {
                _isWhitelistSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWhitelistSelected)));
                if (value && _isBlacklistSelected)
                {
                    IsBlacklistSelected = false;
                }
            }
        }
    }

    public bool IsBlacklistSelected
    {
        get => _isBlacklistSelected;
        set
        {
            if (_isBlacklistSelected != value)
            {
                _isBlacklistSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBlacklistSelected)));
                if (value && _isWhitelistSelected)
                {
                    IsWhitelistSelected = false;
                }
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => DisplayLabel;
}

public sealed class MouseGestureExtensionOption
{
    public MouseGestureExtensionOption(CommandItem command)
    {
        ExtensionId = command.ExtensionId;
        Label = command.Title;
        Category = command.Category;
        IconSource = command.IconSource;
        VectorIcon = command.VectorIcon;
        AccentBrush = command.AccentBrush;
        DisplayGlyph = command.DisplayGlyph;
    }

    public string ExtensionId { get; }

    public string Label { get; }

    public string Category { get; }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public override string ToString() => Label;
}

public class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightTextProperty =
        DependencyProperty.Register(
            nameof(HighlightText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBrush),
            typeof(System.Windows.Media.Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(System.Windows.Media.Brushes.DeepSkyBlue, OnHighlightPropertyChanged));

    public static readonly DependencyProperty HighlightBackgroundBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBackgroundBrush),
            typeof(System.Windows.Media.Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(System.Windows.Media.Brushes.Transparent, OnHighlightPropertyChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public System.Windows.Media.Brush HighlightBrush
    {
        get => (System.Windows.Media.Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public System.Windows.Media.Brush HighlightBackgroundBrush
    {
        get => (System.Windows.Media.Brush)GetValue(HighlightBackgroundBrushProperty);
        set => SetValue(HighlightBackgroundBrushProperty, value);
    }

    private static void OnHighlightPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightedTextBlock textBlock)
        {
            textBlock.RebuildInlines();
        }
    }

    private void RebuildInlines()
    {
        Inlines.Clear();

        var text = SourceText ?? string.Empty;
        var keyword = (HighlightText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (keyword.Length == 0)
        {
            Inlines.Add(new Run(text) { Foreground = Foreground });
            return;
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                Inlines.Add(new Run(text[startIndex..]) { Foreground = Foreground });
                break;
            }

            if (matchIndex > startIndex)
            {
                Inlines.Add(new Run(text[startIndex..matchIndex]) { Foreground = Foreground });
            }

            Inlines.Add(new Run(text.Substring(matchIndex, keyword.Length))
            {
                Foreground = HighlightBrush,
                Background = HighlightBackgroundBrush,
                FontWeight = FontWeights.SemiBold
            });

            startIndex = matchIndex + keyword.Length;
        }
    }
}

public sealed record SettingsShortcutItem(string ExtensionId, string Title, string Category, string? Shortcut)
{
    public string ShortcutValue => Shortcut ?? string.Empty;

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? "未设置" : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
}

public sealed record YarnSelectActionTypeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record YanmActivationKeyOption(string Value, string Label);

public sealed record MouseTriggerOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record YarnSelectExtensionOption(
    string ExtensionId,
    string Title,
    string Detail,
    ImageSource? IconSource,
    Geometry? VectorIcon,
    System.Windows.Media.Brush AccentBrush,
    string DisplayGlyph)
{
    public YarnSelectExtensionOption(CommandItem command)
        : this(
            command.ExtensionId,
            command.Title,
            string.IsNullOrWhiteSpace(command.OpenTarget)
                ? command.ItemKindLabel
                : $"{command.ItemKindLabel} · {command.OpenTarget}",
            command.IconSource,
            command.VectorIcon,
            command.AccentBrush,
            command.DisplayGlyph)
    {
    }

    public YarnSelectExtensionOption(string extensionId, string title)
        : this(extensionId, title, string.Empty, null, null, System.Windows.Media.Brushes.Transparent, string.Empty)
    {
    }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public override string ToString() => Title;
}

public sealed class RadialMenuSlotEditorItem : INotifyPropertyChanged
{
    private string _extensionId;
    private string _displayTitle;
    private string _childPageId;
    private string _extensionTitle;
    private string _childPageTitle;
    private bool _isHovered;
    private ImageSource? _iconSource;
    private Geometry? _vectorIcon;
    private System.Windows.Media.Brush _accentBrush;
    private string _displayGlyph;

    public RadialMenuSlotEditorItem(int index, string extensionId, string displayTitle, string childPageId, string extensionTitle, string childPageTitle, double x, double y, bool isOuter, Geometry sectorGeometry, ImageSource? iconSource, Geometry? vectorIcon, System.Windows.Media.Brush accentBrush, string displayGlyph)
    {
        Index = index;
        _extensionId = extensionId;
        _displayTitle = displayTitle;
        _childPageId = childPageId;
        _extensionTitle = extensionTitle;
        _childPageTitle = childPageTitle;
        X = x;
        Y = y;
        IsOuter = isOuter;
        SectorGeometry = sectorGeometry;
        _iconSource = iconSource;
        _vectorIcon = vectorIcon;
        _accentBrush = accentBrush;
        _displayGlyph = displayGlyph;
    }

    public int Index { get; }

    public string Label => (Index + 1).ToString(CultureInfo.InvariantCulture);

    public double X { get; }

    public double Y { get; }

    public bool IsOuter { get; }

    public Geometry SectorGeometry { get; }

    public double SlotWidth => IsOuter ? 62 : 76;

    public double SlotHeight => IsOuter ? 50 : 60;

    public double TitleWidth => IsOuter ? 50 : 60;

    public double IconSize => IsOuter ? 23 : 32;

    public double IconContainerSize => IsOuter ? 23 : 32;

    public CornerRadius IconCornerRadius => IsOuter ? new CornerRadius(6) : new CornerRadius(8);

    public double VectorIconSize => IsOuter ? 14 : 19;

    public double GlyphFontSize => IsOuter ? 11 : 13;

    public double PlusFontSize => IsOuter ? 20 : 24;

    public Thickness SlotPadding => IsOuter ? new Thickness(4, 2, 4, 0) : new Thickness(6, 4, 6, 0);

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (Equals(value, _iconSource))
            {
                return;
            }

            _iconSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImageIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public Geometry? VectorIcon
    {
        get => _vectorIcon;
        set
        {
            if (Equals(value, _vectorIcon))
            {
                return;
            }

            _vectorIcon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVectorIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public System.Windows.Media.Brush AccentBrush
    {
        get => _accentBrush;
        set
        {
            if (Equals(value, _accentBrush))
            {
                return;
            }

            _accentBrush = value;
            OnPropertyChanged();
        }
    }

    public string DisplayGlyph
    {
        get => _displayGlyph;
        set
        {
            value ??= string.Empty;
            if (value == _displayGlyph)
            {
                return;
            }

            _displayGlyph = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseGlyphIcon));
            OnPropertyChanged(nameof(HasPresentationIcon));
        }
    }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public bool HasPresentationIcon => HasImageIcon || HasVectorIcon || UseGlyphIcon;

    public bool IsEmpty => string.IsNullOrWhiteSpace(_extensionId) && string.IsNullOrWhiteSpace(_childPageId);

    public bool IsNotEmpty => !IsEmpty;

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (value == _isHovered)
            {
                return;
            }

            _isHovered = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));
        }
    }

    public string DisplayTitle
    {
        get => _displayTitle;
        set
        {
            value ??= string.Empty;
            if (value == _displayTitle)
            {
                return;
            }

            _displayTitle = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionTitle
    {
        get => _extensionTitle;
        set
        {
            value ??= string.Empty;
            if (value == _extensionTitle)
            {
                return;
            }

            _extensionTitle = value;
            OnPropertyChanged();
        }
    }

    public string ChildPageId
    {
        get => _childPageId;
        set
        {
            value ??= string.Empty;
            if (value == _childPageId)
            {
                return;
            }

            _childPageId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsNotEmpty));
        }
    }

    public string ChildPageTitle
    {
        get => _childPageTitle;
        set
        {
            value ??= string.Empty;
            if (value == _childPageTitle)
            {
                return;
            }

            _childPageTitle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChildPageTitle));
        }
    }

    public bool HasChildPageTitle => !string.IsNullOrWhiteSpace(_childPageTitle);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}

public sealed record RadialMenuPageEditorItem(string Id, string Name, ImageSource? Icon = null, bool IsAppPage = false, int Level = 0, string DisplayName = "")
{
    public System.Windows.Thickness IndentMargin => new(Level * 14, 0, 0, 0);
}

public sealed class YarnSelectPreviewKeyItem : INotifyPropertyChanged
{
    private bool _isConfigured;
    private bool _ruleEnabled;
    private string _ruleSummary = string.Empty;

    public string KeyCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public bool IsConfigured
    {
        get => _isConfigured;
        set
        {
            if (_isConfigured == value) return;
            _isConfigured = value;
            NotifyStyleProperties();
        }
    }

    public bool RuleEnabled
    {
        get => _ruleEnabled;
        set
        {
            if (_ruleEnabled == value) return;
            _ruleEnabled = value;
            NotifyStyleProperties();
        }
    }

    public string RuleSummary
    {
        get => _ruleSummary;
        set
        {
            if (_ruleSummary == value) return;
            _ruleSummary = value;
            OnPropertyChanged();
        }
    }

    public System.Windows.Media.Brush BackgroundBrush
    {
        get
        {
            if (!IsConfigured)
            {
                return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushSecondaryBtnBG"];
            }
            return RuleEnabled
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15803D"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151"));
        }
    }

    public System.Windows.Media.Brush BorderBrush
    {
        get
        {
            if (!IsConfigured)
            {
                return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushBorder"];
            }
            return RuleEnabled
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280"));
        }
    }

    public System.Windows.Media.Brush TextBrush
    {
        get
        {
            if (IsConfigured)
            {
                return System.Windows.Media.Brushes.White;
            }
            return (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushTextMain"];
        }
    }

    public bool HasGreenDot => IsConfigured && RuleEnabled;

    private void NotifyStyleProperties()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(RuleEnabled));
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(HasGreenDot));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class YarnSelectRuleItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string _triggerKey;
    private string _actionType;
    private string _extensionId;
    private string _extensionSearchText;
    private string _description;
    private bool _isKeyPickerOpen;
    private bool _isHighlighted;
    private ObservableCollection<YarnSelectExtensionOption> _filteredExtensionOptions = [];

    public Action? OnChangedAction { get; set; }

    public YarnSelectRuleItem(YarnSelectRuleSettings rule)
    {
        _enabled = rule.Enabled;
        _triggerKey = rule.TriggerKey;
        _actionType = rule.ActionType;
        _extensionId = rule.ExtensionId;
        _extensionSearchText = string.Empty;
        _description = rule.Description;
    }

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (value == _isHighlighted)
            {
                return;
            }

            _isHighlighted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ItemBorderBrush));
            OnPropertyChanged(nameof(ItemBackgroundBrush));
        }
    }

    public System.Windows.Media.Brush ItemBorderBrush => IsHighlighted
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"))
        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushBorder"];

    public System.Windows.Media.Brush ItemBackgroundBrush => IsHighlighted
        ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B"))
        : System.Windows.Media.Brushes.Transparent;

    public bool IsKeyPickerOpen
    {
        get => _isKeyPickerOpen;
        set
        {
            if (value == _isKeyPickerOpen)
            {
                return;
            }

            _isKeyPickerOpen = value;
            OnPropertyChanged();
        }
    }

    public void SelectTriggerKey(string key)
    {
        TriggerKey = key;
        IsKeyPickerOpen = false;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public string TriggerKey
    {
        get => _triggerKey;
        set
        {
            value = YarnSelectSettings.NormalizeTriggerKey(value);
            if (value == _triggerKey)
            {
                return;
            }

            _triggerKey = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public bool IsRunExtension => YarnSelectActionTypes.Normalize(ActionType) == YarnSelectActionTypes.RunExtension;

    public string ActionType
    {
        get => _actionType;
        set
        {
            value = YarnSelectActionTypes.Normalize(value);
            if (value == _actionType)
            {
                return;
            }

            _actionType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunExtension));
            OnChangedAction?.Invoke();
        }
    }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            OnPropertyChanged();
        }
    }

    private bool _isExtensionPickerOpen;

    public bool IsExtensionPickerOpen
    {
        get => _isExtensionPickerOpen;
        set
        {
            if (value == _isExtensionPickerOpen)
            {
                return;
            }

            _isExtensionPickerOpen = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<YarnSelectExtensionOption> FilteredExtensionOptions
    {
        get => _filteredExtensionOptions;
        set
        {
            if (ReferenceEquals(value, _filteredExtensionOptions))
            {
                return;
            }

            _filteredExtensionOptions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredExtensionListVisibility));
            IsExtensionPickerOpen = value.Count > 0;
        }
    }

    public Visibility FilteredExtensionListVisibility => FilteredExtensionOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string Description
    {
        get => _description;
        set
        {
            value ??= string.Empty;
            if (value == _description)
            {
                return;
            }

            _description = value;
            OnPropertyChanged();
            OnChangedAction?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsExtensionItem : INotifyPropertyChanged
{
    private bool _isPublished;
    private string _publisherName;
    private bool _isPublishing;
    private bool _isUnpublishing;
    private string _shortcut;
    private string _startupMode;
    private string _startupSchedule;
    private bool _isSelected;

    public SettingsExtensionItem(
        string extensionId,
        string title,
        string description,
        string category,
        string version,
        string directoryPath,
        string sourceLabel,
        bool canOpenDirectory,
        bool isEnabled,
        bool isPublished,
        string publisherName,
        string shortcut,
        ImageSource? iconSource,
        Geometry? vectorIcon,
        System.Windows.Media.Brush accentBrush,
        string displayGlyph,
        string startupMode,
        string startupSchedule)
    {
        ExtensionId = extensionId;
        Title = title;
        Description = description;
        Category = category;
        Version = version;
        DirectoryPath = directoryPath;
        SourceLabel = sourceLabel;
        CanOpenDirectory = canOpenDirectory;
        IsEnabled = isEnabled;
        _isPublished = isPublished;
        _publisherName = publisherName;
        _shortcut = shortcut ?? string.Empty;
        IconSource = iconSource;
        VectorIcon = vectorIcon;
        AccentBrush = accentBrush;
        DisplayGlyph = displayGlyph;
        _startupMode = startupMode ?? string.Empty;
        _startupSchedule = startupSchedule ?? string.Empty;
    }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Description { get; }

    public string Category { get; }

    public string Version { get; }

    public string DirectoryPath { get; }

    public string SourceLabel { get; }

    public bool CanOpenDirectory { get; }

    public bool IsEnabled { get; }

    public ImageSource? IconSource { get; }

    public Geometry? VectorIcon { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string DisplayGlyph { get; }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public bool IsRunning
    {
        get
        {
            if (string.IsNullOrEmpty(ExtensionId))
            {
                return false;
            }
            return RunningExtensionRegistry.GetSnapshot().Any(x => string.Equals(x.ExtensionId, ExtensionId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RefreshRunningState()
    {
        OnPropertyChanged(nameof(IsRunning));
    }

    public string Shortcut
    {
        get => _shortcut;
        set
        {
            if (_shortcut == value)
            {
                return;
            }

            _shortcut = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShortcutLabel));
            OnPropertyChanged(nameof(HasShortcut));
            OnPropertyChanged(nameof(ShortcutBadgeVisibility));
            OnPropertyChanged(nameof(ShortcutDetailLabel));
        }
    }

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? string.Empty : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);

    public Visibility ShortcutBadgeVisibility => HasShortcut ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CategoryBadgeVisibility =>
        !string.IsNullOrWhiteSpace(Category) &&
        !string.Equals(Category, "扩展", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Category, "小程序", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Category, "插件", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Category, "extension", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ShortcutDetailLabel => HasShortcut ? Shortcut : "未设置";

    public string StartupMode
    {
        get => _startupMode;
        set
        {
            value ??= string.Empty;
            if (_startupMode == value)
            {
                return;
            }

            _startupMode = value;
            NotifyStartupStateChanged();
        }
    }

    public string StartupSchedule
    {
        get => _startupSchedule;
        set
        {
            value ??= string.Empty;
            if (_startupSchedule == value)
            {
                return;
            }

            _startupSchedule = value;
            NotifyStartupStateChanged();
        }
    }

    public bool HasAppLaunchStartup => StartupMode.Equals("on_app_launch", StringComparison.OrdinalIgnoreCase);

    public bool HasScheduleStartup => !string.IsNullOrWhiteSpace(StartupSchedule);

    public string StartupActionLabel => HasAppLaunchStartup ? "关闭自启" : "开机自启";

    public string StartupDetailLabel => HasAppLaunchStartup ? "已启用" : "未启用";

    public string ScheduleDetailLabel => HasScheduleStartup ? ScheduleConfigWindow.CronToFriendly(StartupSchedule) : "未设置";

    public Visibility AutoStartBadgeVisibility => HasAppLaunchStartup ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ScheduleBadgeVisibility => HasScheduleStartup ? Visibility.Visible : Visibility.Collapsed;

    public string EnabledStateLabel => IsEnabled ? "已启用" : "已禁用";

    public string PublishedStateLabel => IsPublishedInStore ? "已发布到商店" : "仅本地";

    public string DescriptionOrFallback => string.IsNullOrWhiteSpace(Description) ? "这个小程序没有提供额外说明。" : Description;

    private bool _isBatchChecked;

    public bool IsBatchChecked
    {
        get => _isBatchChecked;
        set
        {
            if (_isBatchChecked == value)
            {
                return;
            }

            _isBatchChecked = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsPublished
    {
        get => _isPublished;
        set
        {
            if (_isPublished == value)
            {
                return;
            }

            _isPublished = value;
            NotifyPublishStateChanged();
        }
    }

    public string PublisherName
    {
        get => _publisherName;
        set
        {
            if (string.Equals(_publisherName, value, StringComparison.Ordinal))
            {
                return;
            }

            _publisherName = value;
            NotifyPublishStateChanged();
        }
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        set
        {
            if (_isPublishing == value)
            {
                return;
            }

            _isPublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsUnpublishing
    {
        get => _isUnpublishing;
        set
        {
            if (_isUnpublishing == value)
            {
                return;
            }

            _isUnpublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsPublishedInStore => IsPublished && !string.IsNullOrWhiteSpace(PublisherName);

    public bool IsOperationBusy => IsPublishing || IsUnpublishing;

    public string PublishActionLabel => IsPublishedInStore ? "更新商店版本" : "发布到商店";

    public string PublishButtonText => IsPublishing
        ? (IsPublishedInStore ? "更新中..." : "发布中...")
        : PublishActionLabel;

    public string UnpublishButtonText => IsUnpublishing ? "下线中..." : "下线";

    public Visibility PublishSpinnerVisibility => IsPublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PublishIconVisibility => IsPublishing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UnpublishSpinnerVisibility => IsUnpublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishIconVisibility => IsUnpublishing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PublishNewButtonVisibility => IsPublishedInStore ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PublishUpdateButtonVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StoreLinkButtonVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishButtonVisibility => CanUnpublish ? Visibility.Visible : Visibility.Collapsed;

    public string PublisherLabel => string.IsNullOrWhiteSpace(PublisherName) ? "未发布" : $"发布者：{PublisherName}";

    public bool CanUnpublish => IsPublishedInStore;

    public bool PublishButtonEnabled => !IsOperationBusy;

    public bool UnpublishButtonEnabled => CanUnpublish && !IsOperationBusy;

    public bool EditButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public bool OpenDirectoryButtonEnabled => CanOpenDirectory && !IsOperationBusy;

    public Visibility PublishedBadgeVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DisabledBadgeVisibility => !IsEnabled ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyPublishStateChanged()
    {
        OnPropertyChanged(nameof(IsPublished));
        OnPropertyChanged(nameof(PublisherName));
        OnPropertyChanged(nameof(IsPublishedInStore));
        OnPropertyChanged(nameof(PublishActionLabel));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(PublisherLabel));
        OnPropertyChanged(nameof(CanUnpublish));
        OnPropertyChanged(nameof(PublishNewButtonVisibility));
        OnPropertyChanged(nameof(PublishUpdateButtonVisibility));
        OnPropertyChanged(nameof(StoreLinkButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
        OnPropertyChanged(nameof(PublishedBadgeVisibility));
        OnPropertyChanged(nameof(PublishedStateLabel));
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsPublishing));
        OnPropertyChanged(nameof(IsUnpublishing));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(UnpublishButtonText));
        OnPropertyChanged(nameof(PublishSpinnerVisibility));
        OnPropertyChanged(nameof(PublishIconVisibility));
        OnPropertyChanged(nameof(UnpublishSpinnerVisibility));
        OnPropertyChanged(nameof(UnpublishIconVisibility));
        OnPropertyChanged(nameof(PublishButtonEnabled));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
        OnPropertyChanged(nameof(EditButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
        OnPropertyChanged(nameof(OpenDirectoryButtonEnabled));
    }

    private void NotifyStartupStateChanged()
    {
        OnPropertyChanged(nameof(StartupMode));
        OnPropertyChanged(nameof(StartupSchedule));
        OnPropertyChanged(nameof(HasAppLaunchStartup));
        OnPropertyChanged(nameof(HasScheduleStartup));
        OnPropertyChanged(nameof(StartupActionLabel));
        OnPropertyChanged(nameof(StartupDetailLabel));
        OnPropertyChanged(nameof(ScheduleDetailLabel));
        OnPropertyChanged(nameof(AutoStartBadgeVisibility));
        OnPropertyChanged(nameof(ScheduleBadgeVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsRecycleBinItem : INotifyPropertyChanged
{
    private bool _isRestoring;
    private bool _isDeletingPermanently;

    public SettingsRecycleBinItem(
        string itemId,
        string extensionId,
        string title,
        string category,
        string version,
        string deletedAtUtc)
    {
        ItemId = itemId;
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        Version = version;
        DeletedAtUtc = deletedAtUtc;
    }

    public string ItemId { get; }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string Version { get; }

    public string DeletedAtUtc { get; }

    public string DeletedAtLabel => DateTimeOffset.TryParse(DeletedAtUtc, out var timestamp)
        ? $"删除时间：{timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
        : "删除时间：未知";

    public bool IsRestoring
    {
        get => _isRestoring;
        set
        {
            if (_isRestoring == value)
            {
                return;
            }

            _isRestoring = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsDeletingPermanently
    {
        get => _isDeletingPermanently;
        set
        {
            if (_isDeletingPermanently == value)
            {
                return;
            }

            _isDeletingPermanently = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsOperationBusy => IsRestoring || IsDeletingPermanently;

    public string RestoreButtonText => IsRestoring ? "恢复中..." : "恢复";

    public string DeleteButtonText => IsDeletingPermanently ? "删除中..." : "彻底删除";

    public Visibility RestoreSpinnerVisibility => IsRestoring ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeleteSpinnerVisibility => IsDeletingPermanently ? Visibility.Visible : Visibility.Collapsed;

    public bool RestoreButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsRestoring));
        OnPropertyChanged(nameof(IsDeletingPermanently));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(RestoreSpinnerVisibility));
        OnPropertyChanged(nameof(DeleteSpinnerVisibility));
        OnPropertyChanged(nameof(RestoreButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record AccountSyncStatusView(
    string ModeText,
    string HealthText,
    string RevisionText,
    string ObjectCountText,
    string LastCheckedText,
    string ExplanationText,
    bool HasErrors,
    bool HasPending,
    IReadOnlyList<AccountSyncObjectStatusItem> Objects)
{
    public static AccountSyncStatusView Empty { get; } = new(
        "未登录账号",
        "登录燕子云后可查看账号配置同步状态",
        "云端版本 0",
        "待同步项目 0 个 · 等待中 0",
        "未连接",
        "账号配置同步与个人仓库备份相互独立。",
        false,
        false,
        []);
}

public sealed record AccountSyncObjectStatusItem(
    string ObjectId,
    string DisplayName,
    string StatusText,
    long Revision,
    string UpdatedAtText,
    string DetailText,
    bool IsPending,
    bool HasError,
    bool HasConflict,
    bool HistoryAvailable)
{
    public string ConflictSummary { get; init; } = "";
}

public sealed class PersonalConfigRestorePointItem
{
    public PersonalConfigRestorePointItem(LauncherConfigRestorePointInfo info)
    {
        RestorePointId = info.RestorePointId;
        CreatedAtText = DateTimeOffset.TryParse(info.CreatedAtUtc, out var createdAt)
            ? createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : info.CreatedAtUtc;
        DeviceText = SyncUserText.Device(info.SourceDeviceId, info.SourceDeviceName, DeviceIdentityStore.GetOrCreateDesktopDeviceId());
        SummaryText = $"已备份 {info.ObjectCount} 项设置 · 本次更新 {info.ChangedObjectIds.Count} 项";
        SizeText = info.SizeBytes < 1024
            ? $"{info.SizeBytes} B"
            : $"{info.SizeBytes / 1024.0:0.#} KB";
        RevisionText = $"备份版本 {info.Revision}";
    }

    public string RestorePointId { get; }
    public string CreatedAtText { get; }
    public string DeviceText { get; }
    public string SummaryText { get; }
    public string SizeText { get; }
    public string RevisionText { get; }
}

public sealed class ExtensionSyncConflictItem
{
    public ExtensionSyncConflictItem(ExtensionSyncConflictRecord record)
    {
        ExtensionId = record.ExtensionId;
        LocalText = record.LocalPurged
            ? $"本机：彻底删除"
            : record.LocalDeleted
                ? $"本机：删除"
                : $"本机：v{record.LocalVersion}";
        RemoteText = record.RemotePurged
            ? $"云端：彻底删除"
            : record.RemoteDeleted
                ? $"云端：删除"
                : $"云端：v{record.RemoteVersion}";
        RemoteDeviceText = !string.IsNullOrWhiteSpace(record.RemoteDeviceName)
            ? record.RemoteDeviceName!
            : !string.IsNullOrWhiteSpace(record.RemoteDeviceId) ? record.RemoteDeviceId! : "未知设备";
        DetectedAtText = DateTimeOffset.TryParse(record.DetectedAtUtc, out var detectedAt)
            ? detectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : record.DetectedAtUtc;
    }

    public string ExtensionId { get; set; }
    public string LocalText { get; set; }
    public string RemoteText { get; set; }
    public string RemoteDeviceText { get; set; }
    public string DetectedAtText { get; set; }
}

public sealed class ExtensionDataConflictItem
{
    public ExtensionDataConflictItem(ExtensionDataSyncState state)
    {
        ExtensionId = state.ExtensionId;
        Key = state.Key;
        var conflict = state.Conflict ?? new ExtensionDataConflict();
        LocalText = conflict.LocalDeleted
            ? "本机：已删除"
            : "本机：待同步的内容已保留";
        RemoteText = conflict.Remote.Deleted
            ? "云端：已删除"
            : "云端：有另一份修改";
        RemoteDeviceText = !string.IsNullOrWhiteSpace(conflict.Remote.UpdatedByDeviceName)
            ? conflict.Remote.UpdatedByDeviceName
            : !string.IsNullOrWhiteSpace(conflict.Remote.UpdatedByDeviceId)
                ? conflict.Remote.UpdatedByDeviceId
                : "未知设备";
        DetectedAtText = DateTimeOffset.TryParse(conflict.DetectedAtUtc, out var detectedAt)
            ? detectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : conflict.DetectedAtUtc;
    }

    public string ExtensionId { get; set; }
    public string Key { get; set; }
    public string DisplayId => $"{ExtensionId} / {Key}";
    public string LocalText { get; set; }
    public string RemoteText { get; set; }
    public string RemoteDeviceText { get; set; }
    public string DetectedAtText { get; set; }

    private static string ShortHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "无 hash" : value.Length <= 10 ? value : value[..10];
}

public sealed class PersonalSyncCommitItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private string? _diffText;

    public PersonalSyncCommitItem(string sha, string message, string author, DateTimeOffset committedAtUtc, string url)
    {
        Sha = sha;
        Message = string.IsNullOrWhiteSpace(message) ? "备份记录" : message;
        FriendlyMessage = GetFriendlyCommitMessage(Message);
        Author = string.IsNullOrWhiteSpace(author) ? "未知作者" : author;
        CommittedAtUtc = committedAtUtc;
        Url = url;
    }

    public string Sha { get; }
    public string ShortSha => Sha.Length <= 8 ? Sha : Sha[..8];
    public string Message { get; }
    public string FriendlyMessage { get; }
    public string Author { get; }
    public DateTimeOffset CommittedAtUtc { get; }
    public string LocalTimeLabel => CommittedAtUtc.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.CurrentCulture);
    public string Url { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (value != _isExpanded)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(DiffVisibility));
                OnPropertyChanged(nameof(DiffBtnText));
            }
        }
    }

    public string DiffBtnText => IsExpanded ? "收起技术详情" : "技术详情";

    public Visibility DiffVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string? DiffText
    {
        get => _diffText;
        set
        {
            if (value != _diffText)
            {
                _diffText = value;
                OnPropertyChanged(nameof(DiffText));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetFriendlyCommitMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "备份记录";
        }

        var msg = rawMessage.Trim();
        if (msg == "同步目录：更新个人扩展索引") return "已更新小程序列表";
        if (msg == "同步扩展：上传个人扩展包") return "已备份小程序";

        if (msg.Equals("Update yanm-state.json from Web", StringComparison.OrdinalIgnoreCase))
        {
            return "同步状态：更新燕幕组件状态 (云漫游)";
        }

        if (msg.StartsWith("上传数据：更新 ", StringComparison.OrdinalIgnoreCase))
        {
            var path = msg.Substring("上传数据：更新 ".Length).Trim();
            var detail = GetPathFriendlyName(path);
            return $"上传数据：更新 {detail}";
        }

        if (msg.StartsWith("上传数据：删除 ", StringComparison.OrdinalIgnoreCase))
        {
            var path = msg.Substring("上传数据：删除 ".Length).Trim();
            var detail = GetPathFriendlyName(path);
            return $"上传数据：删除 {detail}";
        }

        return msg;
    }

    private static string GetPathFriendlyName(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.EndsWith("state/launcher-config.json", StringComparison.OrdinalIgnoreCase))
            return "系统主设置与快捷菜单";
        if (normalized.EndsWith("state/yanm-state.json", StringComparison.OrdinalIgnoreCase))
            return "燕幕组件状态";
        if (normalized.EndsWith("state/config-manifest.json", StringComparison.OrdinalIgnoreCase))
            return "设置列表";
        if (normalized.Contains("state/config-changes/", StringComparison.OrdinalIgnoreCase))
            return "设置更改记录";
        if (normalized.EndsWith("settings-general.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】通用系统设置";
        if (normalized.EndsWith("settings-ai.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】AI 服务与模型设置";
        if (normalized.EndsWith("settings-hotkeys.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】系统主快捷键";
        if (normalized.EndsWith("settings-mouse-triggers.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】鼠标动作与手势触发规则";
        if (normalized.EndsWith("quick-panel-groups.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】快捷面板菜单分组";
        if (normalized.EndsWith("quick-panel-favorites.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】常用小程序收藏与禁用项";
        if (normalized.EndsWith("radial-menu-pages.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】轮盘菜单页面布局";
        if (normalized.EndsWith("yanm-layout.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】燕幕布局与样式";
        if (normalized.EndsWith("yanyu-rules.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】窗口别名 (燕语) 规则";
        if (normalized.EndsWith("window-controls.json", StringComparison.OrdinalIgnoreCase))
            return "【设置】窗口绑定、吸附与切换配置";
        if (normalized.Contains("packages/") && normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return "个人备份小程序包";
        if (normalized.Contains("appdata/"))
            return "小程序专属应用数据备份";

        return path;
    }
}

public sealed class EnvironmentVariableEditorItem : INotifyPropertyChanged
{
    private string _name;
    private string _value;
    private string _description;

    public EnvironmentVariableEditorItem(string name, string value, string description)
    {
        _name = name;
        _value = value;
        _description = description;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (value == _name)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (value == _description)
            {
                return;
            }

            _description = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ModelNameFirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
        {
            return s.Substring(0, 1).ToUpper();
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class SettingsAiProviderVM : INotifyPropertyChanged
{
    private readonly AiServiceProviderSettings _settings;
    public AiServiceProviderSettings RawSettings => _settings;

    public SettingsAiProviderVM(AiServiceProviderSettings settings)
    {
        _settings = settings;
    }

    public string Id => _settings.Id;

    public string Name
    {
        get => _settings.Name;
        set
        {
            if (_settings.Name != value)
            {
                _settings.Name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(AvatarChar));
            }
        }
    }

    public string ProviderType
    {
        get => _settings.ProviderType;
        set
        {
            if (_settings.ProviderType != value)
            {
                _settings.ProviderType = value;
                OnPropertyChanged(nameof(ProviderType));
            }
        }
    }

    public string BaseUrl
    {
        get => _settings.BaseUrl;
        set
        {
            if (_settings.BaseUrl != value)
            {
                _settings.BaseUrl = value;
                OnPropertyChanged(nameof(BaseUrl));
                OnPropertyChanged(nameof(PreviewUrl));
            }
        }
    }

    public string ApiKey
    {
        get => _settings.ApiKey;
        set
        {
            if (_settings.ApiKey != value)
            {
                _settings.ApiKey = value;
                OnPropertyChanged(nameof(ApiKey));
            }
        }
    }

    public bool IsEnabled
    {
        get => _settings.IsEnabled;
        set
        {
            if (_settings.IsEnabled != value)
            {
                _settings.IsEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public string SelectedModel
    {
        get => _settings.SelectedModel;
        set
        {
            if (_settings.SelectedModel != value)
            {
                _settings.SelectedModel = value;
                OnPropertyChanged(nameof(SelectedModel));
            }
        }
    }

    public ObservableCollection<string> Models { get; } = new();

    public string AvatarChar => !string.IsNullOrEmpty(Name) ? Name.Substring(0, 1).ToUpper() : "?";

    public string PreviewUrl => string.IsNullOrWhiteSpace(BaseUrl) ? "无预览" : $"{BaseUrl.TrimEnd('/')}/chat/completions";

    public Visibility DetailsVisibility => Visibility.Visible; // 仅在自身 DataContext 下总是 Visible，而在外层做 Visibility 绑定

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record SearchDisplayItem(string TabKey, string DisplayTitle, System.Windows.Media.Geometry? IconGeometry);
public sealed record SettingsSearchItem(string TabKey, string DisplayTitle, string MatchTerm);

public static class SettingsSearchData
{
    public static readonly List<SettingsSearchItem> AllSearchItems = new()
    {
        // 常规
        new("general", "常规 - 主题模式", "主题模式 暗黑模式 深色模式 浅色模式 theme mode dark light"),
        new("general", "常规 - 开机启动", "开机启动 随系统启动 自启动 startup launch"),
        new("general", "常规 - 自动检测更新", "自动检测更新 自动下载更新 升级 update version upgrade"),
        new("general", "常规 - 最小化到系统托盘", "最小化到系统托盘 关闭时最小化 托盘 任务栏 tray close"),
        new("general", "常规 - 自动刷新云状态", "启动后自动刷新云状态 同步云状态 refresh cloud"),
        new("general", "常规 - 窗口快速排列", "窗口快速排列 窗口分屏 布局轮盘 Snap layout"),
        new("general", "常规 - 窗口排列快捷键", "窗口排列快捷键 快捷键 组合键 视窗快捷键 hotkey shortcut"),

        // 模型服务
        new("ai", "模型服务 - API Key", "API Key 密钥 接口密钥 Token 密码 鉴权 apikey key secret"),
        new("ai", "模型服务 - 自定义 Base URL", "Base URL 接口地址 代理地址 域名 接口链接 自定义服务 url"),
        new("ai", "模型服务 - 模型名称", "模型名称 默认模型 模型切换 AI模型 Gemini Claude GPT model"),
        new("ai", "模型服务 - 系统提示词", "系统提示词 System Prompt 预设 角色扮演 prompt system"),

        // 环境变量
        new("environment", "环境变量 - 添加/编辑环境变量", "环境变量 变量配置 Notion Key OpenAI Key 密钥 环境变量列表 env var key token"),

        // 同步与备份
        new("sync", "同步与备份 - WebDAV 同步", "WebDAV 同步 云同步 坚果云 同步服务器 账号 密码 备份 sync backup account password webdav"),
        new("sync", "同步与备份 - 自动备份频率", "自动备份频率 备份频率 自动备份 备份时间 frequency"),
        new("sync", "同步与备份 - 备份与恢复操作", "立即备份 立即恢复 上传备份 下载备份 同步数据 restore"),

        // 扩展
        new("extensions", "小程序 - 小程序管理", "小程序 插件 本地小程序 启用小程序 禁用小程序 编辑 搜索 打开目录 小程序根目录 extension plugin folder"),

        // 回收站
        new("recycle", "回收站 - 小程序回收站", "回收站 小程序回收站 恢复 彻底删除 已删除插件 recycle bin trash restore"),

        // 快捷键
        new("shortcuts", "快捷键 - 快捷键绑定", "快捷键绑定 热键 录制快捷键 全局快捷键 组合键 shortcut hotkey binding"),

        // 鼠标触发
        new("quickpanel", "鼠标触发 - 背包触发方式", "背包 随身背包 面板触发 鼠标触发 快捷面板 右键 中键 X1键 X2键 长按 滚轮 trigger mouse right click"),

        // 鼠标手势
        new("mousegestures", "鼠标手势 - 手势绑定", "鼠标手势 手势绑定 绘制手势 轨迹 常用手势 gesture mouse draw"),

        // 燕环
        new("radial", "燕环 - 轮盘设置", "燕环 轮盘 游戏轮盘 Caps Lock 唤醒 槽位 子环 唤醒键 radial ring wheel"),

        // 燕选
        new("yarnselect", "燕选 - 选中操作", "燕选 选中操作 复制 剪切 粘贴 快捷操作 划词搜索 select copy paste selection"),

        // 燕幕
        new("yanm", "燕幕 - 仪表盘", "燕幕 仪表盘 WebView HTML 组件 全局信息层 Caps Lock 唤醒 双击 overlay webview html"),

        // 关于
        new("about", "关于 - 版本与协议", "关于 软件版本 官方网站 用户协议 开源许可 开源协议 about version update website")
    };
}

public class KeywordMatchToBrushConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string keyword && values[1] is string tag)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && !string.IsNullOrWhiteSpace(tag))
            {
                if (tag.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    keyword.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4DFFD600"));
                }
            }
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

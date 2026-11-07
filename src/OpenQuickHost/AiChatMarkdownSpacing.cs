using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentList = System.Windows.Documents.List;

namespace OpenQuickHost;

/// <summary>
/// Codex keeps headings at the body size and only makes them bold. Sasabune
/// instead enlarges them and gives them no vertical margin. These distances
/// leave a short break around a same-size heading, and the first and last
/// blocks stay flush with the bubble padding.
/// </summary>
public static class AiChatMarkdownSpacing
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(AiChatMarkdownSpacing),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsWatchingProperty =
        DependencyProperty.RegisterAttached(
            "IsWatching",
            typeof(bool),
            typeof(AiChatMarkdownSpacing),
            new PropertyMetadata(false));

    private static readonly EventHandler DocumentChanged = OnDocumentChanged;

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static void Apply(FlowDocument? document)
    {
        if (document is null)
            return;
        ApplyBlocks(document.Blocks, inList: false);
    }

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FlowDocumentScrollViewer viewer)
            return;

        if (args.NewValue is true)
        {
            viewer.Loaded += OnViewerLoaded;
            viewer.Unloaded += OnViewerUnloaded;
            if (viewer.IsLoaded)
                Watch(viewer);
            return;
        }

        viewer.Loaded -= OnViewerLoaded;
        viewer.Unloaded -= OnViewerUnloaded;
        Unwatch(viewer);
    }

    private static void OnViewerLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FlowDocumentScrollViewer viewer)
            Watch(viewer);
    }

    private static void OnViewerUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FlowDocumentScrollViewer viewer)
            Unwatch(viewer);
    }

    private static void Watch(FlowDocumentScrollViewer viewer)
    {
        if ((bool)viewer.GetValue(IsWatchingProperty))
            return;

        viewer.SetValue(IsWatchingProperty, true);
        DependencyPropertyDescriptor
            .FromProperty(FlowDocumentScrollViewer.DocumentProperty, typeof(FlowDocumentScrollViewer))
            .AddValueChanged(viewer, DocumentChanged);
        Apply(viewer.Document);
    }

    private static void Unwatch(FlowDocumentScrollViewer viewer)
    {
        if (!(bool)viewer.GetValue(IsWatchingProperty))
            return;

        DependencyPropertyDescriptor
            .FromProperty(FlowDocumentScrollViewer.DocumentProperty, typeof(FlowDocumentScrollViewer))
            .RemoveValueChanged(viewer, DocumentChanged);
        viewer.ClearValue(IsWatchingProperty);
    }

    private static void OnDocumentChanged(object? sender, EventArgs args)
    {
        if (sender is FlowDocumentScrollViewer viewer)
            Apply(viewer.Document);
    }

    private static void ApplyBlocks(BlockCollection blocks, bool inList)
    {
        var index = 0;
        foreach (var block in blocks)
        {
            ApplyBlock(block, zeroTop: index == 0, zeroBottom: index == blocks.Count - 1, inList);
            index++;
        }
    }

    private static void ApplyBlock(Block block, bool zeroTop, bool zeroBottom, bool inList)
    {
        switch (block)
        {
            case Paragraph paragraph:
                ApplyParagraph(paragraph, zeroTop, zeroBottom, inList);
                break;
            case DocumentList list:
                // Markdown "-" is a hollow circle in MdXaml. Codex uses a filled dot.
                if (list.MarkerStyle == TextMarkerStyle.Circle)
                    list.MarkerStyle = TextMarkerStyle.Disc;
                // The marker hangs to the left of the text by its own width plus MarkerOffset.
                // A 16px pad fits a dot, but "10." is wider and was getting clipped.
                var markerGap = 8d;
                var markerWidth = IsOrderedMarker(list.MarkerStyle) ? OrderedMarkerWidth(list) : 6d;
                list.MarkerOffset = markerGap;
                list.Padding = new Thickness(markerWidth + markerGap + 4, 0, 0, 0);
                list.Margin = new Thickness(0, zeroTop ? 0 : 4, 0, zeroBottom ? 0 : 8);
                foreach (var item in list.ListItems)
                    ApplyBlocks(item.Blocks, inList: true);
                break;
            case Table table:
                table.Margin = new Thickness(0, zeroTop ? 0 : 6, 0, zeroBottom ? 0 : 12);
                break;
            case Section section:
                if (section.Tag as string == "Blockquote")
                    section.Margin = new Thickness(0, zeroTop ? 0 : 4, 0, zeroBottom ? 0 : 10);
                ApplyBlocks(section.Blocks, inList);
                break;
            case BlockUIContainer container:
                ApplyContainer(container, zeroTop, zeroBottom);
                break;
        }
    }

    private static bool IsOrderedMarker(TextMarkerStyle style) => style is
        TextMarkerStyle.Decimal or
        TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin or
        TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;

    private static double OrderedMarkerWidth(DocumentList list)
    {
        var count = Math.Max(1, list.ListItems.Count);
        var sample = list.MarkerStyle switch
        {
            TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman => "viii.",
            TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin => "m.",
            _ => count.ToString(CultureInfo.InvariantCulture) + "."
        };
        var size = list.FontSize;
        if (double.IsNaN(size) || size <= 0) size = 14;
        var family = list.FontFamily ?? new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei");
        var formatted = new FormattedText(
            sample,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size,
            System.Windows.Media.Brushes.Black,
            1.25);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    private static void ApplyParagraph(Paragraph paragraph, bool zeroTop, bool zeroBottom, bool inList)
    {
        var tag = paragraph.Tag as string;
        if (inList && tag is null)
        {
            paragraph.Margin = new Thickness(0, 0, 0, zeroBottom ? 0 : 4);
            paragraph.LineHeight = 22;
            return;
        }

        var isHeading = tag is "Heading1" or "Heading2" or "Heading3" or "Heading4" or "Heading5" or "Heading6";
        var (top, bottom, lineHeight) = tag switch
        {
            "Heading1" or "Heading2" or "Heading3" or "Heading4" or "Heading5" or "Heading6" => (8d, 6d, 22d),
            "CodeBlock" => (4d, 12d, 18d),
            "Note" => (4d, 8d, 22d),
            _ => (0d, 8d, 22d),
        };
        paragraph.Margin = new Thickness(0, zeroTop ? 0 : top, 0, zeroBottom ? 0 : bottom);
        if (!double.IsNaN(lineHeight))
            paragraph.LineHeight = lineHeight;
        if (isHeading)
        {
            paragraph.FontSize = 14;
            paragraph.FontWeight = FontWeights.SemiBold;
        }
    }

    private static void ApplyContainer(BlockUIContainer container, bool zeroTop, bool zeroBottom)
    {
        var tag = container.Tag as string;
        if (tag is "RuleSingle" or "RuleDouble" or "RuleBold" or "RuleBoldWithSingle")
        {
            container.Margin = new Thickness(0, zeroTop ? 0 : 6, 0, zeroBottom ? 0 : 6);
            return;
        }

        if (tag == "CodeBlock")
        {
            container.Margin = new Thickness(0, zeroTop ? 0 : 4, 0, zeroBottom ? 0 : 12);
            return;
        }

        container.Margin = new Thickness(0, zeroTop ? 0 : 4, 0, zeroBottom ? 0 : 10);
    }
}

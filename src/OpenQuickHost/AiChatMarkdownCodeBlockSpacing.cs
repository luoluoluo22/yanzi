using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MdXaml;

namespace OpenQuickHost;

/// <summary>
/// Formats MdXaml's existing AvalonEdit code controls without replacing its
/// Markdown renderer, syntax highlighting, or clipboard behavior.
/// Applied only to AI chat viewers that opt into this attached property.
/// </summary>
public static class AiChatMarkdownCodeBlockSpacing
{
    private const string CodeEditorType = "ICSharpCode.AvalonEdit.TextEditor";
    private const string PaddingResource = "Yanzi.Chat.CodeBlock.Padding";
    private const string MarginResource = "Yanzi.Chat.CodeBlock.Margin";

    internal static readonly Thickness DefaultPadding = new(16, 12, 16, 14);
    internal static readonly Thickness DefaultMargin = new(8, 10, 8, 14);

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AiChatMarkdownCodeBlockSpacing),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyDescriptor? DocumentDescriptor =
        DependencyPropertyDescriptor.FromProperty(
            FlowDocumentScrollViewer.DocumentProperty, typeof(FlowDocumentScrollViewer));

    public static bool GetIsEnabled(DependencyObject target) =>
        (bool)target.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject target, bool enabled) =>
        target.SetValue(IsEnabledProperty, enabled);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not MarkdownScrollViewer viewer)
            return;

        viewer.Loaded -= Viewer_Loaded;
        viewer.Unloaded -= Viewer_Unloaded;
        DocumentDescriptor?.RemoveValueChanged(viewer, DocumentChanged);

        if (args.NewValue is true)
        {
            viewer.Loaded += Viewer_Loaded;
            viewer.Unloaded += Viewer_Unloaded;
            if (viewer.IsLoaded)
                DocumentDescriptor?.AddValueChanged(viewer, DocumentChanged);
            ApplySpacing(viewer);
        }
    }

    private static void Viewer_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not MarkdownScrollViewer viewer)
            return;

        // Recycled chat items may unload and load repeatedly.
        DocumentDescriptor?.RemoveValueChanged(viewer, DocumentChanged);
        DocumentDescriptor?.AddValueChanged(viewer, DocumentChanged);
        ApplySpacing(viewer);
    }

    private static void Viewer_Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is MarkdownScrollViewer viewer)
            DocumentDescriptor?.RemoveValueChanged(viewer, DocumentChanged);
    }

    private static void DocumentChanged(object? sender, EventArgs args)
    {
        if (sender is MarkdownScrollViewer viewer)
            ApplySpacing(viewer);
    }

    public static void ApplySpacing(MarkdownScrollViewer viewer)
    {
        var document = viewer.Document;
        if (document is null)
            return;

        var codePadding = viewer.TryFindResource(PaddingResource) is Thickness padding
            ? padding : DefaultPadding;
        var codeMargin = viewer.TryFindResource(MarginResource) is Thickness margin
            ? margin : DefaultMargin;

        ApplyBlocks(document.Blocks, codePadding, codeMargin);
    }

    private static void ApplyBlocks(BlockCollection blocks, Thickness padding, Thickness margin)
    {
        foreach (Block block in blocks)
        {
            if (block is BlockUIContainer codeBlock &&
                codeBlock.Child is Control codeEditor &&
                string.Equals(codeEditor.GetType().FullName, CodeEditorType, StringComparison.Ordinal))
            {
                codeBlock.Margin = margin;
                codeEditor.Padding = padding;
                codeEditor.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else if (block is Section section)
            {
                ApplyBlocks(section.Blocks, padding, margin);
            }
            else if (block is System.Windows.Documents.List list)
            {
                foreach (ListItem item in list.ListItems)
                    ApplyBlocks(item.Blocks, padding, margin);
            }
        }
    }
}

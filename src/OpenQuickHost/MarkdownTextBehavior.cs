using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OpenQuickHost;

/// <summary>
/// Compatibility adapter for the existing inert HTML renderer.
/// Markdown is rendered by the maintained MdXaml control in MainWindow.xaml;
/// never attempt to parse CommonMark with regexes in a TextBlock.
/// </summary>
public static class MarkdownTextBehavior
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.RegisterAttached(
            "MarkdownText", typeof(string), typeof(MarkdownTextBehavior),
            new PropertyMetadata(string.Empty, OnMarkdownTextChanged));

    public static string GetMarkdownText(DependencyObject obj) =>
        (string)obj.GetValue(MarkdownTextProperty);

    public static void SetMarkdownText(DependencyObject obj, string value) =>
        obj.SetValue(MarkdownTextProperty, value);

    private static void OnMarkdownTextChanged(DependencyObject target,
        DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock textBlock) return;
        textBlock.Inlines.Clear();
        var content = args.NewValue as string;
        if (string.IsNullOrEmpty(content)) return;

        if (!AiChatHtmlContent.TryRender(textBlock, content))
            textBlock.Inlines.Add(new Run(content));
    }

    // HTML DOM text nodes are already escaped and parsed by HtmlAgilityPack.
    // Treat their content as text, not a second, ad-hoc Markdown dialect.
    internal static void ParseAndAddInlineSegment(InlineCollection inlines, string text)
    {
        if (!string.IsNullOrEmpty(text))
            inlines.Add(new Run(text));
    }
}

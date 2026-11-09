using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using DocumentList = System.Windows.Documents.List;
using TextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace OpenQuickHost;

/// <summary>
/// Codex keeps headings at the body size and only makes them bold. Sasabune
/// instead enlarges them and gives them no vertical margin. These distances
/// leave a short break around a same-size heading, and the first and last
/// blocks stay flush with the bubble padding.
/// </summary>
public static class AiChatMarkdownSpacing
{
    // Sasabune insets a code editor by 5px horizontally and 0 vertically, and that
    // padding sits on a scroll viewer that does not reliably move the glyphs.
    // The shell owns a larger inset so the text is not pressed against the panel.
    private static readonly Thickness CodeBlockPadding = new(18, 16, 18, 16);
    private const string CodeBlockChromeTag = "AiChatCodeBlock";
    private const double CodeBlockCornerFallback = 10;
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
        AiChatEmoji.Apply(document);
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
        // Replacing a code editor invalidates a live block enumerator.
        var snapshot = new List<Block>(blocks.Count);
        foreach (var block in blocks)
            snapshot.Add(block);
        for (var index = 0; index < snapshot.Count; index++)
            ApplyBlock(snapshot[index], zeroTop: index == 0, zeroBottom: index == snapshot.Count - 1, inList);
    }

    private static void ApplyBlock(Block block, bool zeroTop, bool zeroBottom, bool inList)
    {
        switch (block)
        {
            case Paragraph paragraph:
                if (!TryUpgradeCodeParagraph(paragraph, zeroTop, zeroBottom))
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
            "CodeBlock" => (12d, 16d, double.NaN),
            "Note" => (4d, 8d, 22d),
            _ => (0d, 8d, 22d),
        };
        paragraph.Margin = new Thickness(0, zeroTop ? 0 : top, 0, zeroBottom ? 0 : bottom);
        if (tag == "CodeBlock")
            paragraph.Padding = CodeBlockPadding;
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
            container.Margin = new Thickness(0, zeroTop ? 0 : 12, 0, zeroBottom ? 0 : 16);
            DressCodeBlock(container);
            return;
        }

        container.Margin = new Thickness(0, zeroTop ? 0 : 4, 0, zeroBottom ? 0 : 10);
    }

    private static CornerRadius CodeBlockCorner()
    {
        if (System.Windows.Application.Current?.TryFindResource("Yanzi.Radius.Lg") is CornerRadius radius)
            return radius;
        return new CornerRadius(CodeBlockCornerFallback);
    }

    private static bool TryUpgradeCodeParagraph(Paragraph paragraph, bool zeroTop, bool zeroBottom)
    {
        if (paragraph.Tag as string != "CodeBlock")
            return false;
        var owner = paragraph.Parent switch
        {
            FlowDocument document => document.Blocks,
            Section section => section.Blocks,
            ListItem item => item.Blocks,
            _ => null
        };
        if (owner is null)
            return false;

        var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n');
        var editor = new TextEditor
        {
            Text = text,
            IsReadOnly = true,
            Tag = GuessCodeLanguage(text)
        };
        var container = new BlockUIContainer(BuildCodeShell(editor))
        {
            Tag = "CodeBlock",
            Margin = new Thickness(0, zeroTop ? 0 : 12, 0, zeroBottom ? 0 : 16)
        };
        owner.InsertAfter(paragraph, container);
        owner.Remove(paragraph);
        return true;
    }

    private static string? GuessCodeLanguage(string code)
    {
        if (code.Contains("def ", StringComparison.Ordinal) ||
            code.Contains("import ", StringComparison.Ordinal) ||
            code.Contains("elif ", StringComparison.Ordinal))
            return "python";
        var start = code.TrimStart();
        if (start.StartsWith('{') || start.StartsWith('['))
            return "json";
        return null;
    }

    private static void DressCodeBlock(BlockUIContainer container)
    {
        if (container.Child is Border { Tag: CodeBlockChromeTag })
            return;
        if (container.Child is not Control editor)
            return;

        container.Child = null;
        container.Child = BuildCodeShell(editor);
    }

    private static Border BuildCodeShell(Control editor)
    {
        var panel = editor.Background;
        editor.Padding = new Thickness(0);
        editor.Margin = new Thickness(0);
        editor.BorderThickness = new Thickness(0);
        editor.Background = System.Windows.Media.Brushes.Transparent;
        if (editor is TextEditor code)
            PrepareCodeEditor(code);

        var radius = CodeBlockCorner();
        var shell = new Border
        {
            Tag = CodeBlockChromeTag,
            CornerRadius = radius,
            Padding = CodeBlockPadding,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            Background = IsInsetBrush(panel) ? panel : CodePanelBrush(editor),
            BorderBrush = CodeEdgeBrush(editor)
        };
        var host = new Grid();
        var copy = CreateCodeCopyButton(editor);
        copy.Visibility = Visibility.Collapsed;
        copy.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        copy.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        System.Windows.Controls.Panel.SetZIndex(copy, 2);
        host.Children.Add(editor);
        host.Children.Add(copy);
        shell.Child = host;
        shell.MouseEnter += (_, _) => copy.Visibility = Visibility.Visible;
        shell.MouseLeave += (_, _) =>
        {
            if (!copy.IsMouseOver)
                copy.Visibility = Visibility.Collapsed;
        };
        shell.SizeChanged += (_, _) => UpdateCodeClip(shell, radius);
        if (shell.IsLoaded)
            UpdateCodeClip(shell, radius);
        else
            shell.Loaded += (_, _) => UpdateCodeClip(shell, radius);
        return shell;
    }

    private static void PrepareCodeEditor(TextEditor editor)
    {
        editor.IsReadOnly = true;
        editor.WordWrap = true;
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        editor.FontFamily = new System.Windows.Media.FontFamily("Consolas, Microsoft YaHei");
        editor.FontSize = 14;
        editor.Padding = new Thickness(0);
        editor.BorderThickness = new Thickness(0);
        editor.Background = System.Windows.Media.Brushes.Transparent;
        ApplyCodeHighlight(editor);
        FitCodeHeight(editor);
        editor.Loaded += (_, _) =>
        {
            ApplyCodeHighlight(editor);
            FitCodeHeight(editor);
        };
        editor.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged)
                FitCodeHeight(editor);
        };
    }

    private static void ApplyCodeHighlight(TextEditor editor)
    {
        var highlighting = ResolveCodeHighlighting(editor.Tag as string, ForegroundColor(editor));
        if (highlighting is not null)
            editor.SetValue(TextEditor.SyntaxHighlightingProperty, highlighting);
    }

    private static void FitCodeHeight(TextEditor editor)
    {
        var view = editor.TextArea.TextView;
        double height = 0;
        if (editor.ActualWidth > 1)
        {
            view.EnsureVisualLines();
            height = view.DocumentHeight;
        }

        if (height < 1)
        {
            var lines = Math.Max(1, editor.Document?.LineCount ?? 1);
            var line = view.DefaultLineHeight;
            if (line < 1 || double.IsNaN(line))
                line = editor.FontSize * 1.45;
            height = lines * line;
        }

        if (double.IsNaN(editor.Height) || Math.Abs(editor.Height - height) > 1)
            editor.Height = height;
    }

    private static Color ForegroundColor(Control editor)
    {
        if (editor.Foreground is SolidColorBrush brush && brush.Color.A > 0 && IsLight(brush.Color))
            return brush.Color;
        if (System.Windows.Application.Current?.TryFindResource("Yanzi.Color.Foreground") is SolidColorBrush theme &&
            theme.Color.A > 0 && IsLight(theme.Color))
            return theme.Color;
        return Color.FromRgb(232, 234, 237);
    }

    private static bool IsLight(Color color) => color.R + color.G + color.B > 360;

    private static bool IsInsetBrush(Brush? brush)
    {
        if (brush is not SolidColorBrush solid || solid.Color.A < 12)
            return false;
        var color = solid.Color;
        if (color.A > 240 && color.R > 220 && color.G > 220 && color.B > 220)
            return false;
        if (color.A > 240 && color.R < 32 && color.G < 32 && color.B < 32)
            return false;
        return true;
    }

    private static Brush CodePanelBrush(Control editor)
    {
        var fore = ForegroundColor(editor);
        return new SolidColorBrush(Color.FromArgb(32, fore.R, fore.G, fore.B));
    }

    private static Brush CodeEdgeBrush(Control editor)
    {
        var fore = ForegroundColor(editor);
        return new SolidColorBrush(Color.FromArgb(56, fore.R, fore.G, fore.B));
    }

    private static IHighlightingDefinition? ResolveCodeHighlighting(string? language, Color foreground)
    {
        var name = NormalizeCodeLanguage(language);
        if (name.Length == 0)
            return null;
        var definition = LookupCodeDefinition(name);
        if (definition is null)
            return null;
        try
        {
            return new MdXaml.Ext.HighlightWrapper(definition, foreground);
        }
        catch
        {
            return definition;
        }
    }

    private static string NormalizeCodeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;
        var token = language.Trim().Trim('`');
        var cut = token.IndexOfAny(new[] { ' ', '\t', '{', ',' });
        if (cut > 0)
            token = token[..cut];
        return token.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static IHighlightingDefinition? LookupCodeDefinition(string name)
    {
        var extension = name switch
        {
            "py" or "python" or "python3" or "py3" => ".py",
            "cs" or "csharp" or "c#" => ".cs",
            "js" or "javascript" or "jsx" => ".js",
            "json" or "jsonc" => ".json",
            "html" or "htm" => ".html",
            "xml" or "xaml" or "csproj" or "svg" => ".xml",
            "css" => ".css",
            "java" => ".java",
            "cpp" or "c++" or "cxx" or "cc" or "hpp" or "h" or "c" => ".cpp",
            "php" => ".php",
            "ps1" or "ps" or "powershell" or "pwsh" => ".ps1",
            "sql" => ".sql",
            "vb" or "vbnet" => ".vb",
            "md" or "markdown" => ".md",
            _ => "." + name
        };
        var manager = HighlightingManager.Instance;
        return manager.GetDefinitionByExtension(extension) ?? NamedCodeDefinition(manager, name);
    }

    private static IHighlightingDefinition? NamedCodeDefinition(HighlightingManager manager, string name) => name switch
    {
        "py" or "python" or "python3" or "py3" => manager.GetDefinition("Python"),
        "cs" or "csharp" or "c#" => manager.GetDefinition("C#"),
        "js" or "javascript" or "jsx" => manager.GetDefinition("JavaScript"),
        "json" or "jsonc" => manager.GetDefinition("Json"),
        "ps1" or "ps" or "powershell" or "pwsh" => manager.GetDefinition("PowerShell"),
        "html" or "htm" => manager.GetDefinition("HTML"),
        "xml" or "xaml" or "svg" => manager.GetDefinition("XML"),
        "cpp" or "c++" or "cxx" or "h" or "c" => manager.GetDefinition("C++"),
        "java" => manager.GetDefinition("Java"),
        "css" => manager.GetDefinition("CSS"),
        "php" => manager.GetDefinition("PHP"),
        "sql" => manager.GetDefinition("TSQL"),
        _ => null
    };

    private static void UpdateCodeClip(Border shell, CornerRadius radius)
    {
        if (shell.ActualWidth <= 0 || shell.ActualHeight <= 0)
            return;
        var corner = Math.Max(radius.TopLeft, radius.BottomRight);
        shell.Clip = new RectangleGeometry(
            new Rect(0, 0, shell.ActualWidth, shell.ActualHeight),
            corner,
            corner);
    }

    private static Button CreateCodeCopyButton(Control editor)
    {
        var button = new Button
        {
            Content = "复制",
            ToolTip = "复制代码",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0),
            MinWidth = 0,
            MinHeight = 0,
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 12,
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Secondary");
        var generation = 0;
        button.Click += (_, _) =>
        {
            try
            {
                ClipboardService.SetText(ReadCodeText(editor));
            }
            catch
            {
                return;
            }

            var token = ++generation;
            button.Content = "已复制";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (token == generation)
                    button.Content = "复制";
            };
            timer.Start();
        };
        return button;
    }

    private static string ReadCodeText(Control editor)
    {
        if (editor is System.Windows.Controls.TextBox box)
            return box.Text ?? string.Empty;
        return editor.GetType().GetProperty("Text")?.GetValue(editor) as string ?? string.Empty;
    }
}

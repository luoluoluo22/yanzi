using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OpenQuickHost
{
    public static class MarkdownTextBehavior
    {
        public static readonly DependencyProperty MarkdownTextProperty =
            DependencyProperty.RegisterAttached(
                "MarkdownText",
                typeof(string),
                typeof(MarkdownTextBehavior),
                new PropertyMetadata(string.Empty, OnMarkdownTextChanged));

        public static string GetMarkdownText(DependencyObject obj) => (string)obj.GetValue(MarkdownTextProperty);
        public static void SetMarkdownText(DependencyObject obj, string value) => obj.SetValue(MarkdownTextProperty, value);

        private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock) return;

            textBlock.Inlines.Clear();
            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text)) return;

            // 逐行解析
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (i > 0)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }

                // 1. 判断是否是分割线 (Horizontal Rule)
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*[-*_]{3,}\s*$"))
                {
                    var border = new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#25FFFFFF")),
                        Margin = new Thickness(0, 6, 0, 6),
                        SnapsToDevicePixels = true
                    };
                    textBlock.Inlines.Add(new InlineUIContainer(border));
                    continue;
                }

                // 2. 判断是否是标题 (Headers)
                var headerMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                if (headerMatch.Success)
                {
                    var level = headerMatch.Groups[1].Value.Length;
                    var content = headerMatch.Groups[2].Value;

                    var span = new Span { FontWeight = FontWeights.Bold };
                    switch (level)
                    {
                        case 1: span.FontSize = 22; break;
                        case 2: span.FontSize = 18; break;
                        case 3: span.FontSize = 16; break;
                        case 4: span.FontSize = 14; break;
                        default: span.FontSize = 13; break;
                    }

                    ParseAndAddInlineSegment(span.Inlines, content);
                    textBlock.Inlines.Add(span);
                    continue;
                }

                // 3. 判断是否是无序列表、有序列表或任务列表 (Lists)
                var listMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(\s*)([*+-]|\d+\.)\s+(.*)$");
                if (listMatch.Success)
                {
                    var indent = listMatch.Groups[1].Value;
                    var marker = listMatch.Groups[2].Value;
                    var content = listMatch.Groups[3].Value;

                    // 添加行首缩进
                    textBlock.Inlines.Add(new Run(indent));

                    // 处理列表前缀与任务列表
                    if (marker == "*" || marker == "-" || marker == "+")
                    {
                        if (content.StartsWith("[ ] "))
                        {
                            textBlock.Inlines.Add(new Run("☐ ") { FontWeight = FontWeights.Medium });
                            content = content.Substring(4);
                        }
                        else if (content.StartsWith("[x] ", System.StringComparison.OrdinalIgnoreCase))
                        {
                            textBlock.Inlines.Add(new Run("☑ ") { Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF60A5FA")), FontWeight = FontWeights.Medium });
                            content = content.Substring(4);
                        }
                        else
                        {
                            textBlock.Inlines.Add(new Run("• ") { FontWeight = FontWeights.Bold });
                        }
                    }
                    else // 数字列表 (e.g. "1.")
                    {
                        textBlock.Inlines.Add(new Run(marker + " ") { FontWeight = FontWeights.Bold });
                    }

                    ParseAndAddInlineSegment(textBlock.Inlines, content);
                    continue;
                }

                // 4. 普通段落行
                ParseAndAddInlineSegment(textBlock.Inlines, line);
            }
        }

        private static void ParseAndAddInlineSegment(InlineCollection inlines, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 匹配超链接、粗斜体、粗体、斜体、删除线、行内代码
            var inlinePattern = @"(\[.*?\]\(.*?\)|__.*?__|__.*?__|\*\*\*.*?\*\*\*|\*\*.*?\*\*|__.*?__|\*.*?\*|_.*?_|`.*?`|~~.*?~~)";
            var parts = System.Text.RegularExpressions.Regex.Split(text, inlinePattern);

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                // A. 超链接
                if (part.StartsWith("[") && part.Contains("](") && part.EndsWith(")"))
                {
                    var linkMatch = System.Text.RegularExpressions.Regex.Match(part, @"^\[(.*?)\]\((.*?)\)$");
                    if (linkMatch.Success)
                    {
                        var linkText = linkMatch.Groups[1].Value;
                        var linkUrl = linkMatch.Groups[2].Value;

                        try
                        {
                            var hyperlink = new Hyperlink(new Run(linkText))
                            {
                                NavigateUri = new System.Uri(linkUrl),
                                Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF60A5FA")),
                                TextDecorations = TextDecorations.Underline,
                                Cursor = System.Windows.Input.Cursors.Hand
                            };
                            hyperlink.RequestNavigate += (sender, args) =>
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = args.Uri.AbsoluteUri,
                                        UseShellExecute = true
                                    });
                                }
                                catch { }
                                args.Handled = true;
                            };
                            inlines.Add(hyperlink);
                        }
                        catch
                        {
                            inlines.Add(new Run(part));
                        }
                        continue;
                    }
                }

                // B. 粗斜体
                if (part.StartsWith("***") && part.EndsWith("***") && part.Length > 6)
                {
                    var clean = part.Substring(3, part.Length - 6);
                    inlines.Add(new Run(clean) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic });
                    continue;
                }

                // C. 粗体
                if (((part.StartsWith("**") && part.EndsWith("**")) || (part.StartsWith("__") && part.EndsWith("__"))) && part.Length > 4)
                {
                    var clean = part.Substring(2, part.Length - 4);
                    inlines.Add(new Run(clean) { FontWeight = FontWeights.Bold });
                    continue;
                }

                // D. 斜体
                if (((part.StartsWith("*") && part.EndsWith("*")) || (part.StartsWith("_") && part.EndsWith("_"))) && part.Length > 2)
                {
                    var clean = part.Substring(1, part.Length - 2);
                    inlines.Add(new Run(clean) { FontStyle = FontStyles.Italic });
                    continue;
                }

                // E. 删除线
                if (part.StartsWith("~~") && part.EndsWith("~~") && part.Length > 4)
                {
                    var clean = part.Substring(2, part.Length - 4);
                    inlines.Add(new Run(clean) { TextDecorations = TextDecorations.Strikethrough });
                    continue;
                }

                // F. 行内代码
                if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                {
                    var clean = part.Substring(1, part.Length - 2);
                    var run = new Run(clean)
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF60A5FA")),
                        Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15FFFFFF"))
                    };
                    inlines.Add(run);
                    continue;
                }

                // G. 普通文本
                inlines.Add(new Run(part));
            }
        }
    }
}

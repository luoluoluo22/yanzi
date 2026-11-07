using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using MdXaml;

internal static class Program
{
    private static int _checks;
    private const string MdText = """
# 一级标题

普通段落有 **加粗**、*斜体*、~~删除线~~ 和 `代码`。

## 二级标题

- 学习新技能
  - 经常复习
- 巩固知识

1. 第一项
2. 第二项

> 这是一段引用，应保持独立段落。

| 语言 | 用途 |
| --- | --- |
| Python | 自动化 |
| C# | WPF |

```python
def hello(name):
    print(f"你好，{name}")
```

[MDN 文档](https://developer.mozilla.org/)
""";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--preview"))
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var viewer = new MarkdownScrollViewer
            {
                Markdown = MdText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 15,
                IsToolBarVisible = false
            };
            var styleNameProperty = viewer.GetType().GetProperty("MarkdownStyleName")!;
            styleNameProperty.SetValue(viewer, "Sasabune");
            var border = new Border
            {
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(10),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1B1B1B")!,
                BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#343434")!,
                BorderThickness = new Thickness(1),
                Child = viewer
            };
            var outer = new ScrollViewer
            {
                Padding = new Thickness(15),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = border
            };
            var window = new Window
            {
                Title = "Yanzi Markdown QA Preview",
                Width = 770, Height = 850,
                Content = outer,
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#0E0E0E")!,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            app.Run(window);
            return 0;
        }
        try
        {
            var clickType=typeof(MarkdownScrollViewer).GetProperty("ClickAction")!.PropertyType;
            Console.WriteLine("CLICK_ACTION_TYPE="+clickType.FullName+" VALUES="+(clickType.IsEnum?string.Join(",",Enum.GetNames(clickType)):"non-enum"));
            var doc = new Markdown().Transform(MdText);
            Assert("MdXaml generates FlowDocument", doc is not null);
            var blocks = Walk(doc!.Blocks).ToArray();
            Assert("headings and paragraphs are distinct blocks",
                blocks.OfType<Paragraph>().Count() >= 4);
            Assert("nested and numbered lists use document lists",
                blocks.OfType<System.Windows.Documents.List>().Any());
            Assert("table uses WPF Table", blocks.OfType<Table>().Any());
            var content = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert("fenced code preserved", content.Contains("def hello") || blocks.OfType<BlockUIContainer>().Any());
            Assert("Chinese preserved in normal paragraphs", content.Contains("学习新技能") && content.Contains("二级标题"));
            Assert("formatting markers not printed", !content.Contains("**加粗**") &&
                !content.Contains("```python"));
            Assert("not all text flattened into one paragraph",
                blocks.OfType<Paragraph>().Count() > 2);
            OpenQuickHost.AiChatMarkdownSpacing.Apply(doc);
            var headings = blocks.OfType<Paragraph>().Where(p => p.Tag is string tag && tag.StartsWith("Heading")).ToArray();
            var h1 = headings.First(p => (string)p.Tag == "Heading1");
            var h2 = headings.First(p => (string)p.Tag == "Heading2");
            var body = blocks.OfType<Paragraph>().First(p => p.Tag is null && new TextRange(p.ContentStart, p.ContentEnd).Text.Contains("普通段落"));
            Assert("opening heading stays flush with the bubble", h1.Margin.Top == 0 && h1.Margin.Bottom >= 6);
            Assert("headings keep the body size and only become semibold",
                h1.FontSize == 14 && h2.FontSize == 14 &&
                h1.FontWeight == FontWeights.SemiBold && h2.FontWeight == FontWeights.SemiBold &&
                h2.Margin.Top >= 8 && h2.Margin.Bottom >= 6);
            Assert("body paragraphs use the chat line height and an 8px break",
                body.Margin.Bottom >= 8 && body.LineHeight == 22);
            var table = blocks.OfType<Table>().First();
            Assert("table keeps space around its grid", table.Margin.Top >= 6 && table.Margin.Bottom >= 12);
            var quote = blocks.OfType<Section>().First(s => (s.Tag as string) == "Blockquote");
            Assert("blockquote keeps space around the quote", quote.Margin.Top >= 4 && quote.Margin.Bottom >= 10);
            var list = blocks.OfType<System.Windows.Documents.List>().First();
            Assert("lists stay closer than paragraphs without colliding", list.Margin.Bottom >= 8);
            Assert("dash lists use Codex filled markers", list.MarkerStyle == TextMarkerStyle.Disc);
            AssertNumberedMarkerIsFullyVisible();
            var view = new MarkdownScrollViewer
            {
                Markdown = MdText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            view.Measure(new Size(520, double.PositiveInfinity));
            Assert("viewer measured in chat width",
                view.DesiredSize.Height > 0 && double.IsFinite(view.DesiredSize.Height));
            var root = FindRoot();
            var xml = XDocument.Load(Path.Combine(root,"src","OpenQuickHost","MainWindow.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var el = xml.Descendants().FirstOrDefault(e =>
                e.Attribute(x+"Name")?.Value == "NormalMarkdownViewer");
            Assert("native MarkdownScrollViewer bound to response",
                el is not null && el.Name.LocalName == "MarkdownScrollViewer" &&
                el.Attribute("Markdown")?.Value == "{Binding FormattedText}");
            Assert("selecting reply text does not remeasure the conversation",
                el?.Attribute("RequestBringIntoView")?.Value == "AiChatMarkdown_RequestBringIntoView" &&
                el.Attribute("PreviewKeyDown")?.Value == "AiChatMarkdown_PreviewKeyDown" &&
                el.Attribute("CommandManager.PreviewExecuted")?.Value == "AiChatMarkdown_PreviewExecuted");
            Assert("image attachments bind a preview instead of the file name",
                xml.Descendants().Any(e =>
                    e.Name.LocalName == "Image" &&
                    e.Attribute("Source")?.Value == "{Binding PreviewImage}"));
            var imagePath = Path.Combine(Path.GetTempPath(), "yanzi-preview-" + Guid.NewGuid().ToString("N") + ".png");
            var textPath = Path.ChangeExtension(imagePath, ".txt");
            try
            {
                File.WriteAllBytes(imagePath, Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
                File.WriteAllText(textPath, "1cd41f56-b9d4-4840-954b-432c1fe");
                var preview = OpenQuickHost.AiChatImagePreview.TryCreate(imagePath);
                Assert("png preview decodes", preview != null);
                Assert("text file does not decode as an image", OpenQuickHost.AiChatImagePreview.TryCreate(textPath) == null);
            }
            finally
            {
                try { File.Delete(imagePath); } catch { }
                try { File.Delete(textPath); } catch { }
            }
            var copy = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AssistantCopyButton");
            var card = new Grid();
            card.Children.Add(new TextBlock { Text = "我是 GPT-6", FontSize = 14, LineHeight = 22 });
            var overlay = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            var hoverButton = new Button { Width = 30, Height = 28, MinWidth = 0, MinHeight = 0, Content = "c", Visibility = Visibility.Collapsed };
            Canvas.SetRight(hoverButton, 0);
            Canvas.SetTop(hoverButton, 0);
            overlay.Children.Add(hoverButton);
            card.Children.Add(overlay);
            card.Measure(new Size(520, double.PositiveInfinity));
            var restingHeight = card.DesiredSize.Height;
            hoverButton.Visibility = Visibility.Visible;
            card.InvalidateMeasure();
            card.Measure(new Size(520, double.PositiveInfinity));
            Assert("hover copy button does not change measured card height", Math.Abs(card.DesiredSize.Height - restingHeight) < 0.1);
            Assert("copy button overlays the card instead of growing it",
                copy?.Parent?.Name.LocalName == "Canvas" &&
                copy.Parent.Attribute("Width")?.Value == "0" &&
                copy.Parent.Attribute("Height")?.Value == "0" &&
                copy.Attribute("MinHeight")?.Value == "0" &&
                copy.Parent.Elements().Any(e => e.Attribute(x + "Name")?.Value == "AssistantHtmlCopyButton"));
            var userActions = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "UserMessageActions");
            var userPopup = userActions?.Parent;
            var userRoot = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "UserMessageRoot");
            var assistantBubble = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AssistantBubble");
            var sendButton = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatSendButton");
            var stopButton = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatStopButton");
            Assert("user message actions float in a popup under the bubble",
                userPopup?.Name.LocalName == "Popup" &&
                userPopup.Attribute("AllowsTransparency")?.Value == "True" &&
                userPopup.Parent?.Attribute(x + "Name")?.Value == "UserMessageRoot" &&
                userActions!.Descendants().Count(e => e.Name.LocalName == "Button") == 3 &&
                userRoot?.Attribute("MouseEnter")?.Value == "UserMessageRoot_MouseEnter" &&
                userRoot.Attribute("MouseLeave")?.Value == "UserMessageRoot_MouseLeave");
            Assert("assistant replies have no card frame",
                assistantBubble?.Attribute("BorderThickness")?.Value == "0" &&
                assistantBubble.Attribute("Background")?.Value == "Transparent" &&
                assistantBubble.Attribute("Style") == null);
            Assert("send is a circular up-arrow button",
                sendButton?.Attribute("Width")?.Value == "36" &&
                sendButton.Attribute("Height")?.Value == "36" &&
                sendButton.Descendants().Any(e => (e.Attribute("Data")?.Value ?? "").Contains("M12,19")));
            Assert("stop is a blue rounded square",
                stopButton?.Attribute("Width")?.Value == "36" &&
                stopButton.Attribute("Height")?.Value == "36" &&
                stopButton.Attribute("Content") == null &&
                (stopButton.Attribute("Background")?.Value ?? "").Contains("ChatUserBubble") &&
                stopButton.Descendants().Any(e => e.Name.LocalName == "Border" && e.Attribute("CornerRadius")?.Value == "8"));
            var addFile = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatAddFileButton");
            var modelButton = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatModelMenuButton");
            var detach = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatDetachButton");
            Assert("add file is a circular plus beside the model picker",
                addFile?.Attribute("Grid.Column")?.Value == "0" &&
                addFile.Attribute("Width")?.Value == "36" &&
                addFile.Attribute("Height")?.Value == "36" &&
                modelButton?.Attribute("Grid.Column")?.Value == "1" &&
                addFile.Descendants().Any(e => e.Name.LocalName == "Border" && e.Attribute("CornerRadius")?.Value == "18") &&
                !addFile.Descendants().Any(e => e.Attribute("Text")?.Value == "添加文件") &&
                !addFile.ToString().Contains("paperclip"));
            Assert("detach sits beside new chat without a label",
                detach?.Parent?.Descendants().Any(e => e.Attribute("Text")?.Value == "新建对话") == true &&
                detach.Descendants().Any(e => (e.Attribute("Data")?.Value ?? "").Contains("open-in-new")) &&
                !xml.Descendants().Any(e => e.Attribute("Text")?.Value == "分离窗口"));
            Assert("chat text uses Microsoft YaHei, not the lighter UI face",
                (el?.Attribute("FontFamily")?.Value ?? "").Contains("Microsoft YaHei") &&
                !(el?.Attribute("FontFamily")?.Value ?? "").Contains("YaHei UI"));
            Assert("chat markdown uses the relaxed block spacing",
                el?.Attribute(XNamespace.Get("clr-namespace:OpenQuickHost") + "AiChatMarkdownSpacing.IsEnabled")?.Value == "True");
            Assert("links use MdXaml safe-browser action", el?.Attribute("ClickAction")?.Value == "SafetyOpenBrowser");
            var widgets = """
                | 工具 | 能做什么 |
                | --- | --- |
                | <icon name="globe" size="sm" inline/> 网络搜索 | 搜索 |
                另外。<Cite refs={"tum247630search0"}/>
                <box border radius="lg" gap={3} padding={3}>
                <row align="center"><icon name="play-circle" size="xl"/> 启动小程序 </row>
                </box>
                {@body const cmds = ["检查状态","启动网易云音乐"]}
                ```text
                <icon name="keep"/>
                ```
                """;
            var cleaned = OpenQuickHost.AiChatGptMarkup.Normalize(widgets);
            Assert("chatgpt icon tag removed", !cleaned.Contains("name=\"globe\"") && !cleaned.Contains("name=\"play-circle\""));
            Assert("chatgpt citation removed", !cleaned.Contains("<Cite", StringComparison.Ordinal));
            Assert("chatgpt layout tags removed", !cleaned.Contains("<box", StringComparison.Ordinal) && !cleaned.Contains("<row", StringComparison.Ordinal));
            Assert("chatgpt widget labels kept", cleaned.Contains("网络搜索") && cleaned.Contains("启动小程序"));
            Assert("chatgpt suggested commands become a list", cleaned.Contains("- 检查状态") && cleaned.Contains("- 启动网易云音乐"));
            Assert("fenced example keeps its tag", cleaned.Contains("<icon name=\"keep\"/>"));
            var commands = """
                {@body const cmds = ["帮我开发一个新的燕子小程序"]}
                选择一个操作，我来执行： {#each cmds as cmd}
                ```
                pressable onClick={() => GenUI.issueNewTum(cmd)} padding={2} radius="md"
                background="surface-secondary"
                ```
                ```
                {cmd}
                ```
                ```
                /pressable
                ```
                {/each}
                注意：需要实际检查才能确认状态。
                ```text
                def keep():
                    return 1
                ```
                """;
            var commandsCleaned = OpenQuickHost.AiChatGptMarkup.Normalize(commands);
            Assert("command template markers removed",
                !commandsCleaned.Contains("{#each") && !commandsCleaned.Contains("{/each") &&
                !commandsCleaned.Contains("pressable") && !commandsCleaned.Contains("{cmd}") &&
                !commandsCleaned.Contains("GenUI") && !commandsCleaned.Contains("onClick"));
            Assert("command template keeps the suggestion and the note",
                commandsCleaned.Contains("- 帮我开发一个新的燕子小程序") &&
                commandsCleaned.Contains("选择一个操作，我来执行：") &&
                commandsCleaned.Contains("注意：需要实际检查才能确认状态。") &&
                commandsCleaned.Contains("def keep():"));
            var labeled = """
                {#each cmds as cmd}
                <pressable onClick={() => GenUI.issueNewTum(cmd)} padding={2} radius="md" background="surface-secondary">
                检查所有小程序运行状态
                </pressable>
                {/each}
                """;
            var labeledCleaned = OpenQuickHost.AiChatGptMarkup.Normalize(labeled);
            Assert("pressable label kept", labeledCleaned.Contains("检查所有小程序运行状态") &&
                !labeledCleaned.Contains("pressable") && !labeledCleaned.Contains("{#each"));
            var broken = """
                选择一个操作，我来执行： {#each cmds as cmd}
                pressable onClick={() => GenUI.issueNewTum(cmd)} padding={2} radius="md"
                background="surface-secondary"
                {cmd}
                /pressable
                {/each}
                注意：保留这段说明。
                """;
            var brokenCleaned = OpenQuickHost.AiChatGptMarkup.Normalize(broken);
            Assert("unfenced pressable shell removed",
                brokenCleaned.Contains("选择一个操作，我来执行：") &&
                brokenCleaned.Contains("注意：保留这段说明。") &&
                !brokenCleaned.Contains("pressable") && !brokenCleaned.Contains("{cmd}") &&
                !brokenCleaned.Contains("background="));
            Assert("dark adaptive MdXaml theme used",
                el?.Attribute("MarkdownStyleName")?.Value == "Sasabune" ||
                xml.Descendants().Any(e => (e.Attribute("BasedOn")?.Value ?? "").Contains("MarkdownStyle.Sasabune")));
            Assert("internal scrolling disabled",
                el?.Attribute("VerticalScrollBarVisibility")?.Value == "Disabled");
            Assert("standalone HTML fallback preserved",
                xml.Descendants().Any(e => e.Attribute(x+"Name")?.Value == "NormalHtmlTextBlock"));
            Assert("old TextBlock renderer removed",
                !xml.Descendants().Any(e => e.Attribute(x+"Name")?.Value == "NormalTextBlock"));
            var adapter = File.ReadAllText(Path.Combine(root,"src","OpenQuickHost","MarkdownTextBehavior.cs"));
            Assert("hand-written regex Markdown parser eliminated",
                !adapter.Contains("Regex.Split") && !adapter.Contains("headerMatch"));
            Console.WriteLine("AI_CHAT_MDXAML_QA_OK checks="+_checks);
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("AI_CHAT_MDXAML_QA_FAILED "+error);
            return 1;
        }
    }

    private static void AssertNumberedMarkerIsFullyVisible()
    {
        var family = new FontFamily("Segoe UI, Microsoft YaHei");
        var doc = new FlowDocument
        {
            FontFamily = family,
            FontSize = 14,
            PagePadding = new Thickness(0),
            PageWidth = 420,
            Background = Brushes.White
        };
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = TextMarkerStyle.Decimal,
            FontSize = 14,
            FontFamily = family
        };
        for (var n = 0; n < 10; n++)
            list.ListItems.Add(new ListItem(new Paragraph(new Run("项")) { Margin = new Thickness(0), LineHeight = 22 }));
        doc.Blocks.Add(list);
        OpenQuickHost.AiChatMarkdownSpacing.Apply(doc);
        const int width = 420, height = 280;
        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            Width = width,
            Height = height,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White,
            IsToolBarVisible = false
        };
        viewer.Measure(new Size(width, height));
        viewer.Arrange(new Rect(0, 0, width, height));
        viewer.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewer);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        var limit = Math.Max(1, (int)Math.Floor(list.Padding.Left) - 2);
        var minX = int.MaxValue;
        var maxX = -1;
        var dark = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < limit; x++)
            {
                var i = y * stride + x * 4;
                if (pixels[i + 3] > 48 && pixels[i] < 90 && pixels[i + 1] < 90 && pixels[i + 2] < 90)
                {
                    dark++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
        }
        var sample = new FormattedText(
            "10.",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14,
            Brushes.Black,
            1.25);
        var ink = maxX >= minX ? maxX - minX + 1 : 0;
        Assert($"numbered marker is not clipped on the left (ink={ink}, min={minX}, sample={sample.Width:0.#}, dark={dark})",
            dark > 8 && minX >= 1 && ink >= sample.Width * 0.72);
    }

    private static IEnumerable<Block> Walk(BlockCollection items)
    {
        foreach(var b in items)
        {
            yield return b;
            if(b is Section section)
                foreach(var nested in Walk(section.Blocks)) yield return nested;
            if(b is System.Windows.Documents.List list)
                foreach(var item in list.ListItems)
                    foreach(var nested in Walk(item.Blocks)) yield return nested;
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while(dir != null && !File.Exists(Path.Combine(dir.FullName,"AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("AGENTS.md missing");
    }

    private static void Assert(string name, bool ok)
    {
        if(!ok) throw new InvalidOperationException(name);
        Console.WriteLine("PASS "+name);
        _checks++;
    }
}
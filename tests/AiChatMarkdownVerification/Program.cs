using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using MdXaml;
using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Button = System.Windows.Controls.Button;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

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
            if (Application.Current is null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
            var pythonHost = blocks.OfType<BlockUIContainer>().First(container =>
                EditorText(container).Contains("def hello"));
            var pythonShell = pythonHost.Child as Border;
            var pythonEditor = FindNamed(pythonShell, "TextEditor");
            var highlighting = pythonEditor?.GetType().GetProperty("SyntaxHighlighting")?.GetValue(pythonEditor);
            var highlightingName = highlighting?.GetType().GetProperty("Name")?.GetValue(highlighting) as string;
            var pythonCopy = pythonShell is null ? null : FindButton(pythonShell, "复制");
            Assert("python fence is colored instead of one plain color",
                highlighting is not null && highlightingName is not null && highlightingName.Contains("Python"));
            Assert("python fence keeps air inside a rounded block",
                pythonShell is not null &&
                pythonShell.Padding.Left >= 16 && pythonShell.Padding.Top >= 14 &&
                pythonShell.Padding.Bottom >= 14 && pythonShell.Padding.Right >= 16 &&
                pythonShell.CornerRadius.TopLeft == 10);
            Assert("copy stays hidden until the pointer is over the code",
                pythonCopy is not null && pythonCopy.Visibility == Visibility.Collapsed &&
                pythonCopy.HorizontalAlignment == HorizontalAlignment.Right);
            RaiseMouseEnter(pythonShell!);
            Assert("hovering the code shows its copy button", pythonCopy!.Visibility == Visibility.Visible);
            var codeDocument = new FlowDocument();
            codeDocument.Blocks.Add(new Paragraph(new Run("说明")));
            var codeEditor = new System.Windows.Controls.TextBox { Text = "print(1)" };
            var codeBlock = new BlockUIContainer(codeEditor) { Tag = "CodeBlock" };
            codeDocument.Blocks.Add(codeBlock);
            var plainCode = new Paragraph(new Run("{\n  \"ok\": true\n}")) { Tag = "CodeBlock" };
            codeDocument.Blocks.Add(plainCode);
            codeDocument.Blocks.Add(new Paragraph(new Run("结束")));
            OpenQuickHost.AiChatMarkdownSpacing.Apply(codeDocument);
            var codeShell = codeBlock.Child as Border;
            var copyButtons = codeShell is null ? 0 : CountLogical<Button>(codeShell, b => b.Content as string == "复制");
            var copyButton = codeShell is null ? null : FindButton(codeShell, "复制");
            codeShell?.Measure(new Size(420, 240));
            codeShell?.Arrange(new Rect(0, 0, 420, 180));
            codeShell?.UpdateLayout();
            var clip = codeShell?.Clip as RectangleGeometry;
            var upgraded = codeDocument.Blocks.OfType<BlockUIContainer>().Skip(1).First();
            Assert("fenced code keeps space around the block and inside it",
                codeBlock.Margin.Top >= 12 && codeBlock.Margin.Bottom >= 16 &&
                codeShell is not null &&
                codeShell.Padding.Left >= 16 && codeShell.Padding.Top >= 14 &&
                codeShell.Padding.Bottom >= 14 && codeShell.Padding.Right >= 16 &&
                codeEditor.Padding.Left == 0 &&
                upgraded.Margin.Top >= 12 && EditorText(upgraded).Contains("\"ok\""));
            Assert("fenced code is rounded and has its own copy button",
                codeShell is not null &&
                codeShell.CornerRadius.TopLeft == 10 && codeShell.CornerRadius.BottomRight == 10 &&
                copyButtons == 1 &&
                copyButton is not null && copyButton.Visibility == Visibility.Collapsed &&
                clip is not null && Math.Abs(clip.RadiusX - 10) < 0.1 &&
                LogicalTreeHelper.GetParent(codeEditor) is not BlockUIContainer);
            RaiseMouseEnter(codeShell!);
            Assert("hovering a code block reveals the copy button on its right",
                copyButton!.Visibility == Visibility.Visible &&
                copyButton.HorizontalAlignment == HorizontalAlignment.Right);
            OpenQuickHost.AiChatMarkdownSpacing.Apply(codeDocument);
            Assert("dressing a code block twice does not nest another copy button",
                codeBlock.Child is Border &&
                codeDocument.Blocks.OfType<BlockUIContainer>().Count() == 2 &&
                CountLogical<Button>(codeShell!, b => b.Content as string is "复制" or "已复制") == 1);
            AssertStyledPythonFenceKeepsColor();
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
            var newProject = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "AiChatNewProjectButton");
            Assert("new project sits beside new chat",
                newProject?.Attribute("Click")?.Value == "AiChatNewProjectButton_Click" &&
                newProject.Parent == detach?.Parent &&
                newProject.Descendants().Any(e => e.Attribute("Text")?.Value == "新建项目"));
            var topicEdit = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "TopicEditButton");
            var historyRow = new Grid();
            historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            historyRow.Children.Add(new TextBlock { Text = "新对话", FontSize = 14 });
            var historyTime = new TextBlock { Text = "刚刚", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(historyTime, 1);
            var historyOverlay = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var historyEdit = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Visibility = Visibility.Collapsed };
            Canvas.SetRight(historyEdit, 0);
            Canvas.SetTop(historyEdit, -14);
            historyOverlay.Children.Add(historyEdit);
            Grid.SetColumn(historyOverlay, 1);
            historyRow.Children.Add(historyTime);
            historyRow.Children.Add(historyOverlay);
            historyRow.Measure(new Size(260, double.PositiveInfinity));
            var historyHeight = historyRow.DesiredSize.Height;
            historyEdit.Visibility = Visibility.Visible;
            historyRow.InvalidateMeasure();
            historyRow.Measure(new Size(260, double.PositiveInfinity));
            var projectTopic = new OpenQuickHost.AiChatTopic("新项目");
            projectTopic.SetProjectFiles(new[] { @"C:\Users\me\Desktop\readme.txt", @"C:\Users\me\Desktop\readme.txt", @"C:\Users\me\Desktop\main.cs" });
            Assert("history edit button overlays the card instead of growing it",
                topicEdit?.Parent?.Name.LocalName == "Canvas" &&
                topicEdit.Parent.Attribute("Width")?.Value == "0" &&
                topicEdit.Parent.Attribute("Height")?.Value == "0" &&
                topicEdit.Attribute("MinHeight")?.Value == "0" &&
                Math.Abs(historyRow.DesiredSize.Height - historyHeight) < 0.1);
            Assert("project file paths stay on the topic",
                projectTopic.ProjectFiles.Count == 2 &&
                projectTopic.ProjectFilesLabel.Contains("readme.txt") &&
                projectTopic.ProjectFilesLabel.Contains("main.cs") &&
                projectTopic.Title == "新项目" &&
                projectTopic.ProjectFilesVisibility == Visibility.Visible);
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
            AssertEmojiSegments();
            AssertEmojiRunsInMarkdown();
            AssertEmojiRunsKeepBoldText();
            AssertEmojiTextBlockKeepsCaret();
            AssertEmojiRendersInColor();
            var userBubble = xml.Descendants().FirstOrDefault(e => e.Attribute(x + "Name")?.Value == "UserMessageBubble");
            Assert("user bubble renders emoji characters",
                userBubble?.Descendants().Any(e =>
                    e.Attribute(XNamespace.Get("clr-namespace:OpenQuickHost") + "AiChatEmoji.Text")?.Value == "{Binding Text}") == true);
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

    private static void AssertEmojiSegments()
    {
        var greeting = OpenQuickHost.AiChatEmoji.Split("你好！我是 ChatGPT。😊\n很高兴认识你！🙂");
        Assert("greeting keeps Chinese and splits the trailing emoji",
            greeting.Count == 4 &&
            greeting[0].Text.StartsWith("你好") && !greeting[0].IsEmoji &&
            greeting[1].Text == "😊" && greeting[1].IsEmoji &&
            greeting[2].Text.Contains("很高兴认识你") && !greeting[2].IsEmoji &&
            greeting[3].Text == "🙂" && greeting[3].IsEmoji);
        var family = OpenQuickHost.AiChatEmoji.Split("一家👨‍👩‍👧人");
        Assert("zwj family stays one emoji cluster",
            family.Count == 3 && family[1].IsEmoji && family[1].Text == "👨‍👩‍👧" &&
            !family[0].IsEmoji && family[0].Text == "一家");
        var tone = OpenQuickHost.AiChatEmoji.Split("好👍🏻");
        Assert("skin tone stays with the emoji", tone.Count == 2 && tone[1].IsEmoji && tone[1].Text == "👍🏻");
        var flag = OpenQuickHost.AiChatEmoji.Split("中国🇨🇳");
        Assert("flag pair stays one emoji", flag.Count == 2 && flag[1].Text == "🇨🇳" && flag[1].IsEmoji);
        var keycap = OpenQuickHost.AiChatEmoji.Split("第1\uFE0F\u20E3名");
        Assert("keycap sequence stays one emoji", keycap.Count == 3 && keycap[1].IsEmoji && keycap[1].Text == "1\uFE0F\u20E3");
        var heart = OpenQuickHost.AiChatEmoji.Split("喜欢❤\uFE0F");
        Assert("variation selector stays with the heart", heart.Count == 2 && heart[1].IsEmoji && heart[1].Text == "❤\uFE0F");
        var plain = OpenQuickHost.AiChatEmoji.Split("模型。© 2026");
        Assert("ordinary text and a copyright sign stay as text",
            plain.Count == 1 && !plain[0].IsEmoji && !OpenQuickHost.AiChatEmoji.ContainsEmoji(plain[0].Text));
        Assert("empty text has no segments", OpenQuickHost.AiChatEmoji.Split("") .Count == 0);
    }

    private static void AssertEmojiRunsInMarkdown()
    {
        var sample = "你好！我是 ChatGPT，由 OpenAI 训练的 GPT-6 人工智能模型。😊\n\n很高兴认识你！🙂";
        var doc = new Markdown().Transform(sample);
        OpenQuickHost.AiChatMarkdownSpacing.Apply(doc);
        var emoji = AllEmoji(doc).Select(item => item.Text).ToList();
        Assert("markdown emoji is drawn as a color glyph",
            emoji.Contains("😊") && emoji.Contains("🙂"));
        Assert("markdown Chinese stays in a normal run",
            AllRuns(doc).Any(run => run.Text.Contains("你好")));
        var copied = OpenQuickHost.AiChatEmoji.ReadPlainText(doc.ContentStart, doc.ContentEnd);
        Assert("copied markdown still contains the emoji characters",
            copied.Contains("你好") && copied.Contains("😊") && copied.Contains("很高兴认识你"));
        var before = AllEmoji(doc).Count();
        OpenQuickHost.AiChatMarkdownSpacing.Apply(doc);
        Assert("emoji substitution is stable when the document is restyled",
            AllEmoji(doc).Count() == before &&
            OpenQuickHost.AiChatEmoji.ReadPlainText(doc.ContentStart, doc.ContentEnd).Contains("😊"));
    }

    private static void AssertEmojiRunsKeepBoldText()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph(new Bold(new Run("加粗😊文字"))));
        OpenQuickHost.AiChatEmoji.Apply(doc);
        var runs = AllRuns(doc).ToList();
        var emoji = AllEmoji(doc).ToList();
        Assert("bold sentence keeps its weight around the emoji",
            runs.Any(run => run.Text.Contains("加粗") && run.FontWeight == FontWeights.Bold) &&
            runs.Any(run => run.Text.Contains("文字") && run.FontWeight == FontWeights.Bold) &&
            emoji.Any(item => item.Text == "😊") &&
            OpenQuickHost.AiChatEmoji.ReadPlainText(doc.ContentStart, doc.ContentEnd).Contains("加粗😊文字"));
    }

    private static void AssertEmojiTextBlockKeepsCaret()
    {
        var block = new TextBlock { FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"), FontSize = 14 };
        var caret = new InlineUIContainer(new Border { Width = 8, Height = 14 });
        block.Inlines.Add(caret);
        OpenQuickHost.AiChatEmoji.SetText(block, "你");
        var first = block.Inlines.OfType<Run>().Single();
        OpenQuickHost.AiChatEmoji.SetText(block, "你好");
        Assert("plain streaming updates the same run",
            ReferenceEquals(first, block.Inlines.OfType<Run>().Single()) && first.Text == "你好" &&
            block.Inlines.LastInline == caret);
        OpenQuickHost.AiChatEmoji.SetText(block, "你好😊");
        var drawn = block.Inlines.OfType<Emoji.Wpf.EmojiInline>().Single();
        Assert("streaming emoji is a color glyph and the caret stays",
            block.Inlines.LastInline == caret &&
            string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text)) == "你好" &&
            drawn.Text == "😊" && drawn.Child is System.Windows.Controls.Image);
    }

    private static void AssertEmojiRendersInColor()
    {
        if (Application.Current is null)
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var yellow = RenderEmoji("yanzi-emoji-chat.png");
        Console.WriteLine("EMOJI_YELLOW chat=" + yellow);
        Assert("emoji glyphs render in color", yellow > 40);
    }

    private static int RenderEmoji(string fileName)
    {
        var block = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"),
            FontSize = 48,
            Foreground = Brushes.White,
            Background = Brushes.Black,
            Margin = new Thickness(12)
        };
        var window = new Window
        {
            Width = 180,
            Height = 140,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 80,
            Top = 80,
            Background = Brushes.Black,
            Content = block,
            Topmost = true
        };
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
        OpenQuickHost.AiChatEmoji.SetText(block, "😊");
        window.Show();
        window.UpdateLayout();
        Pump();
        var origin = block.PointToScreen(new Point(0, 0));
        var source = PresentationSource.FromVisual(block);
        var dpi = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var width = Math.Max(16, (int)Math.Ceiling(block.ActualWidth * dpi.M11));
        var height = Math.Max(16, (int)Math.Ceiling(block.ActualHeight * dpi.M22));
        using var captured = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(captured))
            graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, new System.Drawing.Size(width, height));
        var yellow = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = captured.GetPixel(x, y);
                if (pixel.R > 200 && pixel.G > 160 && pixel.B < 120 && pixel.G > pixel.B + 50)
                    yellow++;
            }
        }
        var path = Path.Combine(Path.GetTempPath(), fileName);
        captured.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        window.Close();
        Console.WriteLine("EMOJI_PREVIEW=" + path + " yellow=" + yellow);
        return yellow;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(state =>
        {
            ((DispatcherFrame)state!).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<Run> AllRuns(FlowDocument document)
    {
        foreach (var block in Walk(document.Blocks))
        {
            if (block is Paragraph paragraph)
            {
                foreach (var run in RunsIn(paragraph.Inlines))
                    yield return run;
            }
            else if (block is Table table)
            {
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var nested in Walk(cell.Blocks).OfType<Paragraph>())
                                foreach (var run in RunsIn(nested.Inlines))
                                    yield return run;
            }
        }
    }

    private static IEnumerable<Emoji.Wpf.EmojiInline> AllEmoji(FlowDocument document)
    {
        foreach (var block in Walk(document.Blocks))
        {
            if (block is Paragraph paragraph)
            {
                foreach (var emoji in EmojiIn(paragraph.Inlines))
                    yield return emoji;
            }
        }
    }

    private static IEnumerable<Emoji.Wpf.EmojiInline> EmojiIn(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Emoji.Wpf.EmojiInline emoji:
                    yield return emoji;
                    break;
                case Span span:
                    foreach (var emoji in EmojiIn(span.Inlines))
                        yield return emoji;
                    break;
            }
        }
    }

    private static IEnumerable<Run> RunsIn(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    yield return run;
                    break;
                case Span span:
                    foreach (var run in RunsIn(span.Inlines))
                        yield return run;
                    break;
            }
        }
    }

    private static void AssertStyledPythonFenceKeepsColor()
    {
        var viewer = new MarkdownScrollViewer
        {
            Foreground = Brushes.White,
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#1B1B1B")!,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"),
            FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            Width = 560
        };
        viewer.GetType().GetProperty("MarkdownStyleName")!.SetValue(viewer, "Sasabune");
        OpenQuickHost.AiChatMarkdownSpacing.SetIsEnabled(viewer, true);
        viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        viewer.Markdown = "```python\n# note\ndef hello():\n    return 1\n```\n";
        OpenQuickHost.AiChatMarkdownSpacing.Apply(viewer.Document);
        viewer.Measure(new Size(560, 800));
        var arrangedHeight = Math.Max(1, viewer.DesiredSize.Height);
        viewer.Arrange(new Rect(0, 0, 560, arrangedHeight));
        viewer.UpdateLayout();
        var editor = FindNamed(viewer, "TextEditor") ?? FindNamed(viewer.Document, "TextEditor");
        var highlighting = editor?.GetType().GetProperty("SyntaxHighlighting")?.GetValue(editor);
        var name = highlighting?.GetType().GetProperty("Name")?.GetValue(highlighting) as string;
        var shell = FindShell(viewer) ?? (viewer.Document is null ? null : FindShell(viewer.Document));
        var button = shell is null ? null : FindButton(shell, "复制");
        Assert("styled python fence keeps a python highlighter",
            name is not null && name.Contains("Python"));
        Assert("styled code shell keeps inner padding and a hidden right-side copy button",
            shell is not null && shell.Padding.Left >= 16 && shell.Padding.Top >= 14 &&
            shell.Padding.Bottom >= 14 &&
            button is not null && button.Visibility == Visibility.Collapsed &&
            button.HorizontalAlignment == HorizontalAlignment.Right);
        var width = Math.Max(1, (int)Math.Ceiling(viewer.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(viewer.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewer);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var blue = 0;
        var green = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            if (b > r + 25 && b > g + 10 && b > 90) blue++;
            if (g > r + 25 && g > b + 10 && g > 90) green++;
        }
        var path = Path.Combine(Path.GetTempPath(), "yanzi-code-block.png");
        using (var stream = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }
        Console.WriteLine("CODE_BLOCK_PNG=" + path + " blue=" + blue + " green=" + green + " size=" + width + "x" + height);
        Assert("python keywords and comments render in different colors", blue > 12 && green > 8);
    }

    private static string EditorText(DependencyObject root)
    {
        var editor = root.GetType().Name == "TextEditor" ? root : FindNamed(root, "TextEditor");
        return editor?.GetType().GetProperty("Text")?.GetValue(editor) as string ?? string.Empty;
    }

    private static DependencyObject? FindNamed(DependencyObject? root, string typeName)
    {
        if (root is null)
            return null;
        if (root.GetType().Name == typeName)
            return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dep)
                continue;
            var found = FindNamed(dep, typeName);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        if (root is Button button && button.Content as string == content)
            return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dep)
                continue;
            var found = FindButton(dep, content);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static Border? FindShell(DependencyObject root)
    {
        if (root is Border border && border.Tag as string == "AiChatCodeBlock")
            return border;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dep)
                continue;
            var found = FindShell(dep);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static void RaiseMouseEnter(UIElement element)
    {
        element.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = Mouse.MouseEnterEvent,
            Source = element
        });
    }

    private static int CountLogical<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var count = 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dep)
                continue;
            if (dep is T typed && match(typed))
                count++;
            count += CountLogical(dep, match);
        }
        return count;
    }

    private static void Assert(string name, bool ok)
    {
        if(!ok) throw new InvalidOperationException(name);
        Console.WriteLine("PASS "+name);
        _checks++;
    }
}
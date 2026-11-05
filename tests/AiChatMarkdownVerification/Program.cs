using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            Assert("links use MdXaml safe-browser action", el?.Attribute("ClickAction")?.Value == "SafetyOpenBrowser");
            Assert("dark adaptive MdXaml theme used", el?.Attribute("MarkdownStyleName")?.Value == "Sasabune");
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
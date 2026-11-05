using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Markup;
using System.Xml.Linq;
using OpenQuickHost;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        try
        {
            var message = new AiChatMessage(false, string.Empty);
            var raised = new HashSet<string>(StringComparer.Ordinal);
            message.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null) raised.Add(e.PropertyName);
            };

            Assert("idle reply has no animation",
                !message.IsAwaitingFirstToken && !message.IsReceivingTokens &&
                message.WaitingIndicatorVisibility == Visibility.Collapsed);
            message.IsStreaming = true;
            Assert("first-token wait is inside reply",
                message.IsAwaitingFirstToken && !message.IsReceivingTokens &&
                message.WaitingIndicatorVisibility == Visibility.Visible &&
                message.StreamingTextVisibility == Visibility.Collapsed);
            Assert("initial visibility notifications",
                raised.Contains(nameof(AiChatMessage.WaitingIndicatorVisibility)) &&
                raised.Contains(nameof(AiChatMessage.StreamingTextVisibility)));
            raised.Clear();

            message.Text = "你好";
            Assert("first token swaps dots for inline caret",
                !message.IsAwaitingFirstToken && message.IsReceivingTokens &&
                message.WaitingIndicatorVisibility == Visibility.Collapsed &&
                message.StreamingTextVisibility == Visibility.Visible);
            Assert("first token notifies both layouts",
                raised.Contains(nameof(AiChatMessage.WaitingIndicatorVisibility)) &&
                raised.Contains(nameof(AiChatMessage.StreamingTextVisibility)) &&
                raised.Contains(nameof(AiChatMessage.IsReceivingTokens)));
            raised.Clear();

            message.Text = "你好，世界";
            Assert("streamed text stays in one message",
                message.IsReceivingTokens && !message.IsAwaitingFirstToken);
            message.IsStreaming = false;
            Assert("successful completion stops all indicators",
                !message.IsAwaitingFirstToken && !message.IsReceivingTokens &&
                message.WaitingIndicatorVisibility == Visibility.Collapsed &&
                message.StreamingTextVisibility == Visibility.Collapsed);
            Assert("completion re-enables formatted text", message.FormattedText == message.Text);

            var canceled = new AiChatMessage(false, string.Empty) { IsStreaming = true };
            canceled.Text = "已停止";
            canceled.IsStreaming = false;
            Assert("cancellation removes activity", canceled.WaitingIndicatorVisibility == Visibility.Collapsed &&
                canceled.StreamingTextVisibility == Visibility.Collapsed);

            var toolMessage = new AiChatMessage(false, "调用工具") { IsToolCall = true };
            toolMessage.IsToolRunning = true;
            Assert("tool execution uses the message header instead of the footer",
                toolMessage.ToolRunningVisibility == Visibility.Visible);
            toolMessage.IsToolRunning = false;
            Assert("tool execution indicator disappears after result",
                toolMessage.ToolRunningVisibility == Visibility.Collapsed);

            var otherTopic = new AiChatMessage(false, string.Empty) { IsStreaming = true };
            Assert("topic state is message-owned, not window-global",
                otherTopic.IsAwaitingFirstToken && !message.IsAwaitingFirstToken);

            var user = new AiChatMessage(true, "hello") { IsStreaming = true };
            Assert("user messages never animate", !user.IsAwaitingFirstToken && !user.IsReceivingTokens);

            var root = FindRoot();
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var chatXaml = XDocument.Load(System.IO.Path.Combine(root, "src", "OpenQuickHost", "MainWindow.xaml"));
            XElement? FindName(string value) => chatXaml.Descendants()
                .FirstOrDefault(e => e.Attribute(x + "Name")?.Value == value);
            var list = FindName("AiChatMessagesList");
            var waiting = FindName("AssistantWaitingIndicator");
            var streaming = FindName("AssistantStreamingTextBlock");
            Assert("assistant bubble has embedded waiting state",
                list is not null && waiting is not null &&
                waiting.Attribute("Visibility")?.Value == "{Binding WaitingIndicatorVisibility}");
            Assert("streaming block contains bound Run and inline caret",
                streaming is not null && streaming.Elements().Any(e =>
                e.Name.LocalName == "Run" && e.Attribute("Text")?.Value == "{Binding Text}") &&
                streaming.Descendants().Any(e => e.Name.LocalName == "InlineUIContainer" &&
                    e.Descendants().Any(d => d.Attribute("Style")?.Value ==
                        "{DynamicResource Yanzi.Chat.GenerationCaret}")));
            Assert("standalone footer and rotating spinner are absent",
                !chatXaml.Descendants().Any(e =>
                    e.Attribute("Visibility")?.Value?.Contains("IsAiChatRequestInFlight") == true &&
                    e.Attribute("Margin")?.Value == "18,2,14,8"));
            Assert("toolcall header has its own inline activity state",
                chatXaml.Descendants().Any(e =>
                    e.Attribute("Visibility")?.Value == "{Binding ToolRunningVisibility}" &&
                    e.Descendants().Any(child => child.Attribute("Style")?.Value ==
                        "{DynamicResource Yanzi.Chat.ToolRunningDot}")));
            Assert("waiting indicator is part of assistant message template",
                waiting is not null &&
                waiting.Ancestors().Any(e => e.Attribute(x + "Name")?.Value == "AssistantBubble"));

            var themeXaml = XDocument.Load(System.IO.Path.Combine(root, "src", "Yanzi.UI.Wpf", "Themes", "Controls.xaml"));
            var dots = new[] { "First", "Second", "Third" };
            foreach (var dot in dots)
            {
                var style = themeXaml.Descendants().FirstOrDefault(e =>
                    e.Attribute(x + "Key")?.Value == "Yanzi.Chat.GenerationDot." + dot);
                Assert("shared animation style " + dot, style is not null &&
                    style.Descendants().Any(e => e.Name.LocalName == "StopStoryboard") &&
                    style.Descendants().Any(e => e.Name.LocalName == "BeginStoryboard"));
            }
            var toolDot = themeXaml.Descendants().FirstOrDefault(e =>
                e.Attribute(x + "Key")?.Value == "Yanzi.Chat.ToolRunningDot");
            Assert("tool activity style also has a stop action",
                toolDot is not null && toolDot.Descendants().Any(e => e.Name.LocalName == "StopStoryboard"));
            var caret = themeXaml.Descendants().FirstOrDefault(e =>
                e.Attribute(x + "Key")?.Value == "Yanzi.Chat.GenerationCaret");
            Assert("shared caret stops on completion", caret is not null &&
                caret.Descendants().Any(e => e.Name.LocalName == "StopStoryboard"));

            var styles = themeXaml.Root!.Elements().Where(e =>
                e.Attribute(x + "Key")?.Value?.StartsWith("Yanzi.Chat.Generation", StringComparison.Ordinal) == true).ToArray();
            var mini = new XElement(XName.Get("ResourceDictionary",
                 "http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
                new XAttribute(XNamespace.Xmlns + "x", x), styles);
            var dict = (ResourceDictionary)XamlReader.Parse(mini.ToString());
            var panel = new StackPanel { DataContext = otherTopic, Background = System.Windows.Media.Brushes.White };
            foreach (var dot in dots)
                panel.Children.Add(new Ellipse { Style = (Style)dict["Yanzi.Chat.GenerationDot." + dot] });
            panel.Children.Add(new Border { Style = (Style)dict["Yanzi.Chat.GenerationCaret"] });
            panel.Measure(new System.Windows.Size(350, 100));
            panel.Arrange(new Rect(0, 0, 350, 100));
            panel.UpdateLayout();
            Assert("real WPF controls accept shared styles and bindings",
                panel.Children.OfType<Ellipse>().All(d => d.ActualWidth == 5));
            otherTopic.Text = "开始输出";
            otherTopic.IsStreaming = false;
            panel.UpdateLayout();
            Assert("binding transitions do not throw on actual WPF controls", !otherTopic.IsReceivingTokens);

            Console.WriteLine("AI_CHAT_GENERATION_QA_OK checks=" + _checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("AI_CHAT_GENERATION_QA_FAILED " + ex);
            return 1;
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void Assert(string message, bool condition)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}

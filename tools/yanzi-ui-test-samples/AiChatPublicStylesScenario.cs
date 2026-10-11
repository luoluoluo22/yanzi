using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yanzi.UiTesting;
using Yanzi.UI.Wpf;

namespace Yanzi.UiTestSamples;

/// <summary>
/// Public AI chat presentation regression. All widgets are rendered on an
/// isolated desktop: no ChatGPT requests, account state, attachments, or live
/// launcher instances are touched.
/// </summary>
public sealed class AiChatPublicStylesScenario : IUiTestScenario
{
    private sealed class TopicViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public string Name => "AI chat public UI regression";
    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var window = new Window { Title = "燕子 AI 对话 · 公共组件回归", Width = 960,
            Height = 700, Background = Brushes.Black };
        YanziUi.ApplyTo(window, YanziTheme.Dark);
        var content = new Grid { Margin = new Thickness(16) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new Border { Padding = new Thickness(12) };
        sidebar.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Sidebar");
        var history = new StackPanel();
        sidebar.Child = history;
        var newTopic = new Button { Content = "＋ 新建对话", Height = 40 };
        newTopic.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Outline");
        history.Children.Add(newTopic);
        var keycap = new YanziKbd { KeyText = "Ctrl K" };
        history.Children.Add(keycap);
        var vm = new TopicViewModel();
        var topic = new Border { DataContext = vm, Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = "新对话 · 带选中状态" } };
        topic.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Topic");
        history.Children.Add(topic);
        Grid.SetColumn(sidebar, 0);
        content.Children.Add(sidebar);

        var conversation = new StackPanel();
        var provider = new ComboBox { Height = 38, Width = 215, HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new [] { "ChatGPT Plan", "OpenAI API" }, SelectedIndex = 0 };
        provider.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Select");
        conversation.Children.Add(provider);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        var rename = new Button { Content = "重命名", Width = 96, Height = 36 };
        rename.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Outline");
        var delete = new Button { Content = "删除", Width = 80, Height = 36 };
        delete.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Destructive");
        actions.Children.Add(rename);
        actions.Children.Add(delete);
        conversation.Children.Add(actions);
        var transcript = new Border { Padding = new Thickness(15), Height = 320 };
        transcript.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Transcript");
        var chatRows = new StackPanel();
        var bot = new Border { Margin = new Thickness(0, 0, 70, 14),
            Child = new TextBlock { Text = "燕子 AI · 您好，有什么可以帮您？", TextWrapping = TextWrapping.Wrap } };
        bot.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Bubble");
        var user = new Border { Margin = new Thickness(70, 0, 0, 14),
            Child = new TextBlock { Text = "帮我检查公共组件是否复用", TextWrapping = TextWrapping.Wrap } };
        user.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.UserBubble");
        var attached = new Border { Margin = new Thickness(0, 0, 110, 6),
            Child = new TextBlock { Text = "attachment.png" } };
        attached.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Attachment");
        chatRows.Children.Add(bot);
        chatRows.Children.Add(user);
        chatRows.Children.Add(attached);
        var toolExpander = new Expander { Header = "工具执行 · 展开详情",
            Content = new TextBlock { Text = "Read-only tool output" }, IsExpanded = false };
        toolExpander.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Expander");
        chatRows.Children.Add(toolExpander);
        transcript.Child = chatRows;
        conversation.Children.Add(transcript);
        var composer = new Border { Margin = new Thickness(0, 10, 0, 0) };
        composer.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Composer");
        var inner = new StackPanel();
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Text = "第一行\n第二行", MinHeight = 44, MaxHeight = 120 };
        input.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Textarea");
        inner.Children.Add(input);
        var footer = new StackPanel { Orientation = Orientation.Horizontal };
        var attach = new Button { Content = "添加附件", Height = 36 };
        attach.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Outline");
        var send = new Button { Content = "发送", Height = 36, Width = 80,
            Margin = new Thickness(8, 0, 0, 0) };
        send.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Default");
        footer.Children.Add(attach);
        footer.Children.Add(send);
        inner.Children.Add(footer);
        composer.Child = inner;
        conversation.Children.Add(composer);
        Grid.SetColumn(conversation, 2);
        content.Children.Add(conversation);
        window.Content = content;

        try
        {
            context.ShowWindow(window);
            await Task.Delay(120, cancellationToken);
            var expected = new (FrameworkElement Element, string Key)[]
            {
                (sidebar,"Yanzi.Chat.Sidebar"), (topic,"Yanzi.Chat.Topic"),
                (transcript,"Yanzi.Chat.Transcript"), (bot,"Yanzi.Chat.Bubble"),
                (user,"Yanzi.Chat.UserBubble"), (attached,"Yanzi.Chat.Attachment"),
                (composer,"Yanzi.Chat.Composer"), (newTopic,"Yanzi.Button.Outline"),
                (delete,"Yanzi.Button.Destructive"), (provider,"Yanzi.Select"),
                (input,"Yanzi.Textarea"), (send,"Yanzi.Button.Default")
            };
            foreach (var (element,key) in expected)
                context.Check(ReferenceEquals(element.Style, element.TryFindResource(key)),
                    "Shared style is resolved: " + key);
            context.Check(user.Background != null &&
                !Equals(user.Background,bot.Background),
                "User and assistant bubbles use different semantic backgrounds");
            context.Check(input.Text.Contains('\n') && input.AcceptsReturn,
                "Shared composer preserves multiline text and Enter handling");
            context.Check(ReferenceEquals(toolExpander.Style,
                toolExpander.TryFindResource("Yanzi.Expander")),
                "Tool-call details use the shared Expander style");
            toolExpander.IsExpanded = true;
            context.Check(toolExpander.IsExpanded,
                "Tool call expansion retains native two-way state");
            context.Check(keycap.KeyText == "Ctrl K" && keycap.Style != null,
                "New conversation shortcut uses public keyboard hint");
            vm.IsSelected = true;
            await Task.Delay(40,cancellationToken);
            context.Check(!Equals(topic.Background,Brushes.Transparent),
                "History topic highlights when selected via DataContext");
            provider.IsDropDownOpen = true;
            await Task.Delay(110,cancellationToken);
            context.Check(provider.IsDropDownOpen && provider.Items.Count == 2,
                "Shared Select dropdown works for provider and model choices");
            provider.SelectedIndex = 1;
            provider.IsDropDownOpen = false;
            context.Check(provider.SelectedItem?.ToString() == "OpenAI API",
                "Switching provider still updates selection");
            var screenshot = context.CaptureWindow(window, "ai-chat-public-ui-dark");
            context.Check(System.IO.File.Exists(screenshot.VisualTreePng),
                "Actual composed AI chat UI captured safely");
            var darkUserBrush = (user.Background as SolidColorBrush)?.Color;
            YanziUi.ApplyTo(window, YanziTheme.Light);
            await Task.Delay(80,cancellationToken);
            context.Check(ReferenceEquals(sidebar.Style,sidebar.TryFindResource("Yanzi.Chat.Sidebar")),
                "Styles remain bound when switching to light theme");
            context.Check((user.Background as SolidColorBrush)?.Color != darkUserBrush,
                "User bubble switches semantic tint with public light/dark theme");
        }
        finally { window.Close(); }
    }
}

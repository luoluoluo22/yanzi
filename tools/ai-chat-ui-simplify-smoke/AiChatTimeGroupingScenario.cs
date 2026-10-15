using System.Windows;
using System.Windows.Controls;
using OpenQuickHost;
using Yanzi.UiTesting;

namespace Yanzi.AiChatUiSmoke;

/// <summary>Data-bound time grouping and transcript simplification without sending requests.</summary>
public sealed class AiChatTimeGroupingScenario : IUiTestScenario
{
    public string Name => "AI chat centered three-minute timestamp dividers";
    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var date = new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.FromHours(8));
        var first = new AiChatMessage(true, "Hello", timestamp: date);
        var near = new AiChatMessage(false, "Reply", timestamp: date.AddMinutes(2).AddSeconds(59));
        var boundary = new AiChatMessage(true, "Followup", timestamp: near.Timestamp.AddMinutes(3));
        var earlier = new AiChatMessage(false, "Older", timestamp: date.AddMinutes(-1));
        var nextDay = new AiChatMessage(true, "Next date", timestamp: date.AddDays(1));
        context.Check(first.Timestamp == date,
            "Stored message timestamps can be reconstructed exactly after reload");
        context.Check(AiChatMessage.NeedsTimeDivider(null, first),
            "First message shows centered timestamp");
        context.Check(!AiChatMessage.NeedsTimeDivider(first, near),
            "Messages closer than three minutes suppress repeated timestamp");
        context.Check(AiChatMessage.NeedsTimeDivider(near, boundary),
            "Exactly three minutes starts new time group");
        context.Check(AiChatMessage.NeedsTimeDivider(near, earlier),
            "A restored earlier timestamp starts a new time group");
        context.Check(AiChatMessage.NeedsTimeDivider(first, nextDay),
            "Crossing calendar date always starts a new group");
        var notifications = 0;
        near.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AiChatMessage.TimeDividerVisibility)) notifications++; };
        near.ShowTimeDivider = true;
        context.Check(near.TimeDividerVisibility == Visibility.Visible && notifications == 1,
            "Time label visibility notifies the WPF binding");
        near.ShowTimeDivider = true;
        context.Check(notifications == 1,
            "Unchanged time group does not notify twice");
        near.ShowTimeDivider = false;
        context.Check(near.TimeDividerVisibility == Visibility.Collapsed && notifications == 2,
            "Time label collapses without leaving blank vertical space");
        context.Check(first.TimeDividerText.Contains("14:00"),
            "Time separator contains original HH:mm rather than load time");

        var ui = new Window { Title = "AI message timestamp regression", Width = 430, Height = 235 };
        var host = new StackPanel { Margin = new Thickness(12) };
        var caption = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        caption.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AiChatMessage.TimeDividerText))
            { Source = first });
        caption.SetBinding(UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(AiChatMessage.TimeDividerVisibility)) { Source = first });
        first.ShowTimeDivider = true;
        host.Children.Add(caption);
        host.Children.Add(new TextBlock { Text = first.Text });
        ui.Content = host;
        try
        {
            context.ShowWindow(ui);
            await Task.Delay(75, cancellationToken);
            context.Check(caption.Visibility == Visibility.Visible &&
                caption.HorizontalAlignment == HorizontalAlignment.Center,
                "Time marker renders centered outside the message body");
            var capture = context.CaptureWindow(ui, "ai-chat-time-divider");
            context.Check(System.IO.File.Exists(capture.VisualTreePng),
                "Rendered timestamp divider screenshot captured");
        }
        finally { ui.Close(); }
    }
}
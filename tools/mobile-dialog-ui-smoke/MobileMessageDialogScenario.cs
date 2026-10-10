using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQuickHost;
using Yanzi.UiTesting;
using Yanzi.UI.Wpf;

namespace Yanzi.MobileDialogUiSmoke;

public sealed class MobileMessageDialogScenario : IUiTestScenario
{
    public string Name => "Mobile dialog public UI integration";

    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var window = new MobileMessageToastWindow();
        try
        {
            context.ShowWindow(window);
            await Task.Delay(140, cancellationToken);
            var device = (Button)window.FindName("DeviceSwitcherButton");
            var close = (Button)window.FindName("CloseButton");
            var clear = (Button)window.FindName("ClearHistoryButton");
            var send = (Button)window.FindName("SendButton");
            var attach = (Button)window.FindName("AttachButton");
            var voice = (Button)window.FindName("VoiceButton");
            var compose = (TextBox)window.FindName("InputTextBox");
            var messageArea = (ScrollViewer)window.FindName("MessageScrollViewer");
            var messageStack = (StackPanel)window.FindName("MessageStack");
            var chatFrame = (Border)window.FindName("ChatFrame");

            context.Check(window.Resources.MergedDictionaries.Count >= 2,
                "Window loaded opt-in shared semantic tokens and control templates");
            context.Check(chatFrame.CornerRadius.TopLeft >= 12 && chatFrame.Background is not null,
                "Shared popover surface and radius are rendered");
            context.Check(device.Style != null && attach.Style != null && voice.Style != null &&
                close.Style != null && clear.Style != null && send.Style != null,
                "All toolbar actions use shared button styles");
            // Preserve content-driven width when switching to the shared Outline
            // variant; Ghost intentionally has no border and looked unfinished here.
            var textWidth = new FormattedText(
                clear.Content.ToString()!, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(clear.FontFamily, clear.FontStyle, clear.FontWeight, clear.FontStretch),
                clear.FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(clear).PixelsPerDip).WidthIncludingTrailingWhitespace;
            var required = textWidth + clear.Padding.Left + clear.Padding.Right + 4;
            context.Check(ReferenceEquals(clear.Style, clear.TryFindResource("Yanzi.Button.Outline")) &&
                          clear.BorderThickness.Left >= 1 &&
                          ReferenceEquals(close.Style, close.TryFindResource("Yanzi.Button.Ghost")),
                "Clear uses shared outlined button with border; close stays borderless Ghost");
            context.Check(double.IsNaN(clear.Width) && clear.MinWidth >= 48,
                "Clear label uses Auto width and minimum without losing shared outline");
            context.Check(clear.ActualWidth >= required - 0.5,
                "Chinese clear label fully fits button text, padding, and 2-DIP chrome inset",
                $"required={required:F1}, actual={clear.ActualWidth:F1}");
            var initialWidth = clear.ActualWidth;
            clear.FontSize += 4;
            window.UpdateLayout();
            context.Check(clear.ActualWidth > initialWidth,
                "Clear button grows when text metrics increase instead of clipping");
            clear.FontSize -= 4;
            window.UpdateLayout();
            context.Check(close.Content is FrameworkElement && attach.Content is FrameworkElement
                && voice.Content is FrameworkElement,
                "Toolbar icons are common font-independent vector icons");
            context.Check(compose.Style != null && compose.AcceptsReturn && compose.TextWrapping == TextWrapping.Wrap,
                "Composer uses shared rounded textarea with multiline editing");
            compose.Text = "第一行\n第二行";
            context.Check(compose.Text.Contains('\n'), "Multiline message text is preserved");
            context.Check(messageArea.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Chat body owns scrolling without forcing a horizontal scrollbar");
            context.Check(messageStack.Children.Count >= 1,
                "Empty state or historical messages rendered");
            context.Check(window.FindName("TargetDevicePicker") is ComboBox,
                "Device selection model remains wired");
            var menu = new YanziDropdownMenu { AlignStart = true, PreferAbove = false };
            menu.UseStandaloneTheme(YanziUi.GetTheme(window));
            menu.UseContentWidth(190);
            var selected = false;
            menu.AddAction("所有设备", () => selected = true);
            menu.AddSubmenu("管理设备", actions =>
                actions.AddAction("删除设备", () => { }));
            menu.ShowFrom(device);
            await Task.Delay(90, cancellationToken);
            context.Check(menu.IsOpen && menu.Count >= 2,
                "Shared dropdown opens from device button without a native ContextMenu");
            context.Check(menu.OpenDepth == 0 && menu.Surface.ActualWidth >= 190,
                "Device dropdown uses the shared content-sized surface");
            var first = menu.Actions[0];
            context.Click(first);
            context.Check(selected && !menu.IsOpen,
                "Device target choice closes dropdown and executes selection");
            var captured = context.CaptureWindow(window, "mobile-dialog-ui-refactored");
            context.Check(System.IO.File.Exists(captured.VisualTreePng),
                "Actual mobile dialog visual tree captured without sending anything");

            // In-memory presentation only; this does not write inbox history or send messages.
            window.AppendMessage("手机发来消息", "测试多行气泡\n第二行", "测试手机",
                DateTimeOffset.Now);
            await Task.Delay(100, cancellationToken);
            context.Check(messageStack.Children.OfType<Border>().Any(),
                "Incoming message bubble still renders with shared theme tokens");
            context.Check(compose.Text == "第一行\n第二行",
                "Receiving a message does not destroy unsent composer text");
        }
        finally
        {
            window.Close();
        }
    }
}

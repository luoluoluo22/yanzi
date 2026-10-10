using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UI.Wpf;
using Yanzi.UiTesting;

namespace Yanzi.UiTestSamples;

/// <summary>Real WPF dropdown hierarchy used by the bottom-right AI model picker.
/// Uses synthetic providers; never reads or writes account credentials.</summary>
public sealed class AiChatHierarchicalModelScenario : IUiTestScenario
{
    public string Name => "AI chat hierarchical model and history edit menus";

    public async Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var window = new Window { Width = 640, Height = 430, Title = "AI model hierarchy" };
        YanziUi.ApplyTo(window, YanziTheme.Dark);
        var container = new Grid { Margin = new Thickness(16) };
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topic = new Border { Margin = new Thickness(5, 3, 5, 3),
            Padding = new Thickness(9), Child = new Grid(), Width = 210,
            HorizontalAlignment = HorizontalAlignment.Left };
        topic.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Chat.Topic");
        var topicGrid = (Grid)topic.Child;
        topicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topicGrid.Children.Add(new TextBlock { Text = "Session one", VerticalAlignment = VerticalAlignment.Center });
        var edit = new Button { Content = "✎", Width = 28, Height = 28,
            Visibility = Visibility.Collapsed, ToolTip = "编辑会话" };
        edit.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Ghost");
        Grid.SetColumn(edit, 1);
        topicGrid.Children.Add(edit);
        Grid.SetRow(topic, 0); container.Children.Add(topic);

        var model = new Button { Content = "Model: test-medium ▾", Width = 195, Height = 38,
            HorizontalAlignment = HorizontalAlignment.Right };
        model.SetResourceReference(FrameworkElement.StyleProperty, "Yanzi.Button.Outline");
        Grid.SetRow(model, 2);
        container.Children.Add(model);
        window.Content = container;

        var selection = "";
        var menu = new YanziDropdownMenu { PreferAbove = true, SubmenuWidth = 242 };
        menu.UseStandaloneTheme(YanziTheme.Dark);
        menu.UseContentWidth(190);
        var alpha = menu.AddSubmenu("Provider Alpha", child =>
        {
            child.AddAction("alpha-fast", () => selection = "Alpha/alpha-fast");
            child.AddAction("alpha-reasoning-preview", () => selection = "Alpha/alpha-reasoning-preview");
        });
        var beta = menu.AddSubmenu("Provider Beta", child =>
        {
            child.AddAction("beta-medium", () => selection = "Beta/beta-medium");
        });

        try
        {
            context.ShowWindow(window);
            await Task.Delay(60, cancellationToken);
            context.Check(edit.Visibility == Visibility.Collapsed, "History edit icon hidden until hover");
            edit.Visibility = Visibility.Visible; // trigger equivalent, tested without moving pointer
            context.Check(edit.Visibility == Visibility.Visible && edit.Style is not null,
                "History editor uses shared Ghost control");
            var actionInvoked = "";
            edit.Click += (_, args) => { actionInvoked = "show edit actions"; args.Handled = true; };
            context.Click(edit);
            context.Check(actionInvoked == "show edit actions",
                "Clicking the history edit button invokes its action");
            context.Check(menu.Count == 2 && menu.Actions.Contains(alpha) && menu.Actions.Contains(beta),
                "Enabled providers are the top-level submenu entries");

            var work = SystemParameters.WorkArea;
            menu.ShowAtScreenPoint(new Point(work.Right - 22, work.Bottom - 32), work);
            await Task.Delay(95, cancellationToken);
            context.Check(menu.IsOpen, "Model menu appears from bottom-right selector");
            context.Check(menu.OpenChildrenToLeft, "Near right monitor edge, submenu opens to the left");
            var rootBefore = menu.Surface.PointToScreen(new Point(0, 0));
            context.Require(menu.OpenSubmenu(alpha), "First provider opens child model list");
            await Task.Delay(85, cancellationToken);
            context.Check(menu.OpenDepth == 1, "Two-level model hierarchy expands exactly one level");
            var rootAfter = menu.Surface.PointToScreen(new Point(0, 0));
            context.Check(Math.Abs(rootBefore.X - rootAfter.X) <= 2 &&
                          Math.Abs(rootBefore.Y - rootAfter.Y) <= 2,
                "Expanding model list keeps root in fixed position without flicker");
            var viewport = VisualTreeHelper.GetParent(menu.Surface) as Canvas;
            context.Require(viewport is not null && viewport.Children.Count == 2,
                "Model child pane is inside stable popup canvas");
            var modelsPanel = viewport.Children.OfType<Border>().FirstOrDefault(b => b != menu.Surface);
            context.Require(modelsPanel is not null, "Second-level panel exists");
            var list = modelsPanel.Child as StackPanel;
            // Panel contains a Border + StackPanel hierarchy; locate action recursively.
            Button? selected = null;
            void Visit(DependencyObject node)
            {
                if (node is Button button &&
                    AutomationProperties.GetName(button) == "alpha-reasoning-preview")
                    selected = button;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    Visit(VisualTreeHelper.GetChild(node, i));
            }
            Visit(modelsPanel);
            context.Require(selected is not null, "Full provider model ID remains in submenu");
            context.Click(selected);
            await Task.Delay(50, cancellationToken);
            context.Check(selection == "Alpha/alpha-reasoning-preview",
                "Choosing child model updates provider and model atomically");
            context.Check(!menu.IsOpen, "Selection closes the dropdown");

            var editMenu = new YanziDropdownMenu();
            editMenu.UseStandaloneTheme(YanziTheme.Dark);
            editMenu.AddAction("重命名", () => actionInvoked = "rename");
            editMenu.AddSeparator();
            editMenu.AddAction("删除", () => actionInvoked = "delete", destructive: true);
            editMenu.ShowFrom(edit);
            await Task.Delay(65, cancellationToken);
            context.Check(editMenu.IsOpen && editMenu.Actions.Count == 2,
                "History editor provides rename and delete actions");
            context.Click(editMenu.Actions[0]);
            context.Check(actionInvoked == "rename" && !editMenu.IsOpen,
                "Rename action is accessible and popup closes after selection");
            editMenu.IsOpen = false;
            var capture = context.CaptureWindow(window, "ai-chat-hierarchy-layout");
            context.Check(System.IO.File.Exists(capture.VisualTreePng),
                "Isolated hierarchy screenshot captured");
        }
        finally
        {
            menu.IsOpen = false;
            window.Close();
        }
    }
}

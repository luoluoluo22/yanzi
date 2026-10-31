using System.Collections;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Yanzi.UI.Wpf;
using OpenQuickHost;

Exception? failure = null;
var thread = new Thread(() =>
{
    try { Verify(); }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure != null) throw failure;
Console.WriteLine("Model menu scroll verification passed.");

static void Assert(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + scenario);
    Console.WriteLine("PASS " + scenario);
}

static Border GetSubmenuPanel(YanziDropdownMenu menu, Button trigger)
{
    var field = typeof(YanziDropdownMenu).GetField("_submenus", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var mapping = (IDictionary)field.GetValue(menu)!;
    var level = mapping[trigger] ?? throw new InvalidOperationException("No submenu created.");
    return (Border)level.GetType().GetField("Panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(level)!;
}

static void Verify()
{
    var many = new YanziDropdownMenu { SubmenuWidth = 340, SubmenuMaxHeight = 440 };
    var trigger = many.AddSubmenu("Gemini", child =>
    {
        for (var i = 0; i < 50; i++)
            child.AddAction($"gemini-test-model-{i:00}", () => { });
    });
    var panel = GetSubmenuPanel(many, trigger);
    Assert(panel.MaxHeight == 440, "50-model menu caps its height");
    Assert(panel.Child is ScrollViewer, "50-model menu has scroll viewer");
    var scroll = (ScrollViewer)panel.Child;
    Assert(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "vertical scrollbar automatic");
    Assert(scroll.Content is StackPanel rows && rows.Children.Count == 50, "all 50 models retained");
    panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    Assert(panel.DesiredSize.Height <= 440.1, "measured height stays within cap");
    panel.Arrange(new Rect(0, 0, 340, 440));
    scroll.UpdateLayout();
    Assert(scroll.ScrollableHeight > 0, "50-model list is actually scrollable");

    var ordinary = new YanziDropdownMenu();
    var normalTrigger = ordinary.AddSubmenu("Options", child => child.AddAction("First", () => { }));
    var normal = GetSubmenuPanel(ordinary, normalTrigger);
    Assert(normal.Child is StackPanel, "existing menus remain unchanged");
    Assert(double.IsInfinity(normal.MaxHeight), "existing menus have no cap");

    var manuallyConfigured = new[] { "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.5-pro" };
    var discovered = Enumerable.Range(0, 50).Select(i => $"catalog-{i:00}")
        .Concat(["gemini-2.5-flash"]);
    var ordered = AiModelMenuOrdering.Order(manuallyConfigured, discovered, "gemini-2.5-flash");
    Assert(ordered.Length == 52, "50 discovered and 2 configured IDs kept without duplicates");
    Assert(ordered[0] == "gemini-2.5-flash", "selected model comes first");
    Assert(ordered[1] == "gemini-2.5-pro", "other configured model comes before discovered");
    Assert(ordered[2] == "catalog-00" && ordered[^1] == "catalog-49",
        "all discovered IDs stay ordered and reachable");
    Assert(manuallyConfigured[0] == "gemini-2.5-pro", "manual configuration unchanged");
}

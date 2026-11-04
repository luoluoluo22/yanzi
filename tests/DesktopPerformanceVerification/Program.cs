using System.Xml.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost;
using Yanzi.UI.Wpf;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        VerifyWriter().GetAwaiter().GetResult();
        VerifyFingerprint();
        var application = new OpenQuickHost.App();
        application.InitializeComponent();
        VerifyTiles();
        VerifyTranscript();
        VerifyWindowEventCoalescing();
        Console.WriteLine("DESKTOP_PERFORMANCE_PASS");
        return 0;
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
    }
    private static async Task VerifyWriter()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writes = new List<int>();
        var errors = 0;
        var active = 0;
        var writer = new LatestSnapshotWriter<int>(value =>
        {
            Check(Interlocked.Increment(ref active) == 1, "single writer");
            try
            {
                if (value == 0) { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }
                if (value == -1) throw new IOException("synthetic failure");
                writes.Add(value);
            }
            finally { Interlocked.Decrement(ref active); }
        }, _ => Interlocked.Increment(ref errors));
        writer.Enqueue("busy", 0);
        Check(entered.Wait(5000), "writer runs off caller thread");
        for (var i = 1; i <= 10000; i++) writer.Enqueue("same-extension", i);
        writer.Enqueue("failure", -1);
        writer.Enqueue("other-extension", 42);
        release.Set();
        await writer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(writes.SequenceEqual(new[] { 0, 10000, 42 }), "10000 queued updates coalesce, independent key survives");
        Check(errors == 1, "failed snapshot does not stop writer");
        writer.Enqueue("restart", 43);
        await writer.WhenIdleAsync();
        Check(writes[^1] == 43, "writer restarts after idle");
    }
    private static void VerifyFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "yanzi-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "extension", "src"));
        Directory.CreateDirectory(Path.Combine(root, "extension", ".yanzi-csharp-cache", "deep"));
        try
        {
            var source = Path.Combine(root, "extension", "src", "entry.cs");
            File.WriteAllText(source, "old");
            File.WriteAllText(Path.Combine(root, "extension", "manifest.json"), "{}");
            var baseline = ExtensionCatalogFingerprint.Compute(root);
            File.WriteAllText(Path.Combine(root, "extension", ".yanzi-csharp-cache", "deep", "generated.cs"), "ignored");
            Check(ExtensionCatalogFingerprint.Compute(root) == baseline, "cache subtree excluded");
            File.WriteAllText(source, "new content");
            var changed = ExtensionCatalogFingerprint.Compute(root);
            Check(changed != baseline, "nested source changes detected");
            File.Move(source, source + ".renamed.cs");
            Check(ExtensionCatalogFingerprint.Compute(root) != changed, "rename detected");
            File.Delete(source + ".renamed.cs");
            Check(ExtensionCatalogFingerprint.EnumerateSources(root).Count() == 1, "deletion detected");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Layout(FrameworkElement element, double width = 480)
    {
        element.Measure(new Size(width, 350));
        element.Arrange(new Rect(0, 0, width, 350));
        element.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        element.UpdateLayout();
    }
    private static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindOrNull<T>(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        throw new InvalidOperationException(typeof(T).Name + " not found");
    }
    private static T? FindOrNull<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindOrNull<T>(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }
    internal static async Task VerifyNativeHost(Yanzi.UiTesting.UiTestContext context)
    {
        Check(HostRuntimeProfile.IsDevelopment && !HostRuntimeProfile.OwnsBackgroundServices, "native host uses isolated dev profile");
        var root = Path.Combine(Path.GetTempPath(), "yanzi-native-performance-" + Guid.NewGuid().ToString("N"));
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        using var scope = (IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { root })!;
        var host = new MainWindow { ShowActivated = false, ShowInTaskbar = false, AllowClose = true };
        try
        {
            context.ShowWindow(host);
            host.SelectedSearchScope = host.SearchScopes.Single(item => item.Key == "ai");
            var topic = new AiChatTopic("Performance fixture");
            for (var i = 0; i < 2000; i++) topic.Messages.Add(new AiChatMessage(i % 2 == 0, "Native message " + i + " **markdown**"));
            host.AiChatTopics.Add(topic);
            host.SelectedAiChatTopic = topic;
            host.UpdateLayout();
            var list = (ListBox)host.FindName("AiChatMessagesList");
            Layout(list, 650);
            var panel = Find<VirtualizingStackPanel>(list);
            Check(VisualTreeHelper.GetChildrenCount(panel) < 70, "native MainWindow transcript remains virtualized");
            list.ScrollIntoView(topic.Messages[0]); Layout(list, 650);
            list.ScrollIntoView(topic.Messages[^1]); Layout(list, 650);
            Check(list.ItemContainerGenerator.ContainerFromIndex(1999) != null, "native MainWindow scrolls entire history");
            context.CaptureWindow(host, "native-transcript");
            host.SelectedSearchScope = host.SearchScopes.First(item => item.Key != "ai" && item.Key != "store");
            host.IsGridViewMode = true;
            host.FilteredCommands.ReplaceAll(Enumerable.Range(0, 1000).Select(i =>
                new CommandItem("*", "Tile " + i, "Fixture", "Fixture", "#4488CC", null, Array.Empty<string>())));
            var grid = (ListBox)host.FindName("CommandList");
            host.UpdateLayout(); Layout(grid, 650);
            var tiles = Find<VirtualizingTilePanel>(grid);
            Check(VisualTreeHelper.GetChildrenCount(tiles) is > 0 and < 100, "native launcher grid virtualizes 1000 commands");
            grid.SelectedIndex = 999; grid.ScrollIntoView(grid.SelectedItem); Layout(grid, 650);
            Check(grid.ItemContainerGenerator.ContainerFromIndex(999) is ListBoxItem { IsSelected: true }, "native grid selects last command");
            var columns = (int)typeof(MainWindow).GetMethod("GetGridColumnCount", flags)!.Invoke(host, null)!;
            Check(columns == tiles.Columns, "native keyboard navigation uses actual grid columns");
            context.CaptureWindow(host, "native-grid");
            var quick = (QuickPanelWindow)typeof(MainWindow).GetField("_quickPanel", flags)!.GetValue(host)!;
            var timer = (DispatcherTimer)typeof(QuickPanelWindow).GetField("_mobileDetectTimer", flags)!.GetValue(quick)!;
            Check(!timer.IsEnabled, "native hidden quick panel has no badge polling");
            quick.Show(); Check(timer.IsEnabled, "native visible quick panel starts badge polling");
            quick.Hide(); Check(!timer.IsEnabled, "native quick panel stops badge polling when hidden again");
            // Exercise the actual asynchronous host refresh against synthetic files.
            var extensionDir = Path.Combine(HostAssets.ExtensionsPath, "perf-fixture");
            Directory.CreateDirectory(extensionDir);
            File.WriteAllText(Path.Combine(extensionDir, "manifest.json"),
                """{"id":"perf-fixture","name":"Performance fixture","version":"1.0.0","runtime":"shell","entryPoint":"entry.ps1"}""");
            File.WriteAllText(Path.Combine(extensionDir, "entry.ps1"), "# Never executed");
            using var watcher = new FileSystemWatcher(HostAssets.ExtensionsPath);
            typeof(MainWindow).GetField("_extensionContentWatcher", flags)!.SetValue(host, watcher);
            var refresh = typeof(MainWindow).GetMethod("RefreshExtensionCatalogAsync", flags)!;
            var uiAdvanced = false;
            var scan = (Task)refresh.Invoke(host, new object[] { false })!;
            _ = host.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => uiAdvanced = true));
            await scan;
            Check(uiAdvanced, "UI dispatcher advances during catalog scan");
            Check(host.GetAllCommands().Any(c => c.ExtensionId == "perf-fixture"), "background catalog scan applies new manifest");
            File.WriteAllText(Path.Combine(extensionDir, "manifest.json"),
                """{"id":"perf-fixture","name":"Updated fixture","version":"1.0.1","runtime":"shell","entryPoint":"entry.ps1"}""");
            var scans = Enumerable.Range(0, 12).Select(_ => (Task)refresh.Invoke(host, new object[] { false })!).ToArray();
            await Task.WhenAll(scans);
            Check(host.GetAllCommands().Any(c => c.ExtensionId == "perf-fixture" && c.Title == "Updated fixture"), "overlapping refreshes retain latest catalog");
            File.Delete(Path.Combine(extensionDir, "manifest.json"));
            await (Task)refresh.Invoke(host, new object[] { false })!;
            Check(!host.GetAllCommands().Any(c => c.ExtensionId == "perf-fixture"), "background catalog refresh removes deleted manifest");
            context.Check(true, "Native transcript, grid, quick-panel lifecycle and asynchronous catalog refresh");
            Console.WriteLine("NATIVE_HOST_QA_ARTIFACT=" + root);
        }
        finally { host.AllowClose = true; host.Close(); }
    }

    private static void VerifyWindowEventCoalescing()
    {
        using var service = new WindowBoundExtensionsService(null!); // No hooks or host are started.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var type = typeof(WindowBoundExtensionsService);
        type.GetField("_started", flags)!.SetValue(service, true);
        var callback = type.GetMethod("HandleWinEvent", flags)!;
        var posted = 0;
        DispatcherHookEventHandler handler = (_, _) => posted++;
        Dispatcher.CurrentDispatcher.Hooks.OperationPosted += handler;
        try
        {
            for (var i = 0; i < 10000; i++)
                callback.Invoke(service, new object[] { IntPtr.Zero, 0x800Bu, new IntPtr(123), 0, 0, 0u, 0u });
            Check(posted == 1, "10000 window location events post one dispatcher operation");
            var pending = (System.Collections.Generic.HashSet<IntPtr>)type.GetField("_pendingWindows", flags)!.GetValue(service)!;
            Check(pending.Count == 1, "duplicate window moves collapse to one target");
            callback.Invoke(service, new object[] { IntPtr.Zero, 0x800Bu, new IntPtr(456), -4, 0, 0u, 0u });
            Check(pending.Count == 1, "child-object movement ignored");
            for (var i = 0; i < 1000; i++)
                callback.Invoke(service, new object[] { IntPtr.Zero, 0x800Bu, new IntPtr(i + 1000), 0, 0, 0u, 0u });
            Check(pending.Count <= 256, "event storm has bounded pending targets");
            service.Stop();
            Check(pending.Count == 0, "stop discards pending targets");
        }
        finally { Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= handler; }
    }

    private static void VerifyTranscript()
    {
        // Load the actual production transcript template, removing only code-behind
        // event handlers (no MainWindow, accounts, listeners or real user data).
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var element = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "AiChatMessagesList");
        element.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", x.NamespaceName);
        element.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "local", "clr-namespace:OpenQuickHost;assembly=Yanzi");
        element.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "ui", "clr-namespace:Yanzi.UI.Wpf;assembly=Yanzi.UI.Wpf");
        foreach (var attr in element.DescendantsAndSelf().Attributes().Where(a =>
            a.Name.LocalName is "Click" or "MouseRightButtonUp" or "MouseLeftButtonUp" or "MouseDown" or "MouseUp").ToArray()) attr.Remove();
        var list = (ListBox)System.Windows.Markup.XamlReader.Parse(element.ToString().Replace("clr-namespace:OpenQuickHost\"", "clr-namespace:OpenQuickHost;assembly=Yanzi\""));
        var messages = new ObservableCollection<AiChatMessage>(Enumerable.Range(0, 2000).Select(i =>
            new AiChatMessage(i % 2 == 0, $"Message {i}\n**Bold** and a long wrapped paragraph " + new string('x', i % 200))));
        list.DataContext = new { AiChatMessages = messages };
        Layout(list, 650);
        var panel = Find<VirtualizingStackPanel>(list);
        var realized = VisualTreeHelper.GetChildrenCount(panel);
        Check(realized is > 0 and < 60, $"actual transcript: 2000 messages realize {realized} containers");
        list.ScrollIntoView(messages[^1]); Layout(list, 650);
        Check(list.ItemContainerGenerator.ContainerFromIndex(1999) != null, "transcript reaches latest message");
        Check(VisualTreeHelper.GetChildrenCount(panel) < 60, "transcript containers remain bounded at end");
        messages[^1].IsStreaming = true;
        messages[^1].Text = new string('z', 4000); Layout(list, 650);
        messages[^1].IsStreaming = false; Layout(list, 650);
        list.ScrollIntoView(messages[0]); Layout(list, 650);
        Check(list.ItemContainerGenerator.ContainerFromIndex(0) != null, "transcript returns to first message after streaming and recycling");
        messages.Clear(); messages.Add(new AiChatMessage(false, "New topic")); Layout(list, 650);
        Check(list.Items.Count == 1 && list.ItemContainerGenerator.ContainerFromIndex(0) != null, "topic replacement renders without stale containers");
    }

    private static void VerifyTiles()
    {
        var data = new ObservableCollection<string>(Enumerable.Range(0, 5000).Select(i => "Tile " + i));
        var list = new ListBox { ItemsSource = data };
        list.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBox">
                <ScrollViewer CanContentScroll="True" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
                    <ItemsPresenter />
                </ScrollViewer>
            </ControlTemplate>
            """);
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel)));
        ScrollViewer.SetCanContentScroll(list, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        Layout(list);
        var panel = Find<VirtualizingTilePanel>(list);
        Check(VisualTreeHelper.GetChildrenCount(panel) is > 0 and < 50, "5000 tiles realize fewer than 50 containers");
        panel.SetVerticalOffset(double.PositiveInfinity);
        Layout(list);
        Check(list.ItemContainerGenerator.ContainerFromIndex(4999) != null, "last tile reachable");
        Check(VisualTreeHelper.GetChildrenCount(panel) < 50, "long scroll keeps bounded containers");
        list.SelectedIndex = 2500;
        list.ScrollIntoView(list.SelectedItem);
        Layout(list);
        Check(list.ItemContainerGenerator.ContainerFromIndex(2500) is ListBoxItem { IsSelected: true }, "keyboard/programmatic selection brings offscreen item into view");
        Layout(list, 220);
        Check(VisualTreeHelper.GetChildrenCount(panel) < 25, "resize recalculates visible columns");
        data.Move(2500, 2499); data.RemoveAt(2499); data[2499] = "replacement";
        Layout(list, 220);
        panel.SetVerticalOffset(0); Layout(list, 220);
        data.Insert(0, "inserted"); Layout(list, 220);
        Check(((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)).Content.Equals("inserted"), "move/remove/replace/insert keep generator consistent");
        for (var i = 0; i < 40; i++)
        {
            data.RemoveAt(0); data.Insert(0, "new " + i); data.Move(0, 2); data[1] = "replaced " + i;
            Layout(list, 220);
            for (var j = 0; j < Math.Min(10, data.Count); j++)
                if (list.ItemContainerGenerator.ContainerFromIndex(j) is ListBoxItem item)
                    if (!Equals(item.Content, data[j])) throw new InvalidOperationException("stale container after collection mutation");
        }
        Check(true, "40 collection mutation batches preserve realized data");
        data.Clear(); Layout(list);
        Check(VisualTreeHelper.GetChildrenCount(panel) == 0 && panel.VerticalOffset == 0, "reset clears containers and clamps offset");
        data.Add("restart"); Layout(list);
        Check(list.ItemContainerGenerator.ContainerFromIndex(0) != null, "empty list can refill");
    }
}

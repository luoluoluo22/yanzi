using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using OpenQuickHost;

internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static readonly string AuditLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yanzi-wpf-ui-smoke-{Environment.ProcessId}.log");
    private static void Log(string message) { System.IO.File.AppendAllText(AuditLog, message + Environment.NewLine); Console.WriteLine(message); }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            System.IO.File.WriteAllText(AuditLog, "START " + DateTime.UtcNow.ToString("O") + Environment.NewLine);
            Log("BEFORE app constructor");
            var app = new App();
            Log("AFTER app constructor");
            typeof(App).GetProperty("IsVerificationHarness", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(app, true);
            Log("VERIFICATION HARNESS ENABLED");
            app.InitializeComponent();
            Log("AFTER app.InitializeComponent");
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Log("AFTER ShutdownMode");
            if (args.Length > 0 && args[0].StartsWith("--schedule-visual-", StringComparison.Ordinal))
                return ScheduleVisualPreview(app, args[0].EndsWith("light", StringComparison.Ordinal));
            Run("SimpleTextInputWindow empty guard + valid submit", () =>
                new SimpleTextInputWindow("QA 输入框", "测试输入", "", false), w =>
            {
                var b = (TextBox)w.FindName("ValueBox")!;
                var error = (TextBlock)w.FindName("ErrorText")!;
                CheckShared(w, b, "Yanzi.Input");
                b.Text = " ";
                ClickByContent(w, "确定", "确认", "保存");
                Assert(error.Visibility == Visibility.Visible, "empty value must show error");
                b.Text = "qa value";
                ClickByContent(w, "确定", "确认", "保存");
                Assert(((SimpleTextInputWindow)w).ValueText == "qa value", "value mismatch");
            }, true);

            Run("WebDavCredentialWindow username/password validation", () =>
                new WebDavCredentialWindow("", true), w =>
            {
                var b = (TextBox)w.FindName("UsernameBox")!;
                var pass = (PasswordBox)w.FindName("PasswordBox")!;
                var error = (TextBlock)w.FindName("ErrorText")!;
                CheckShared(w, b, "Yanzi.Input");
                CheckShared(w, pass, "Yanzi.Password");
                ClickByContent(w, "保存", "确定");
                Assert(error.Visibility == Visibility.Visible, "required username must show error");
                b.Text = "qa-user";
                pass.Password = "qa-test-secret-only";
                ClickByContent(w, "保存", "确定");
                Assert(((WebDavCredentialWindow)w).Username == "qa-user", "username mismatch");
            }, true);

            Run("AiModelPickerWindow selection + already-added disabled", () =>
                new AiModelPickerWindow("QA-Provider", new[] {"existing", "new-model"}, new[] {"existing"}), w =>
            {
                var p = (AiModelPickerWindow)w;
                Assert(p.Items.Count == 2, "model count");
                Assert(!p.Items[0].CanSelect && p.Items[1].CanSelect, "already-added disable policy");
                Assert(p.Items[0].IsSelected, "existing model initially selected");
                Assert(!p.Items[1].IsSelected, "new model initially not selected");
                Assert(w.TryFindResource("Yanzi.CheckBox") is Style, "shared checkbox missing");
                p.Items[1].IsSelected = true;
                Assert(p.SelectedModels.SequenceEqual(new[] { "new-model" }), "selection must exclude existing");
                Assert(p.SummaryText.Contains("已勾选 1"), "live summary missing");
                ClickByContent(w, "添加所选", "确认", "确定", "添加");
            }, true);

            Run("SkillExportOptionsWindow combo interaction", () =>
                new SkillExportOptionsWindow(), w =>
            {
                var target = (ComboBox)w.FindName("TargetComboBox")!;
                var scope = (ComboBox)w.FindName("ScopeComboBox")!;
                CheckShared(w, target, "Yanzi.Select");
                CheckShared(w, scope, "Yanzi.Select");
                Assert(target.Items.Count > 0 && scope.Items.Count > 0, "combos should contain options");
                target.SelectedIndex = target.Items.Count - 1;
                scope.SelectedIndex = scope.Items.Count - 1;
                Assert(((SkillExportOptionsWindow)w).SelectedTarget.ToString().Length > 0, "target reading");
                Assert(((SkillExportOptionsWindow)w).SelectedScope.ToString().Length > 0, "scope reading");
                ClickByContent(w, "开始导出", "确认", "确定", "导出");
            }, true);

            Run("MouseTriggerCaptureWindow shared Select", () =>
                new MouseTriggerCaptureWindow(), w =>
            {
                var combo = (ComboBox)w.FindName("TargetComboBox")!;
                CheckShared(w, combo, "Yanzi.Select");
                Assert(combo.Items.Count > 0, "trigger target options");
                Assert(w.TryFindResource("Yanzi.Button.Outline") is Style, "shared outline button missing");
            }, false);

            Run("HotkeyCaptureWindow shared button and initial value", () =>
                new HotkeyCaptureWindow("QA 快捷键", "仅验证渲染", null, "QA", true), w =>
            {
                var button = (Button)w.FindName("ConfirmButton")!;
                var shared = w.TryFindResource("Yanzi.Button.Default") as Style;
                Assert(shared != null && ReferenceEquals(button.Style, shared), "shared confirmation style missing");
                Assert(!button.IsEnabled, "confirm must stay disabled before a shortcut is recorded");
                Assert(w.FindName("CapturedHotkeyText") is TextBlock, "captured hotkey text");
            }, false);


            Run("ProcessPickerWindow shared buttons + process list", () =>
                new ProcessPickerWindow("QA process picker", "Synthetic test", "notepad.exe", new System.Collections.Generic.List<string>(), false, false), w =>
            {
                var style = w.TryFindResource("Yanzi.Button.Default") as Style;
                Assert(style != null, "process picker shared button resource missing");
                Assert(Descendants(w).OfType<Button>().Any(b => ReferenceEquals(b.Style, style)),
                    "process picker confirmation button missing shared style");
                Assert(w.IsLoaded, "process picker did not load");
            }, false);

            var noteFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yanzi-ui-smoke-note-{Environment.ProcessId}.txt");
            System.IO.File.WriteAllText(noteFile, "synthetic note text for UI verification");
            Run("PastedTextEditorWindow synthetic note load", () =>
                new PastedTextEditorWindow(noteFile, "UI QA synthetic text", null), w =>
            {
                var title = (TextBox)w.FindName("TitleTextBox")!;
                CheckShared(w, title, "Yanzi.Input");
                var content = (TextBox)w.FindName("ContentTextBox")!;
                Assert(content.Text.Contains("synthetic note text"), "note file did not load");
                Assert(w.TryFindResource("Yanzi.Button.Default") is Style, "note editor shared button missing");
            }, false);
            System.IO.File.Delete(noteFile);

            var colorOwner = new Window { Title = "QA Color Owner", Width = 200, Height = 120, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
            colorOwner.Show();
            Run("ColorPickerDialog hexadecimal and alpha inputs", () =>
                new ColorPickerDialog(colorOwner, System.Windows.Media.Colors.CornflowerBlue), w =>
            {
                CheckShared(w, (TextBox)w.FindName("HexInput")!, "Yanzi.Input");
                CheckShared(w, (TextBox)w.FindName("AlphaInput")!, "Yanzi.Input");
                var palette = (Panel)w.FindName("SwatchGrid")!;
                Assert(palette.Children.Count >= 12, "color swatches missing");
            }, false);
            colorOwner.Close();

            Run("InstalledAppPickerDialog open and close", () =>
                new InstalledAppPickerDialog(), w =>
            {
                Assert(w.FindName("SearchBox") is TextBox, "installed app search missing");
                Assert(w.TryFindResource("Yanzi.Button.Outline") is Style, "installed app shared button style missing");
            }, false);


            Run("SearchScopesManagerWindow read-only XAML + controls", () =>
                new SearchScopesManagerWindow((MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow))), w =>
            {
                var list = (ListBox)w.FindName("ScopesListBox")!;
                Assert(list != null, "search scope list missing");
                Assert(w.TryFindResource("Yanzi.CheckBox") is Style, "search scope shared checkbox resource missing");
                var everything = (CheckBox)w.FindName("EnableEverythingCheckBox")!;
                CheckShared(w, everything, "Yanzi.CheckBox");
            }, false);

            Run("SystemPromptEditorWindow cancel preserves original", () =>
                new SystemPromptEditorWindow("original prompt"), w =>
            {
                var editor = (TextBox)w.FindName("PromptTextBox")!;
                Assert(editor.Text == "original prompt", "initial system prompt lost");
                Assert(editor.AcceptsReturn && editor.AcceptsTab, "multiline input behavior changed");
                var outline = w.TryFindResource("Yanzi.Button.Outline") as Style;
                var cancel = Descendants(w).OfType<Button>().First(b => Equals(b.Content, "取消"));
                Assert(outline != null && ReferenceEquals(cancel.Style, outline), "shared cancel style missing");
                ClickByContent(w, "取消");
                Assert(((SystemPromptEditorWindow)w).PromptText == string.Empty,
                    "cancel must not commit edited prompt");
            }, false);

            Run("SystemPromptEditorWindow confirms multiline prompt", () =>
                new SystemPromptEditorWindow("old text"), w =>
            {
                var editor = (TextBox)w.FindName("PromptTextBox")!;
                Assert(editor.IsVisible && editor.IsEnabled, "editor must be interactive");
                editor.Text = "line one\n\tline two";
                var primary = w.TryFindResource("Yanzi.Button.Default") as Style;
                var confirm = Descendants(w).OfType<Button>().First(b => Equals(b.Content, "确认"));
                Assert(primary != null && ReferenceEquals(confirm.Style, primary), "shared primary style missing");
                ClickByContent(w, "确认");
                Assert(((SystemPromptEditorWindow)w).PromptText == "line one\n\tline two",
                    "confirm must retain exact newline and tab");
            }, true);

            Run("AddProviderWindow required name, type selection and submit", () =>
                new AddProviderWindow(), w =>
            {
                var input = (TextBox)w.FindName("ProviderNameBox")!;
                var types = (ComboBox)w.FindName("ProviderTypeBox")!;
                var error = (TextBlock)w.FindName("ErrorText")!;
                CheckShared(w, input, "Yanzi.Input");
                CheckShared(w, types, "Yanzi.Select");
                Assert(types.Items.Count >= 8, "provider types unexpectedly missing");
                Assert((string)types.SelectedItem == "OpenAI", "default provider type changed");
                Assert(w.TryFindResource("Yanzi.Brush.Danger") != null, "shared error color unavailable");
                input.Text = "  ";
                ClickByContent(w, "确定");
                Assert(w.IsVisible, "empty name must not close dialog");
                Assert(error.Visibility == Visibility.Visible, "empty name error not displayed");
                input.Text = "  QA Synthetic Provider  ";
                types.SelectedItem = "Gemini";
                ClickByContent(w, "确定");
                var dialog = (AddProviderWindow)w;
                Assert(dialog.ProviderName == "QA Synthetic Provider", "provider trim behavior changed");
                Assert(dialog.ProviderType == "Gemini", "provider type selection lost");
            }, true);

            Run("AddProviderWindow cancel preserves no result", () =>
                new AddProviderWindow(), w =>
            {
                var outline = w.TryFindResource("Yanzi.Button.Outline") as Style;
                var cancel = Descendants(w).OfType<Button>().First(b => Equals(b.Content, "取消"));
                Assert(outline != null && ReferenceEquals(cancel.Style, outline), "shared cancel style not used");
                ((TextBox)w.FindName("ProviderNameBox")!).Text = "not to be saved";
                ClickByContent(w, "取消");
                Assert(((AddProviderWindow)w).ProviderName == "", "cancel must not save name");
            }, false);

            Run("ScheduleConfigWindow shared actions + disabled schedule", () =>
                new ScheduleConfigWindow(""), w =>
            {
                var outline = w.TryFindResource("Yanzi.Button.Outline") as Style;
                var primary = w.TryFindResource("Yanzi.Button.Default") as Style;
                var cancel = Descendants(w).OfType<Button>().First(b => Equals(b.Content, "取消"));
                var confirm = Descendants(w).OfType<Button>().First(b => Equals(b.Content, "确认"));
                Assert(outline != null && ReferenceEquals(cancel.Style, outline), "schedule cancel shared style missing");
                Assert(primary != null && ReferenceEquals(confirm.Style, primary), "schedule confirm shared style missing");
                Assert(((CheckBox)w.FindName("EnableToggle")!).IsChecked == false, "empty schedule must be disabled");
                ClickByContent(w, "确认");
                Assert(((ScheduleConfigWindow)w).ResultSchedule == "", "disabled schedule should stay empty");
            }, true);

            Run("ScheduleConfigWindow cancel does not overwrite schedule", () =>
                new ScheduleConfigWindow("0 9 * * *"), w =>
            {
                Assert(((CheckBox)w.FindName("EnableToggle")!).IsChecked == true, "existing schedule must be enabled");
                ClickByContent(w, "取消");
                Assert(((ScheduleConfigWindow)w).ResultSchedule == "", "cancel must not save a new schedule");
            }, false);


            Run("Schedule accessible choices + native dialog keyboard defaults", () =>
                new ScheduleConfigWindow("0 9 * * *"), w =>
            {
                var choices = Descendants(w).OfType<ScheduleChoiceBorder>().ToArray();
                Assert(choices.Length == 61, $"expected 61 accessible choices, got {choices.Length}");
                Assert(choices.All(c => c.Focusable), "some schedule choices cannot receive keyboard focus");
                foreach (var choice in choices)
                {
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(choice);
                    Assert(peer != null, "schedule choice has no UI Automation peer");
                    Assert(peer.GetAutomationControlType() == System.Windows.Automation.Peers.AutomationControlType.Button, "choice not announced as button");
                    Assert(!string.IsNullOrWhiteSpace(peer.GetName()), "choice has missing accessibility name");
                    Assert(peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is
                        System.Windows.Automation.Provider.IInvokeProvider, "choice missing invoke pattern");
                }
                Assert(((CheckBox)w.FindName("EnableToggle")!).Focusable, "schedule toggle not keyboard focusable");
                Assert(Descendants(w).OfType<Button>().Any(b => b.IsCancel && Equals(b.Content, "取消")),
                    "Esc cancel button missing");
                Assert(Descendants(w).OfType<Button>().Any(b => b.IsDefault && Equals(b.Content, "确认")),
                    "Enter confirm button missing");
                ClickByContent(w, "取消");
            }, false);

            Run("Schedule keyboard choice interaction + cron integration", () =>
                new ScheduleConfigWindow("0 9 * * *"), w =>
            {
                var cron = (ScheduleChoiceBorder)w.FindName("ModeCronBtn")!;
                var simple = (ScheduleChoiceBorder)w.FindName("ModeSimpBtn")!;
                var minute = (ScheduleChoiceBorder)w.FindName("FreqMinBtn")!;
                var interval = (ScheduleChoiceBorder)w.FindName("Min15Btn")!;
                foreach (var choice in new[] { cron, simple, minute, interval })
                {
                    choice.Focus();
                    var source = System.Windows.PresentationSource.FromVisual(w);
                    var key = new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, source, Environment.TickCount,
                        System.Windows.Input.Key.Space)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
                    choice.RaiseEvent(key);
                    Assert(key.Handled, "space activation not handled: " + choice.Name);
                }
                Assert(((StackPanel)w.FindName("SimpleModePanel")!).Visibility == Visibility.Visible,
                    "keyboard mode switch did not restore simple panel");
                ClickByContent(w, "确认");
                Assert(((ScheduleConfigWindow)w).ResultSchedule == "*/15 * * * *",
                    "keyboard setting should round-trip to 15-minute cron");
            }, true);

            Run("Schedule semantic colors in both theme variants", () =>
                new ScheduleConfigWindow("0 9 * * *"), w =>
            {
                foreach (var theme in new[] { Yanzi.UI.Wpf.YanziTheme.Light, Yanzi.UI.Wpf.YanziTheme.Dark })
                {
                    Yanzi.UI.Wpf.YanziUi.ApplyTo(w, theme);
                    var bg = Descendants(w).OfType<Border>().FirstOrDefault(
                        b => b.Child is StackPanel && b.Margin.Left == 1);
                    Assert(bg != null, "schedule outer surface not found");
                    var expected = w.TryFindResource("Yanzi.Color.Background") as System.Windows.Media.Brush;
                    Assert(expected != null && Equals(bg.Background, expected),
                        "schedule background is not bound to semantic theme " + theme);
                    Assert(Descendants(w).OfType<TextBlock>().Any(t => Equals(t.Foreground,
                        w.TryFindResource("Yanzi.Color.Foreground"))), "semantic text color missing " + theme);
                }
                ClickByContent(w, "取消");
            }, false);
            Log($"RESULT pass={_pass} fail={_fail}");
            app.Shutdown();
            return _fail == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log("BOOT_FAILURE "+ex);
            return 2;
        }
    }

    private static int ScheduleVisualPreview(App app, bool light)
    {
        var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yanzi-schedule-visual-qa");
        System.IO.Directory.CreateDirectory(output);
        var w = new ScheduleConfigWindow("0 9 * * *");
        var theme = light ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(w, theme);
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Topmost = true;
        w.PreviewKeyDown += (_, e) => Log("PHYSICAL_KEY=" + e.Key + " focus=" + (System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.Name);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timer.Tick += (_, _) => { timer.Stop(); w.Close(); };
        w.Loaded += (_, _) =>
        {
            w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                w.Activate();
                w.UpdateLayout();
                var width = Math.Max(1, (int)Math.Ceiling(w.ActualWidth));
                var height = Math.Max(1, (int)Math.Ceiling(w.ActualHeight));
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(w);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                var png = System.IO.Path.Combine(output, "schedule-" + (light ? "light" : "dark") + ".png");
                using (var fs = System.IO.File.Create(png)) encoder.Save(fs);
                var rootBorder = Descendants(w).OfType<Border>().FirstOrDefault();
                var bg = rootBorder?.Background as System.Windows.Media.SolidColorBrush;
                Log("VISUAL_UI theme=" + theme + " visual_background=" + bg?.Color.ToString() +
                    " png=" + png + " hwnd=" + new System.Windows.Interop.WindowInteropHelper(w).Handle);
                timer.Start();
            });
        };
        w.ShowDialog();
        app.Shutdown();
        return 0;
    }

    private static void Run(string label, Func<Window> make, Action<Window> check, bool expectSubmit)
    {
        Window? w = null;
        Exception? fail = null;
        bool called = false;
        bool? result = null;
        try
        {
            Log("BEFORE make " + label);
            w = make();
            Log("AFTER make " + label);
            w.Loaded += (_, _) =>
            {
                w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    try { called = true; check(w); }
                    catch (Exception e) { fail = e; }
                    finally { if (w.IsVisible) w.Close(); }
                });
            };
            Log("BEFORE ShowDialog " + label);
            result = w.ShowDialog();
            Log("AFTER ShowDialog " + label);
            if (fail != null) throw fail;
            Assert(called, "loaded check not invoked");
            if (expectSubmit) Assert(result == true, $"submit dialog result must be true, got {result}");
            Log($"PASS {label} result={result}");
            _pass++;
        }
        catch (Exception ex)
        {
            Log($"FAIL {label}: {ex}");
            _fail++;
            try { w?.Close(); } catch { }
        }
    }

    private static void CheckShared(Window w, Control control, string key)
    {
        var resource = w.TryFindResource(key) as Style;
        Assert(resource != null, $"resource {key} unavailable");
        Assert(ReferenceEquals(control.Style, resource), $"control {control.Name} has wrong Style {key}");
        Assert(control.IsVisible && control.IsEnabled, $"control {control.Name} unavailable");
    }

    private static void ClickByContent(Window window, params string[] names)
    {
        var button = Descendants(window).OfType<Button>()
            .FirstOrDefault(b => names.Any(n => string.Equals(b.Content?.ToString()?.Trim(), n, StringComparison.Ordinal)));
        Assert(button != null, "could not locate button: " + string.Join("/", names));
        button!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i=0; i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OpenQuickHost;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [STAThread]
    static int Main()
    {
        try
        {
            if (!HostRuntimeProfile.IsDevelopment) throw new Exception("Run with --dev for isolation.");
            var root = Path.Combine(Path.GetTempPath(), "YanziDetachedVerification", Guid.NewGuid().ToString("N"));
            using var data = (IDisposable)typeof(HostAssets).GetMethod(
                "UseIsolatedDataRootForVerification", PrivateStatic)!.Invoke(null, [root])!;
            var app = new App();
            typeof(App).GetProperty("IsVerificationHarness", PrivateInstance)!.SetValue(app, true);
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Read actual HWND properties, without registering or modifying any
            // Start Menu shortcuts or user pins (including existing Dev pins).
            var detached = new Window
            {
                Title = "AI verification", ShowInTaskbar = false, ShowActivated = false,
                Opacity = 0, Left = -10000, Top = -10000
            };
            var hwnd = new WindowInteropHelper(detached).EnsureHandle();
            var identity = typeof(MainWindow).Assembly.GetType("OpenQuickHost.AiChatTaskbarIdentity")!;
            identity.GetMethod("ConfigureWindowProperties", PrivateStatic)!.Invoke(null,
                [hwnd, @"C:\Program Files\Yanzi\Yanzi.exe", @"C:\QA Icons\ai.ico", "Yanzi.QA.Unique", detached.Title]);
            var iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out var store));
            try
            {
                Check(Read(store, 2) == "\"C:\\Program Files\\Yanzi\\Yanzi.exe\" --dev --ai-chat", "quoted AI relaunch preserves development channel");
                Check(Read(store, 3) == @"C:\QA Icons\ai.ico,0", "persistent AI icon resource");
                Check(Read(store, 4) == detached.Title, "mandatory relaunch display name");
                Check(Read(store, 5) == "Yanzi.QA.Unique", "window-specific AppID");
            }
            finally { Marshal.ReleaseComObject(store); }

            var host = new MainWindow();
            var scope = host.SelectedSearchScope;
            var ai = host.SearchScopes.Single(s => s.Key.Equals("ai", StringComparison.OrdinalIgnoreCase));
            // Reproduce the important detached state: the panel is not in the
            // launcher's tree, and a live detached window owns it.
            var panel = (FrameworkElement)host.FindName("AiChatDockPanel");
            var parent = (Grid)panel.Parent;
            parent.Children.Remove(panel);
            detached.Content = panel;
            var field = typeof(MainWindow).GetField("_detachedAiChatWindow", PrivateInstance)!;
            field.SetValue(host, detached);
            detached.Show();
            for (var i = 0; i < 3; i++)
            {
                detached.WindowState = WindowState.Minimized;
                host.SelectedSearchScope = ai;
                Check(ReferenceEquals(host.SelectedSearchScope, scope), "AI selection preserves launcher scope");
                Check(!host.IsAiChatMode && host.NormalLauncherVisibility == Visibility.Visible, "launcher content remains available");
                Check(detached.WindowState == WindowState.Normal, "existing conversation restored from minimized");
                Check(ReferenceEquals(detached.Content, panel), "same conversation panel retained");
            }
            detached.Content = null;
            parent.Children.Add(panel);
            field.SetValue(host, null);
            detached.Close();
            host.SelectedSearchScope = ai;
            Check(host.IsAiChatMode && host.AiChatVisibility == Visibility.Visible, "embedded AI selection works after detached window closes");
            app.Shutdown();
            Console.WriteLine("AI_DETACHED_WINDOW_VERIFICATION_PASSED (isolated HWND/state tests; physical pinning not covered)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static string? Read(IPropertyStore store, uint id)
    {
        var key = new PropertyKey { FormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = id };
        Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
        try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid FormatId; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}

using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class MobileExtensionCatalogVerification
{
    public static Task RunAsync()
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(() => {
            try {
                using var scope=HostAssets.UseIsolatedDataRootForVerification(Path.Combine(Path.GetTempPath(),"YanziMobileCatalog-"+Guid.NewGuid().ToString("N")));
                var app=new App {IsVerificationHarness=true};app.InitializeComponent();
                var window=new MainWindow();app.MainWindow=window;
                var directory=Path.Combine(HostAssets.ExtensionsPath,"catalog-fixture");Directory.CreateDirectory(directory);
                var manifest=Path.Combine(directory,"manifest.json");
                File.WriteAllText(manifest,"""{"id":"catalog-fixture","name":"目录验证","description":"fixture","version":"0.1.0","icon":"mdi:test-tube","runtime":"csharp","entryMode":"entry","entry":"main.cs","waitForExit":true}""");
                File.WriteAllText(Path.Combine(directory,"main.cs"),"""
                    using System.Threading.Tasks;
                    using OpenQuickHost.CSharpRuntime;
                    public static class YanziAction { public static Task<string> RunAsync(YanziActionContext context) => Task.FromResult("fresh-manifest-ran"); }
                    """);
                var method=typeof(MainWindow).GetMethod("ExecuteMobileDeviceMessageAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
                DeviceMessageRecord Request() => new() {
                    MessageId=Guid.NewGuid().ToString("N"),SourceDeviceId="android-fixture",Kind="run-extension",Text="test",
                    Payload=new() {["extensionId"]=JsonSerializer.SerializeToElement("catalog-fixture")}
                };
                _=Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => {
                    try {
                        var first=await (Task<(bool hasResult,bool success,string output)>)method.Invoke(window,[Request()])!;
                        if(!first.success || first.output!="fresh-manifest-ran")
                            throw new Exception("Newly installed miniapp was invisible to the cached mobile execution entry.");
                        window.ReloadLocalExtensionsFromExternal();
                        File.Delete(manifest);
                        var removed=await (Task<(bool hasResult,bool success,string output)>)method.Invoke(window,[Request()])!;
                        if(removed.success) throw new Exception("Deleted cached miniapp still executed.");
                        Console.WriteLine("MOBILE_EXTENSION_CATALOG=PASSED (install after host startup; deleted cached item rejected)");
                        done.TrySetResult();
                    }catch(Exception e){done.TrySetException(e);}
                    finally {Dispatcher.CurrentDispatcher.InvokeShutdown();}
                }));
                Dispatcher.Run();
            }catch(Exception e){done.TrySetException(e);}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return done.Task;
    }
}

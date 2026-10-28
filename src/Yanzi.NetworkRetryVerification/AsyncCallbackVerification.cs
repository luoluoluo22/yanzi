using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenQuickHost;

internal static class AsyncCallbackVerification
{
    internal static async Task RunAsync()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public static class Callbacks
            {
                static Timer? timer;
                static async void Method() { await Task.Delay(10); throw new Exception("method"); }
                public static void Run()
                {
                    Method();
                    async void Local() => await Fail("local");
                    Local();
                    Action simple = async () => await Fail("lambda");
                    simple();
                    Action<object> arg = async x => { await Task.Yield(); throw new Exception("simple"); };
                    arg(null!);
                    Action anonymous = async delegate { await Task.Yield(); throw new Exception("anonymous"); };
                    anonymous();
                    timer = new Timer(async _ => {
                        await Task.Yield();
                        try { throw new Exception("original"); }
                        catch { throw new Exception("logging-failed"); }
                    }, null, 10, Timeout.Infinite);
                }
                static async Task Fail(string message) { await Task.Yield(); throw new Exception(message); }
                public static async Task TaskFailure() => await Fail("task");
                public static Task LambdaFailure() { Func<Task> action = async () => await Fail("task-lambda"); return action(); }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "Action.cs");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("CallbackVerification", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var guard = typeof(HostAssets).Assembly.GetType("OpenQuickHost.CSharpAsyncCallbackGuard")!;
        compilation = (CSharpCompilation)guard.GetMethod("Protect", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [compilation, tree])!;
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var errors = new ConcurrentQueue<string>();
        assembly.GetType("__YanziAsyncCallbackGuard")!.GetField("ErrorHandler")!.SetValue(null,
            (Action<Exception>)(error => { errors.Enqueue(error.Message); throw new Exception("reporting-failed"); }));
        var callbacks = assembly.GetType("Callbacks")!;
        callbacks.GetMethod("Run")!.Invoke(null, null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (errors.Count < 6) await Task.Delay(20, timeout.Token);
        var expected = new[] { "method", "local", "lambda", "simple", "anonymous", "logging-failed" };
        if (!errors.Order().SequenceEqual(expected.Order())) throw new Exception("Callback failures were not reported exactly once.");
        foreach (var name in new[] { "TaskFailure", "LambdaFailure" })
        {
            try { await (Task)callbacks.GetMethod(name)!.Invoke(null, null)!; throw new Exception("Task failure was swallowed."); }
            catch (Exception ex) when (ex.Message is "task" or "task-lambda") { }
        }
        Console.WriteLine("Async callback verification passed: timer, methods, local functions, all lambda forms, reporting failure containment, preserved Task exceptions.");
    }
}

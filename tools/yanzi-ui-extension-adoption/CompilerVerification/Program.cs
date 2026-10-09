using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using OpenQuickHost;

var files = args.Length > 0
    ? args
    : [Path.Combine(Path.GetTempPath(), "yanzi-ui-v2-candidates", "semantic.cs"),
       Path.Combine(Path.GetTempPath(), "yanzi-ui-v2-candidates", "CapabilityLab.cs")];
var hostType = typeof(ScriptExtensionRunner);
string SharedConst(string name) => (string)(hostType
    .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
    .GetRawConstantValue() ?? throw new InvalidOperationException(name));
var globals = SharedConst("CSharpGlobalUsingsSource");
var runtime = SharedConst("CSharpRuntimeSource");
var metadata = (IReadOnlyList<MetadataReference>)hostType
    .GetMethod("BuildCSharpMetadataReferences", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, null)!;

Console.WriteLine($"REFERENCES={metadata.Count}");
var ui = metadata.FirstOrDefault(x =>
    string.Equals(Path.GetFileName(x.Display), "Yanzi.UI.Wpf.dll", StringComparison.OrdinalIgnoreCase));
if (ui == null) throw new Exception("Host compiler failed to include trusted shared UI assembly.");
Console.WriteLine($"PUBLIC_UI_REFERENCE={ui.Display}");
foreach (var file in files)
{
    var source = File.ReadAllText(file, Encoding.UTF8);
    var options = new CSharpParseOptions(LanguageVersion.Latest);
    var trees = new[] {
        CSharpSyntaxTree.ParseText(SourceText.From(globals, Encoding.UTF8), options, path:"YanziGlobalUsings.cs"),
        CSharpSyntaxTree.ParseText(SourceText.From(runtime, Encoding.UTF8), options, path:"YanziRuntime.cs"),
        CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), options, path:Path.GetFileName(file))
    };
    var compilation = CSharpCompilation.Create("YanziExtension", trees, metadata,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Enable));
    using var stream = new MemoryStream();
    var result = compilation.Emit(stream);
    if (!result.Success)
    {
        foreach (var error in result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Take(12))
            Console.WriteLine(error);
        throw new Exception("Extension failed to compile: " + file);
    }
    stream.Position = 0;
    var alc = new AssemblyLoadContext("ui-compile-check-" + Guid.NewGuid().ToString("N"), isCollectible:true);
    alc.Resolving += (_, name) => name.Name == "Yanzi.UI.Wpf" ? typeof(Yanzi.UI.Wpf.YanziUi).Assembly : null;
    var extension = alc.LoadFromStream(stream);
    if (extension.GetType("YanziAction") == null)
        throw new Exception("Expected extension entry YanziAction not found.");
    Console.WriteLine($"PASS {Path.GetFileName(file)} compiled={stream.Length} bytes sharedUi=direct");
    alc.Unload();
}
// Exercise real WPF theme resource lookup, not merely successful Roslyn linking.
Exception? uiError = null;
var sta = new System.Threading.Thread(() =>
{
    try
    {
        var window = new System.Windows.Window
        {
            Width = 360, Height = 180, ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None, ShowActivated = false,
            Opacity = 0
        };
        Yanzi.UI.Wpf.YanziUi.ApplyTo(window, Yanzi.UI.Wpf.YanziTheme.Dark);
        var input = Yanzi.UI.Wpf.YanziUi.WithStyle(new System.Windows.Controls.TextBox { Text = "Test" },
            Yanzi.UI.Wpf.YanziUi.Styles.Input);
        var button = Yanzi.UI.Wpf.YanziUi.WithStyle(new System.Windows.Controls.Button { Content = "Search" },
            Yanzi.UI.Wpf.YanziUi.Styles.PrimaryButton);
        var layout = new System.Windows.Controls.StackPanel();
        layout.Children.Add(input);
        layout.Children.Add(button);
        window.Content = layout;
        window.Show();
        window.UpdateLayout();
        if (input.Style is null || button.Style is null)
            throw new InvalidOperationException("Shared UI styles did not resolve in a rendered WPF window");
        window.Close();
    }
    catch (Exception ex) { uiError = ex; }
});
sta.SetApartmentState(System.Threading.ApartmentState.STA);
sta.Start();
sta.Join();
if (uiError is not null) throw new Exception("Rendered WPF public UI smoke failed", uiError);
Console.WriteLine("PASS WPF primary button and input styles resolve in a rendered STA window");
Console.WriteLine($"PASS {files.Length}/{files.Length} compiled against host UI metadata.");

using System.IO;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Markup;
using Yanzi.UiTesting;

public sealed class NativePerformanceScenario : IUiTestScenario
{
    public string Name => "Native desktop performance regression";
    public Task RunAsync(UiTestContext context, CancellationToken cancellationToken)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(NativePerformanceScenario).Assembly.Location)!;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(assemblyDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? System.Reflection.Assembly.LoadFrom(candidate) : null;
        };
        // Runner owns the Application. Load the host's actual resources without
        // running App.OnStartup or attaching to any production account/services.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(Path.GetDirectoryName(typeof(NativePerformanceScenario).Assembly.Location)!, "App.xaml"));
        var resources = document.Root!.Element(wpf + "Application.Resources")!.Element(wpf + "ResourceDictionary")!;
        resources.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        resources.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:OpenQuickHost;assembly=Yanzi");
        resources.SetAttributeValue(XNamespace.Xmlns + "shell", "clr-namespace:System.Windows.Shell;assembly=PresentationFramework");
        foreach (var attribute in resources.Descendants().Attributes("Source"))
            if (attribute.Value.StartsWith("Themes/")) attribute.Value = "/Yanzi;component/" + attribute.Value;
        var text = resources.ToString().Replace("clr-namespace:OpenQuickHost\"", "clr-namespace:OpenQuickHost;assembly=Yanzi\"");
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(text));
        return Program.VerifyNativeHost(context);
    }
}

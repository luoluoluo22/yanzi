using System;

namespace OpenQuickHost.CSharpRuntime;

public sealed class YanziActionContext
{
    public string ExtensionDirectory { get; set; } = "";
    public string ExtensionId { get; set; } = "video-resume";
    public string LaunchSource { get; set; } = "test";
    public Func<string, object?>? GetRegisteredObject { get; set; }
    public Action<string, object>? RegisterObject { get; set; }
    public void Log(string message) { }
}

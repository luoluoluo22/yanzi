using System;
using System.Collections.Generic;
using System.IO;

namespace OpenQuickHost.CSharpRuntime;

public sealed record YanziActionContext(
    string ExtensionId,
    string Title,
    string ExtensionDirectory,
    string ExtensionDataDirectory,
    string InputText,
    string LaunchSource,
    DateTimeOffset Now,
    IReadOnlyList<string> Permissions,
    IReadOnlyDictionary<string, string> State,
    string AgentApiBaseUrl,
    string AgentApiToken)
{
    public string StateUpdatePath { get; set; } = string.Empty;
    public Action<string, object>? RegisterObject { get; set; }
    public Func<string, object?>? GetRegisteredObject { get; set; }

    public void Log(string message)
    {
        try
        {
            var path = Path.Combine(ExtensionDirectory, "debug.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}

namespace OpenQuickHost.CSharpRuntime
{
    public sealed class YanziActionContext
    {
        public string? InputText { get; init; }
        public string LaunchSource { get; init; } = "";
        public string ExtensionDataDirectory { get; init; } = "";
        public string AgentApiBaseUrl { get; init; } = "";
        public string AgentApiToken { get; init; } = "";
        public void Log(string x) { }
        public void ShowDesktopNotification(string a, string b) { }
    }
}

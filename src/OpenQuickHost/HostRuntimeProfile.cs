namespace OpenQuickHost;

/// <summary>Process-wide channel identity, resolved before any settings or logs are opened.</summary>
public static class HostRuntimeProfile
{
    public static bool IsRuntime { get; } =
        string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath), "Yanzi.Runtime", StringComparison.OrdinalIgnoreCase)
        || Environment.GetCommandLineArgs().Any(arg => arg.Equals("--runtime", StringComparison.OrdinalIgnoreCase));
    public static bool IsShell => !IsRuntime;
    public static string? RuntimeDataRoot { get; } = ResolveRuntimeDataRoot();
    public static bool OwnsBackgroundServices => IsRuntime;
    public static bool IsDevelopment { get; } = !IsRuntime && ResolveDevelopment(Environment.GetCommandLineArgs());
    public static string AppId => IsRuntime ? RuntimeDataRoot == null ? "Yanzi.OpenQuickHost" : "Yanzi.Runtime.Test." + RuntimeRpc.PipeName
        : IsDevelopment ? "Yanzi.OpenQuickHost.Dev.Shell" : "Yanzi.OpenQuickHost.Shell";
    public static string DataDirectoryName => IsDevelopment ? "OpenQuickHost.Dev" : "OpenQuickHost";
    public static string DisplayName => IsRuntime ? "燕子 Runtime" : IsDevelopment ? "燕子 Dev" : "燕子";
    public static bool GlobalListenersEnabled => OwnsBackgroundServices && RuntimeDataRoot == null;
    public const int DevelopmentApiPort = 53920;

    private static string? ResolveRuntimeDataRoot()
    {
        if (!IsRuntime) return null;
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, arg => arg.Equals("--runtime-data-root", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        if (index + 1 >= args.Length || !System.IO.Path.IsPathFullyQualified(args[index + 1]))
            throw new ArgumentException("Runtime 验证目录必须是绝对路径。");
        return System.IO.Path.GetFullPath(args[index + 1]);
    }

    private static bool ResolveDevelopment(string[] args)
    {
        if (args.Any(arg => arg.Equals("--dev", StringComparison.OrdinalIgnoreCase))) return true;
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    internal static AppSettings ApplySettings(AppSettings settings)
    {
        if (!IsDevelopment) return settings;
        settings.LaunchAtStartup = false;
        settings.EnableAutoUpdate = false;
        settings.EnableLanSync = false;
        settings.EnableWanPush = false;
        settings.EnableWebDavSync = false;
        settings.PersonalSync.Enabled = false;
        settings.EnableEverything = false;
        settings.EnableWindowSnapAssist = false;
        if (settings.AgentApiPort == 53919) settings.AgentApiPort = DevelopmentApiPort;
        return settings;
    }
}

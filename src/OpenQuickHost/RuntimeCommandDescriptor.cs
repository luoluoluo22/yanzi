using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>UI metadata only; the Runtime resolves execution against its own catalog.</summary>
public sealed record RuntimeCommandDescriptor(
    string Id, string Title, string Subtitle, string Glyph, string Category, string Accent,
    string? OpenTarget, string[] Keywords, CommandSource Source, string Version,
    string? Directory, string? Shortcut, string? Runtime, string? UiMode, string? Entry,
    string? EntryMode, string? InlineSource, string[] Permissions, string? Icon,
    ExtensionStartupDefinition? Startup, AppExtensionDefinition? App, HostedPluginViewDefinition? HostedView,
    string[] QueryPrefixes, string? QueryTarget, string? HotkeyBehavior, string? LaunchArguments,
    string? WorkingDirectory, bool ToggleWindow, bool RunAsAdmin, bool WaitForExit)
{
    public static RuntimeCommandDescriptor FromCommand(CommandItem c) => new(
        c.ExtensionId, c.Title, c.Subtitle, c.Glyph, c.Category, c.AccentBrush.ToString(),
        c.OpenTarget, c.Keywords.ToArray(), c.Source, c.DeclaredVersion,
        c.ExtensionDirectoryPath, c.GlobalShortcut, c.Runtime, c.UiMode, c.EntryPoint,
        c.EntryMode, c.InlineScriptSource, c.Permissions.ToArray(), c.IconReference,
        c.Startup, c.App, c.HostedView, c.QueryPrefixes.ToArray(), c.QueryTargetTemplate,
        c.HotkeyBehavior, c.LaunchArguments, c.WorkingDirectory, c.ToggleWindow, c.RunAsAdmin, c.WaitForExit);

    public CommandItem ToCommand() => new(Glyph, Title, Subtitle, Category, Accent,
        OpenTarget, Keywords, Source, Id, Version, Directory, App, QueryPrefixes, QueryTarget,
        HostedView, Shortcut, HotkeyBehavior, Runtime, UiMode, Entry, Permissions, EntryMode,
        InlineSource, Icon, Startup, LaunchArguments, WorkingDirectory,
        toggleWindow: ToggleWindow, runAsAdmin: RunAsAdmin, waitForExit: WaitForExit);
}

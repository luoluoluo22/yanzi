namespace Yanzi.Shared;

public class CommandItem
{
    public string ExtensionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public string? VectorIconData { get; set; }
    public string? Glyph { get; set; }
    public string? Description { get; set; }
    public CommandActionKind ActionKind { get; set; } = CommandActionKind.None;
    public string? ShortcutKey { get; set; }
    public bool ShortcutCommand { get; set; } = true;
    public bool ShortcutShift { get; set; }
    public bool ShortcutOption { get; set; }
    public bool ShortcutControl { get; set; }
    public string? ApplicationName { get; set; }
    public string? ScriptSource { get; set; }
    public string? SnippetText { get; set; }
    public string? Abbreviation { get; set; }
    public string? GlobalHotkey { get; set; }
}

public enum CommandActionKind
{
    None,
    KeyboardShortcut,
    LaunchApplication,
    AppleScript,
    Snippet
}

public interface ICommandActionExecutor
{
    void Execute(CommandItem command);
}

public sealed class DisabledCommandActionExecutor : ICommandActionExecutor
{
    public void Execute(CommandItem command)
    {
    }
}

public class RadialSeparatorViewModel
{
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public string GeometryData => FormattableString.Invariant($"M {X1},{Y1} L {X2},{Y2}");

    public RadialSeparatorViewModel(double x1, double y1, double x2, double y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }
}

public static class MouseTriggerModes
{
    public const string None = "none";
    public const string RightDrag = "rightdrag";
    public const string RightLongPress = "rightlongpress";
    public const string MiddleDown = "middledown";
    public const string X1Down = "x1down";
    public const string X2Down = "x2down";
    public const string HorizontalWheel = "horizontalwheel";
    public const string CtrlLeftClick = "ctrlleftclick";
    public const string CtrlRightClick = "ctrlrightclick";
    public const string MiddleLongPress = "middlelongpress";
    public const string MiddleClick = "middleclick";

    public static string Normalize(string mode)
    {
        return mode?.Trim().ToLowerInvariant() ?? None;
    }
}

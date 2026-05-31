using System.Diagnostics;
using System.Runtime.InteropServices;
using Yanzi.Shared;

namespace Yanzi.Platform.Mac;

public sealed class MacCommandActionExecutor : ICommandActionExecutor
{
    public void Execute(CommandItem command)
    {
        switch (command.ActionKind)
        {
            case CommandActionKind.KeyboardShortcut:
                SendShortcut(command);
                break;
            case CommandActionKind.LaunchApplication:
                LaunchApplication(command.ApplicationName);
                break;
            case CommandActionKind.AppleScript:
                RunAppleScript(command.ScriptSource);
                break;
        }
    }

    private static void RunAppleScript(string? scriptSource)
    {
        if (string.IsNullOrWhiteSpace(scriptSource))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                ArgumentList = { "-e", scriptSource },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to run AppleScript: {ex}");
        }
    }

    private static void SendShortcut(CommandItem command)
    {
        if (!TryResolveKeyCode(command.ShortcutKey, out var keyCode))
            return;

        var flags = CGEventFlags.None;
        if (command.ShortcutCommand)
            flags |= CGEventFlags.MaskCommand;
        if (command.ShortcutShift)
            flags |= CGEventFlags.MaskShift;
        if (command.ShortcutOption)
            flags |= CGEventFlags.MaskAlternate;
        if (command.ShortcutControl)
            flags |= CGEventFlags.MaskControl;

        PostKeyboardEvent(keyCode, true, flags);
        Thread.Sleep(18);
        PostKeyboardEvent(keyCode, false, flags);
    }

    private static void LaunchApplication(string? applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName))
            return;

        if (applicationName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            applicationName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { applicationName },
                UseShellExecute = false
            });
        }
        else if (applicationName.StartsWith("/"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { applicationName },
                UseShellExecute = false
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                ArgumentList = { "-a", applicationName },
                UseShellExecute = false
            });
        }
    }

    private static void PostKeyboardEvent(ushort keyCode, bool keyDown, CGEventFlags flags)
    {
        var eventRef = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (eventRef == IntPtr.Zero)
            return;

        try
        {
            CGEventSetFlags(eventRef, flags);
            CGEventPost(CGEventTapLocation.HID, eventRef);
        }
        finally
        {
            CFRelease(eventRef);
        }
    }

    private static bool TryResolveKeyCode(string? key, out ushort keyCode)
    {
        keyCode = 0;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return KeyCodes.TryGetValue(key.Trim().ToLowerInvariant(), out keyCode);
    }

    private static readonly Dictionary<string, ushort> KeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 0x00,
        ["s"] = 0x01,
        ["d"] = 0x02,
        ["f"] = 0x03,
        ["h"] = 0x04,
        ["g"] = 0x05,
        ["z"] = 0x06,
        ["x"] = 0x07,
        ["c"] = 0x08,
        ["v"] = 0x09,
        ["b"] = 0x0B,
        ["q"] = 0x0C,
        ["w"] = 0x0D,
        ["e"] = 0x0E,
        ["r"] = 0x0F,
        ["y"] = 0x10,
        ["t"] = 0x11,
        ["1"] = 0x12,
        ["2"] = 0x13,
        ["3"] = 0x14,
        ["4"] = 0x15,
        ["6"] = 0x16,
        ["5"] = 0x17,
        ["="] = 0x18,
        ["9"] = 0x19,
        ["7"] = 0x1A,
        ["-"] = 0x1B,
        ["8"] = 0x1C,
        ["0"] = 0x1D,
        ["]"] = 0x1E,
        ["o"] = 0x1F,
        ["u"] = 0x20,
        ["["] = 0x21,
        ["i"] = 0x22,
        ["p"] = 0x23,
        ["l"] = 0x25,
        ["j"] = 0x26,
        ["'"] = 0x27,
        ["k"] = 0x28,
        [";"] = 0x29,
        ["\\"] = 0x2A,
        [","] = 0x2B,
        ["/"] = 0x2C,
        ["n"] = 0x2D,
        ["m"] = 0x2E,
        ["."] = 0x2F,
        ["tab"] = 0x30,
        ["space"] = 0x31,
        ["delete"] = 0x33,
        ["backspace"] = 0x33,
        ["escape"] = 0x35
    };

    private enum CGEventTapLocation : uint
    {
        HID = 0
    }

    [Flags]
    private enum CGEventFlags : ulong
    {
        None = 0,
        MaskShift = 1UL << 17,
        MaskControl = 1UL << 18,
        MaskAlternate = 1UL << 19,
        MaskCommand = 1UL << 20
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr @event, CGEventFlags flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(CGEventTapLocation tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}

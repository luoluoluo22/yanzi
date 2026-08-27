using System;
using System.Runtime.InteropServices;

namespace Yanzi.Platform.Mac;

public static class MacInputResetHelper
{
    public static void ResetKeyboardAndMouseState()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            MacLogger.WriteLog("ResetHelper", "Resetting all keyboard modifier keys and mouse state...");

            // 1. Post explicit KeyUp for all modifier keys
            // Left Cmd: 0x37, Right Cmd: 0x36
            // Left Shift: 0x38, Right Shift: 0x3C
            // Left Option: 0x3A, Right Option: 0x3D
            // Left Control: 0x3B, Right Control: 0x3E
            // CapsLock: 0x39, Fn: 0x3F
            ushort[] modifierKeyCodes = [0x37, 0x36, 0x38, 0x3C, 0x3A, 0x3D, 0x3B, 0x3E, 0x39, 0x3F];
            foreach (var key in modifierKeyCodes)
            {
                PostKeyboardEvent(key, false, CGEventFlags.None);
            }

            // 2. Post explicit FlagsChanged event with 0 flags (clears OS modifier latch)
            var flagsEvent = CGEventCreate(IntPtr.Zero);
            CGPoint currentPoint = new CGPoint(0, 0);
            if (flagsEvent != IntPtr.Zero)
            {
                try
                {
                    currentPoint = CGEventGetLocation(flagsEvent);
                    CGEventSetType(flagsEvent, CGEventType.FlagsChanged);
                    CGEventSetFlags(flagsEvent, CGEventFlags.None);
                    CGEventPost(CGEventTapLocation.HID, flagsEvent);
                }
                finally
                {
                    CFRelease(flagsEvent);
                }
            }

            // 3. Post explicit MouseUp for Left and Right mouse buttons
            PostMouseEvent(CGEventType.LeftMouseUp, currentPoint);
            PostMouseEvent(CGEventType.RightMouseUp, currentPoint);

            MacLogger.WriteLog("ResetHelper", "Keyboard and mouse state reset completed successfully.");
        }
        catch (Exception ex)
        {
            MacLogger.WriteLog("ResetHelper", $"ResetKeyboardAndMouseState failed: {ex.GetType().Name} - {ex.Message}");
        }
    }

    public static void PostFlagsChanged(CGEventFlags flags)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var eventRef = CGEventCreate(IntPtr.Zero);
            if (eventRef == IntPtr.Zero) return;
            try
            {
                CGEventSetType(eventRef, CGEventType.FlagsChanged);
                CGEventSetFlags(eventRef, flags);
                CGEventPost(CGEventTapLocation.HID, eventRef);
            }
            finally
            {
                CFRelease(eventRef);
            }
        }
        catch {}
    }

    private static void PostKeyboardEvent(ushort keyCode, bool keyDown, CGEventFlags flags)
    {
        var eventRef = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, keyDown);
        if (eventRef == IntPtr.Zero) return;
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

    private static void PostMouseEvent(CGEventType type, CGPoint loc)
    {
        var mouseEvent = CGEventCreateMouseEvent(IntPtr.Zero, type, loc, CGMouseButton.Left);
        if (mouseEvent == IntPtr.Zero) return;
        try
        {
            CGEventSetFlags(mouseEvent, CGEventFlags.None);
            CGEventPost(CGEventTapLocation.HID, mouseEvent);
        }
        finally
        {
            CFRelease(mouseEvent);
        }
    }

    #region P/Invoke

    private enum CGEventTapLocation : uint
    {
        HID = 0,
        Session = 1
    }

    private enum CGEventType : uint
    {
        LeftMouseDown = 1,
        LeftMouseUp = 2,
        RightMouseDown = 3,
        RightMouseUp = 4,
        MouseMoved = 5,
        LeftMouseDragged = 6,
        RightMouseDragged = 7,
        KeyDown = 10,
        KeyUp = 11,
        FlagsChanged = 12
    }

    private enum CGMouseButton : uint
    {
        Left = 0,
        Right = 1,
        Center = 2
    }

    [Flags]
    public enum CGEventFlags : ulong
    {
        None = 0,
        MaskShift = 1UL << 17,
        MaskControl = 1UL << 18,
        MaskAlternate = 1UL << 19,
        MaskCommand = 1UL << 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;

        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, CGEventType mouseType, CGPoint mouseCursorPosition, CGMouseButton mouseButton);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetType(IntPtr @event, CGEventType type);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr @event, CGEventFlags flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(CGEventTapLocation tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    #endregion
}

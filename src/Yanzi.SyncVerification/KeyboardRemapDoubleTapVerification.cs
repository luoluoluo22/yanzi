using OpenQuickHost;

internal static class KeyboardRemapDoubleTapVerification
{
    public static void Run()
    {
        const uint injected = 0x10;
        const uint otherFlags = 0x02;
        nint marker = (nint)KeyboardDoubleTapService.MappedModifierInputTag;

        Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(0, 0, 0xA2), "Hardware Ctrl must work");
        Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(0, 0, 0xA4), "Hardware Alt must work");
        foreach (var modifier in new[] { 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, marker, modifier),
                "Tagged remapped modifier ignored: " + modifier);
            Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, 0, modifier),
                "Remote synthetic modifier blocked: " + modifier);
            Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, (nint)1234, modifier),
                "Remote synthetic modifier with arbitrary tag blocked: " + modifier);
            Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(otherFlags, 0, modifier),
                "Physical extended modifier rejected: " + modifier);
        }
        foreach (var otherKey in new[] { 0x1B, 0x5B, 0x20, 0x41 })
            Ensure(!KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, marker, otherKey),
                "Tagged injected non-modifier accepted: " + otherKey);
        Ensure(KeyboardDoubleTapService.NormalizeModifierVk(0x11, 0, 0) == 0xA2, "Generic Ctrl must normalize to left");
        Ensure(KeyboardDoubleTapService.NormalizeModifierVk(0x11, 1, 0) == 0xA3, "Extended Ctrl must normalize to right");
        Ensure(KeyboardDoubleTapService.NormalizeModifierVk(0x12, 0, 0) == 0xA4, "Generic Alt must normalize to left");
        Ensure(KeyboardDoubleTapService.NormalizeModifierVk(0x12, 1, 0) == 0xA5, "Extended Alt must normalize to right");
        VerifyCtrlTriggersOnSecondKeyDown();
        VerifyRemoteInjectedChords();
        Console.WriteLine("KEYBOARD_REMAP_DOUBLE_TAP_VERIFIED remote-injected=accepted remap=accepted nonmodifiers=blocked ctrl-second-down=passed chords=passed");
    }

    private static void VerifyCtrlTriggersOnSecondKeyDown()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var type = typeof(KeyboardDoubleTapService);
        var keyDown = type.GetMethod("HandleKeyDown", flags)
            ?? throw new InvalidOperationException("Missing keyboard key-down handler");
        var keyUp = type.GetMethod("HandleKeyUp", flags)
            ?? throw new InvalidOperationException("Missing keyboard key-up handler");
        var resetKeys = type.GetMethod("ResetKeyState", flags)
            ?? throw new InvalidOperationException("Missing keyboard state reset");
        var lastKind = type.GetField("_lastTapKind", flags)!;
        var lastTime = type.GetField("_lastTapTimestamp", flags)!;
        var sequenceDirty = type.GetField("_sequenceDirty", flags)!;
        var triggeredCtrl = type.GetField("_triggeredCtrlVk", flags)!;

        bool Down(int vk) => (bool)keyDown.Invoke(null, new object[] { vk })!;
        bool Up(int vk) => (bool)keyUp.Invoke(null, new object[] { vk })!;
        void Reset()
        {
            resetKeys.Invoke(null, null);
            lastKind.SetValue(null, Enum.ToObject(lastKind.FieldType, 0));
            lastTime.SetValue(null, 0L);
            sequenceDirty.SetValue(null, false);
        }

        try
        {
            KeyboardDoubleTapService.ApplyConfiguredShortcut("DoubleCtrl");
            Reset();
            Ensure(!Down(0xA2), "First Ctrl down must not launch");
            Ensure(!Down(0xA2), "Held Ctrl auto-repeat must not launch");
            Ensure(!Up(0xA2), "First Ctrl up must not launch");
            Ensure(Down(0xA2), "Second Ctrl DOWN must launch immediately");
            Ensure((int)triggeredCtrl.GetValue(null)! == 0xA2, "Second down was not latched");
            Ensure(Down(0xA2), "Held second Ctrl auto-repeat must remain suppressed");
            Ensure(Up(0xA2), "Second Ctrl up must be consumed without re-trigger");
            Ensure((int)triggeredCtrl.GetValue(null)! == 0, "Second Ctrl release was not cleared");
            Ensure(!Down(0xA2), "Third Ctrl down must start a fresh pair");
            Ensure(!Up(0xA2), "Third Ctrl up must not launch");
            Ensure(Down(0xA2), "Fourth Ctrl down must launch as a new pair");
            Ensure(Up(0xA2), "Fourth Ctrl up must be consumed");

            Reset();
            Ensure(!Down(0xA2) && !Up(0xA2), "Left Ctrl first tap failed");
            Ensure(Down(0xA3), "Right Ctrl should complete a left-right double tap");
            Ensure(Up(0xA3), "Right Ctrl matching release not consumed");

            Reset();
            Ensure(!Down(0xA2), "Ctrl chord first key down failed");
            Ensure(!Down(0x43), "Letter C down unexpectedly suppressed");
            Ensure(!Up(0xA2), "Ctrl+C release unexpectedly suppressed");
            Ensure(!Down(0xA2), "Ctrl+C must not count as the first Ctrl tap");
            Ensure(!Up(0xA2), "Single Ctrl release unexpectedly triggered");
            Ensure(Down(0xA2), "First clean Ctrl tap should permit a new double tap");
            Ensure(Up(0xA2), "Clean second Ctrl up must be consumed");

            Reset();
            Ensure(!Down(0xA2) && !Up(0xA2), "Timed first tap failed");
            lastTime.SetValue(null, Environment.TickCount64 - 500);
            Ensure(!Down(0xA2), "Expired 350ms window incorrectly launched");
            Ensure(!Up(0xA2), "Expired window Ctrl up unexpectedly launched");

            Reset();
            KeyboardDoubleTapService.ApplyConfiguredShortcut("DoubleAlt");
            Ensure(!Down(0xA2) && !Up(0xA2) && !Down(0xA2),
                "DoubleCtrl must stay disabled when DoubleAlt is selected");
            Ensure(!Up(0xA2), "Disabled Ctrl up unexpectedly launched");
        }
        finally
        {
            KeyboardDoubleTapService.ApplyConfiguredShortcut("DoubleCtrl");
            Reset();
        }
    }

    private static void VerifyRemoteInjectedChords()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var type = typeof(KeyboardDoubleTapService);
        var keyDown = type.GetMethod("HandleKeyDown", flags)!;
        var keyUp = type.GetMethod("HandleKeyUp", flags)!;
        var ignored = type.GetMethod("TrackIgnoredInjectedKey", flags)!;
        var resetKeys = type.GetMethod("ResetKeyState", flags)!;
        var lastKind = type.GetField("_lastTapKind", flags)!;
        var lastTime = type.GetField("_lastTapTimestamp", flags)!;
        var dirty = type.GetField("_sequenceDirty", flags)!;

        bool Down(int vk) => (bool)keyDown.Invoke(null, new object[] { vk })!;
        bool Up(int vk) => (bool)keyUp.Invoke(null, new object[] { vk })!;
        void Other(int vk, int msg) => ignored.Invoke(null, new object[] { vk, msg });
        void Reset()
        {
            resetKeys.Invoke(null, null);
            lastKind.SetValue(null, Enum.ToObject(lastKind.FieldType, 0));
            lastTime.SetValue(null, 0L);
            dirty.SetValue(null, false);
            KeyboardDoubleTapService.ApplyConfiguredShortcut("DoubleCtrl");
        }

        try
        {
            Reset();
            Ensure(!Down(0xA2) && !Up(0xA2), "First remote Ctrl tap must pass");
            Ensure(Down(0xA2) && Up(0xA2), "Second remote Ctrl press must trigger immediately");

            Reset();
            Ensure(!Down(0xA2), "Remote Ctrl+C down must pass");
            Other(0x43, 0x0100);
            Other(0x43, 0x0101);
            Ensure(!Up(0xA2), "Remote Ctrl+C release must pass");
            Ensure(!Down(0xA2), "Remote Ctrl+C must not seed double Ctrl");
            Ensure(!Up(0xA2), "Standalone Ctrl up must pass");
            Ensure(Down(0xA2) && Up(0xA2), "Clean second Ctrl tap should still work");

            Reset();
            Other(0xA0, 0x0100);
            Ensure(!Down(0xA2) && !Up(0xA2), "Remote Ctrl+Shift triggered shortcut");
            Other(0xA0, 0x0101);
            Ensure(!Down(0xA2) && !Up(0xA2), "Shift polluted following Ctrl tap");

            Reset();
            Ensure(!Down(0xA2) && !Up(0xA2), "First clean Ctrl tap must pass");
            Other(0x41, 0x0100);
            Ensure(!Down(0xA2) && !Up(0xA2), "Remote injected letter did not invalidate double tap");
        }
        finally { Reset(); }
    }

    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
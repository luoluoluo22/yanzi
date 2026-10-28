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
            Ensure(!KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, 0, modifier),
                "Untagged synthetic modifier accepted: " + modifier);
            Ensure(!KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, (nint)1234, modifier),
                "Other injected modifier accepted: " + modifier);
            Ensure(KeyboardDoubleTapService.ShouldProcessDoubleTapKey(otherFlags, 0, modifier),
                "Physical extended modifier rejected: " + modifier);
        }
        foreach (var otherKey in new[] { 0x1B, 0x5B, 0x20, 0x41 })
            Ensure(!KeyboardDoubleTapService.ShouldProcessDoubleTapKey(injected, marker, otherKey),
                "Tagged injected non-modifier accepted: " + otherKey);
        Console.WriteLine("KEYBOARD_REMAP_DOUBLE_TAP_VERIFIED physical=2 remapped=4 otherInjectedBlocked=12 nonModifierBlocked=4");
    }

    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}

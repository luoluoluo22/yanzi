namespace OpenQuickHost;

internal static class SettingsTriggerController
{
    internal static void ApplyMouseTriggerModeToRadialFlags(RadialMenuSettings radial)
    {
        var mode = MouseTriggerModes.Normalize(radial.MouseTriggerMode);
        switch (mode)
        {
            case MouseTriggerModes.MiddleDown:
                radial.TriggerMiddleButtonDown = true;
                break;
            case MouseTriggerModes.X1Down:
                radial.TriggerX1ButtonDown = true;
                break;
            case MouseTriggerModes.X2Down:
                radial.TriggerX2ButtonDown = true;
                break;
            case MouseTriggerModes.CtrlLeftClick:
                radial.TriggerCtrlLeftClick = true;
                break;
            case MouseTriggerModes.CtrlLeftDrag:
                radial.TriggerCtrlLeftDrag = true;
                break;
            case MouseTriggerModes.CtrlRightClick:
                radial.TriggerCtrlRightClick = true;
                break;
            case MouseTriggerModes.CtrlMiddleClick:
                radial.TriggerCtrlMiddleClick = true;
                break;
            case MouseTriggerModes.MiddleLongPress:
                radial.TriggerMiddleButtonLongPress = true;
                break;
            case MouseTriggerModes.RightLongPress:
                radial.TriggerRightButtonLongPress = true;
                break;
            case MouseTriggerModes.RightDrag:
                radial.TriggerRightButtonDrag = true;
                break;
            case MouseTriggerModes.MiddleDrag:
                radial.TriggerMiddleButtonDrag = true;
                break;
            case MouseTriggerModes.HorizontalWheel:
                radial.TriggerHorizontalWheel = true;
                break;
        }
    }

    internal static void SyncRadialMouseTriggerModeFromFlags(RadialMenuSettings radial)
    {
        radial.MouseTriggerMode =
            radial.TriggerRightButtonDrag ? MouseTriggerModes.RightDrag :
            radial.TriggerMiddleButtonDrag ? MouseTriggerModes.MiddleDrag :
            radial.TriggerRightButtonLongPress ? MouseTriggerModes.RightLongPress :
            radial.TriggerMiddleButtonLongPress ? MouseTriggerModes.MiddleLongPress :
            radial.TriggerMiddleButtonDown ? MouseTriggerModes.MiddleDown :
            radial.TriggerX1ButtonDown ? MouseTriggerModes.X1Down :
            radial.TriggerX2ButtonDown ? MouseTriggerModes.X2Down :
            radial.TriggerHorizontalWheel ? MouseTriggerModes.HorizontalWheel :
            radial.TriggerCtrlLeftClick ? MouseTriggerModes.CtrlLeftClick :
            radial.TriggerCtrlLeftDrag ? MouseTriggerModes.CtrlLeftDrag :
            radial.TriggerCtrlRightClick ? MouseTriggerModes.CtrlRightClick :
            radial.TriggerCtrlMiddleClick ? MouseTriggerModes.CtrlMiddleClick :
            MouseTriggerModes.None;
    }

    internal static void ApplyMouseTriggerModeToYanmFlags(YanmSettings yanm)
    {
        var mode = MouseTriggerModes.Normalize(yanm.MouseTriggerMode);
        switch (mode)
        {
            case MouseTriggerModes.MiddleDown:
                yanm.TriggerMiddleButtonDown = true;
                break;
            case MouseTriggerModes.X1Down:
                yanm.TriggerX1ButtonDown = true;
                break;
            case MouseTriggerModes.X2Down:
                yanm.TriggerX2ButtonDown = true;
                break;
            case MouseTriggerModes.CtrlLeftClick:
                yanm.TriggerCtrlLeftClick = true;
                break;
            case MouseTriggerModes.CtrlLeftDrag:
                yanm.TriggerCtrlLeftDrag = true;
                break;
            case MouseTriggerModes.CtrlRightClick:
                yanm.TriggerCtrlRightClick = true;
                break;
            case MouseTriggerModes.CtrlMiddleClick:
                yanm.TriggerCtrlMiddleClick = true;
                break;
            case MouseTriggerModes.MiddleLongPress:
                yanm.TriggerMiddleButtonLongPress = true;
                break;
            case MouseTriggerModes.RightLongPress:
                yanm.TriggerRightButtonLongPress = true;
                break;
            case MouseTriggerModes.RightDrag:
                yanm.TriggerRightButtonDrag = true;
                break;
            case MouseTriggerModes.MiddleDrag:
                yanm.TriggerMiddleButtonDrag = true;
                break;
            case MouseTriggerModes.HorizontalWheel:
                yanm.TriggerHorizontalWheel = true;
                break;
        }
    }

    internal static void SyncYanmMouseTriggerModeFromFlags(YanmSettings yanm)
    {
        yanm.MouseTriggerMode =
            yanm.TriggerRightButtonDrag ? MouseTriggerModes.RightDrag :
            yanm.TriggerMiddleButtonDrag ? MouseTriggerModes.MiddleDrag :
            yanm.TriggerRightButtonLongPress ? MouseTriggerModes.RightLongPress :
            yanm.TriggerMiddleButtonLongPress ? MouseTriggerModes.MiddleLongPress :
            yanm.TriggerMiddleButtonDown ? MouseTriggerModes.MiddleDown :
            yanm.TriggerX1ButtonDown ? MouseTriggerModes.X1Down :
            yanm.TriggerX2ButtonDown ? MouseTriggerModes.X2Down :
            yanm.TriggerHorizontalWheel ? MouseTriggerModes.HorizontalWheel :
            yanm.TriggerCtrlLeftClick ? MouseTriggerModes.CtrlLeftClick :
            yanm.TriggerCtrlLeftDrag ? MouseTriggerModes.CtrlLeftDrag :
            yanm.TriggerCtrlRightClick ? MouseTriggerModes.CtrlRightClick :
            yanm.TriggerCtrlMiddleClick ? MouseTriggerModes.CtrlMiddleClick :
            MouseTriggerModes.None;
    }
    internal static bool HasRequiredYanmShortcut(YanmSettings yanm, bool required) =>
        !required || !string.Equals(yanm.ActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase)
                  || !string.IsNullOrWhiteSpace(yanm.CustomShortcut);
}
